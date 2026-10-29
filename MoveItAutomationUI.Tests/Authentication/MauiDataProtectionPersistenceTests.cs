// Project: MAUI - MOVEit Automation UI
// Exercises real ASP.NET Core antiforgery cryptography across different DI containers / app lifetimes.
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MoveItAutomationUI.Services.Authentication;

namespace MoveItAutomationUI.Tests.Authentication;

public sealed class MauiDataProtectionPersistenceTests
{
    [Fact]
    public void KeyPathRemainsOutsideWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MAUI-config-test");
        Assert.Equal(Path.Combine(root, "Data", "DataProtection-Keys"),
            MauiDataProtectionConfiguration.GetKeyDirectory(root));
    }

    [Fact]
    public async Task LoginAntiforgeryTokenRemainsValidAcrossAppServiceProviderRestartOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return; // Machine-level DPAPI applies only to Windows.

        var root = Path.Combine(Path.GetTempPath(), "maui-dp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        try
        {
            string cookiePair;
            string formToken;
            using (var first = BuildAppServices(root))
            {
                var http = CreateContext(first, "GET");
                var tokens = first.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
                formToken = Assert.IsType<string>(tokens.RequestToken);
                cookiePair = http.Response.Headers["Set-Cookie"].ToString().Split(';')[0];
                Assert.Contains(MauiAuthenticationDefaults.AntiforgeryCookieName, cookiePair);
            }

            // A different service provider simulates a restarted IIS worker (no shared in-memory keys).
            using (var restarted = BuildAppServices(root))
            {
                var http = CreateContext(restarted, "POST");
                http.Request.ContentType = "application/x-www-form-urlencoded";
                http.Request.Headers.Cookie = cookiePair;
                http.Request.Form = new FormCollection(new Dictionary<string, StringValues>
                {
                    ["__RequestVerificationToken"] = formToken
                });
                await restarted.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);

                var invalid = CreateContext(restarted, "POST");
                invalid.Request.ContentType = "application/x-www-form-urlencoded";
                invalid.Request.Headers.Cookie = cookiePair;
                invalid.Request.Form = new FormCollection(new Dictionary<string, StringValues>
                {
                    ["__RequestVerificationToken"] = "deliberately-invalid"
                });
                await Assert.ThrowsAsync<AntiforgeryValidationException>(() =>
                    restarted.GetRequiredService<IAntiforgery>().ValidateRequestAsync(invalid));
            }

            var keyFiles = Directory.GetFiles(MauiDataProtectionConfiguration.GetKeyDirectory(root), "key-*.xml");
            Assert.NotEmpty(keyFiles);
            foreach (var keyFile in keyFiles)
            {
                // Explicit repository locations disable default encryption; ensure DPAPI was applied.
                Assert.False(File.ReadAllText(keyFile).Contains("<unencryptedSecret", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider BuildAppServices(string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        MauiDataProtectionConfiguration.AddMauiDataProtection(services, root);
        services.AddAntiforgery(options => options.Cookie.Name = MauiAuthenticationDefaults.AntiforgeryCookieName);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider provider, string method)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("maui.test");
        context.Request.PathBase = new PathString("/maui");
        context.Request.Path = new PathString("/Account/Login");
        return context;
    }
}
