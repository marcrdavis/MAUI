using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostSshAlgorithmTests
{
    internal static JsonObject Capture(string kind)
    {
        using var stream = typeof(HostSshAlgorithmTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.HostSshDiscoveryCaptures.json")!;
        return JsonNode.Parse(stream)![kind]!.DeepClone().AsObject();
    }

    internal static HostSshAlgorithmCatalog Catalog() => new(
        HostSshAlgorithms.Parse(Capture("Encryption"), SshAlgorithmOptions.InitialEncryption),
        HostSshAlgorithms.Parse(Capture("KeyExchange"), SshAlgorithmOptions.InitialKeyExchange));

    [Fact]
    public void SuppliedResponsesUseOnlySupportedNamesAndMatchingDefaults()
    {
        var catalog = Catalog();
        Assert.Equal(16, catalog.Encryption.Count); Assert.Equal(18, catalog.KeyExchange.Count);
        Assert.Equal(6, catalog.Encryption.Count(value => value.DefaultEnabled));
        Assert.Equal(13, catalog.KeyExchange.Count(value => value.DefaultEnabled));
        Assert.DoesNotContain(catalog.Encryption, value => value.Value == "rijndael128-cbc");
        Assert.DoesNotContain(catalog.KeyExchange, value => value.Value == "ext-info-c");
        Assert.DoesNotContain(catalog.KeyExchange, value => value.Value == "kex-strict-c-v00@openssh.com");
        Assert.Equal("aes256-ctr", catalog.Encryption[0].Value);
        Assert.Contains(catalog.Encryption, value => value.Value == "3des-ctr" && !value.DefaultEnabled);
        Assert.Contains(catalog.KeyExchange, value => value.Value.StartsWith("gss-", StringComparison.Ordinal) && value.DefaultEnabled);
        foreach (var pair in new[] { ("Encryption", catalog.Encryption), ("KeyExchange", catalog.KeyExchange) })
        {
            var response = Capture(pair.Item1);
            var supported = response["IPWorks"]!.GetValue<string>().Split(',');
            var defaults = pair.Item1 == "Encryption" ? SshAlgorithmOptions.InitialEncryption : SshAlgorithmOptions.InitialKeyExchange;
            Assert.Equal(supported.OrderBy(value => value), pair.Item2.Select(value => value.Value).OrderBy(value => value));
            Assert.Equal(defaults, HostSshAlgorithms.DefaultSelection(pair.Item2));
            Assert.Equal(defaults.Split(','), pair.Item2.Take(defaults.Split(',').Length).Select(value => value.Value));
        }
    }

    [Fact]
    public void ChangedServerChoicesAndDefaultsReplaceThePreviousCatalog()
    {
        var response = new JsonObject { ["IPWorks"] = "old,new,common,new", ["WeOnlyDo"] = "provider-only,new" };
        var first = HostSshAlgorithms.Parse(response, "new");
        Assert.Equal(new[] { "new", "old", "common" }, first.Select(value => value.Value));
        Assert.Equal("new", HostSshAlgorithms.DefaultSelection(first));
        response["IPWorks"] = "common,future"; response["WeOnlyDo"] = "future,provider-only";
        var second = HostSshAlgorithms.Parse(response, "future");
        Assert.Equal(new[] { "future", "common" }, second.Select(value => value.Value));
        Assert.Equal("future", HostSshAlgorithms.DefaultSelection(second));
        Assert.Equal(new[] { "new", "old", "common" }, first.Select(value => value.Value));
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"IPWorks\":null}")]
    [InlineData("{\"IPWorks\":[],\"WeOnlyDo\":\"a\"}")]
    [InlineData("{\"IPWorks\":\"\",\"WeOnlyDo\":\"a\"}")]
    [InlineData("{\"IPWorks\":\"bad name\",\"WeOnlyDo\":\"a\"}")]
    public void MalformedDiscoveryNeverFabricatesChoices(string json)
        => Assert.Throws<InvalidOperationException>(() => HostSshAlgorithms.Parse(JsonNode.Parse(json)!.AsObject(), "a"));

    [Fact]
    public void NoMatchingDefaultsCannotCreateAnEmptyDefaultSelection()
    {
        var choices = HostSshAlgorithms.Parse(new JsonObject { ["IPWorks"] = "a", ["WeOnlyDo"] = "other-provider" }, "unavailable");
        Assert.False(choices[0].DefaultEnabled);
        Assert.Throws<InvalidOperationException>(() => HostSshAlgorithms.DefaultSelection(choices));
    }

    [Fact]
    public void ConfiguredDefaultsPreserveOrderAndDoNotDependOnWeOnlyDo()
    {
        var response = new JsonObject { ["IPWorks"] = "a,b,c,d", ["WeOnlyDo"] = "a" };
        foreach (var provider in new JsonNode?[] { null, JsonValue.Create("d"), new JsonArray() })
        {
            response["WeOnlyDo"] = provider;
            var choices = HostSshAlgorithms.Parse(response, "c,b,c,retired");
            Assert.Equal("c,b", HostSshAlgorithms.DefaultSelection(choices));
            Assert.Equal(new[] { "c", "b", "a", "d" }, choices.Select(choice => choice.Value));
        }
    }

    [Theory]
    [InlineData("")] [InlineData(" , ")] [InlineData("bad name,a")] [InlineData("a,b\tc")]
    public void InvalidConfiguredSelectionsAreRejected(string value)
        => Assert.Throws<ArgumentException>(() => HostSshAlgorithms.NormalizeSelection(value));

    [Fact]
    public void ConfiguredSelectionsNormalizeWithoutChangingPreferenceOrder()
        => Assert.Equal("b,a", HostSshAlgorithms.NormalizeSelection(" b, a,b, "));

    [Fact]
    public async Task ActualRestClientUsesTheSuppliedAuthenticatedGetsAndRefreshesEachConnection()
    {
        using var handler = new DiscoveryHandler();
        var state = new MoveItSessionState { WebAdminUrl = "https://first.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? state : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        var coordinator = new HostCrudCoordinator(client, null!, null!);
        var first = await coordinator.GetSshAlgorithmsAsync();
        handler.Revision = "new"; state.WebAdminUrl = "https://second.test";
        var second = await coordinator.GetSshAlgorithmsAsync();
        Assert.Equal("cipher-old", first.Encryption[0].Value); Assert.Equal("cipher-new", second.Encryption[0].Value);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request => { Assert.Equal("GET", request.Method); Assert.Equal("Bearer test-token", request.Authorization); Assert.False(request.HasBody); });
        foreach (var host in new[] { "first.test", "second.test" })
        {
            Assert.Contains(handler.Requests, request => request.Host == host && request.Path == "/api/v0/ssh/encryptionlist");
            Assert.Contains(handler.Requests, request => request.Host == host && request.Path == "/api/v0/sshdefaultkexalgs");
        }
    }

    [Fact]
    public async Task CancellationBeforeDiscoveryMakesNoNativeCalls()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var calls = 0;
        var rest = Stub<IMoveItRestClient>((_, _) => { calls++; throw new InvalidOperationException("No call expected"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostSshAlgorithms.LoadAsync(rest, canceled.Token));
        Assert.Equal(0, calls);
    }

    private sealed class DiscoveryHandler : HttpMessageHandler
    {
        public string Revision { get; set; } = "old";
        public List<(string Host, string Path, string Method, string Authorization, bool HasBody)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.Host, request.RequestUri.AbsolutePath, request.Method.Method, request.Headers.Authorization!.ToString(), request.Content is not null));
            var response = request.RequestUri.AbsolutePath switch
            {
                "/api/v0/ssh/encryptionlist" => new JsonObject { ["IPWorks"] = "cipher-" + Revision, ["WeOnlyDo"] = "cipher-" + Revision + ",provider-only" },
                "/api/v0/sshdefaultkexalgs" => Capture("KeyExchange"),
                _ => throw new InvalidOperationException("Unexpected discovery path")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), System.Text.Encoding.UTF8, "application/json") });
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
