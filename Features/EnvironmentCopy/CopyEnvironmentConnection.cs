using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// A fresh DI scope and background session prevent either Data Access identity from replacing the browser login.
internal sealed class CopyEnvironmentConnection(IServiceScope scope, MoveItSessionState state) : IAsyncDisposable
{
    public MoveItSessionState State { get; } = state;
    public async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var session = scope.ServiceProvider.GetRequiredService<IMoveItSessionService>().PushBackgroundSession(State);
        using var server = ObjectCheckout.ForServer(State.WebAdminUrl);
        return await action(scope.ServiceProvider);
    }
    public async ValueTask DisposeAsync()
    {
        try { await UseAsync(async services => { await services.GetRequiredService<IMoveItRestClient>().LogoutAsync(); return true; }); }
        catch { /* The isolated token expires normally; never replace a completed copy outcome with a logout error. */ }
        State.Password = ""; scope.Dispose();
    }
    public static async Task<CopyEnvironmentConnection> OpenAsync(IServiceScopeFactory scopes, IDataAccessCredentialService credentials, string environment, CancellationToken ct)
    {
        var candidates = credentials.GetConfiguredForEnvironment(environment);
        if (candidates.Count == 0) throw new InvalidOperationException($"Configure Data Access credentials for environment '{environment}' before copying.");
        foreach (var credential in candidates)
        {
            ct.ThrowIfCancellationRequested(); var scope = scopes.CreateScope();
            try
            {
                var rest = scope.ServiceProvider.GetRequiredService<IMoveItRestClient>();
                var state = await rest.AuthenticateAsync(credential.ServerKey, credential.Username, credential.Password, ct);
                state.Info = await rest.GetInfoAsync(state, ct);
                if (!state.Info.IsAdmin) throw new InvalidOperationException("The Data Access account must be a MOVEit administrator.");
                return new(scope, state);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is MoveItApiException or HttpRequestException or InvalidOperationException)
            { scope.Dispose(); }
            catch { scope.Dispose(); throw; } // Cancellation/unexpected failures must release the temporary scope too.
        }
        throw new InvalidOperationException($"Data Access authentication failed for environment '{environment}'. Verify its configured account and MOVEit administrator permissions.");
    }
}
