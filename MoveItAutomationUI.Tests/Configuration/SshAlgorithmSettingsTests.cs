using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class SshAlgorithmSettingsTests
{
    [Fact]
    public void SettingsMutationAddsMissingDefinitionsAndPreservesUnchangedListsAndMetadata()
    {
        var template = new JsonObject { [SshAlgorithmOptions.SectionName] = JsonSerializer.SerializeToNode(new SshAlgorithmOptions()) };
        var store = Stub<IAppSettingsDocumentStore>((method, _) => method.Name == nameof(IAppSettingsDocumentStore.ReadTemplate)
            ? template.DeepClone().AsObject() : throw new NotSupportedException(method.Name));
        var mutator = new ConfigurationSettingsMutator(null!, store);
        var root = new JsonObject();
        var request = new ConfigurationSettingsSaveRequest { SshAlgorithms = new() { Encryption = "aes128-ctr,aes256-ctr" } };
        mutator.Apply(root, request);
        var encryption = root[SshAlgorithmOptions.SectionName]!["Encryption"]!;
        Assert.Equal(request.SshAlgorithms.Encryption, encryption["Value"]!.GetValue<string>());
        Assert.Equal("Default Encryption Algorithms", encryption["Name"]!.GetValue<string>());
        Assert.Equal(SshAlgorithmOptions.InitialKeyExchange, root[SshAlgorithmOptions.SectionName]!["KeyExchange"]!["Value"]!.GetValue<string>());
        encryption["Description"] = "Installation description";
        var before = root[SshAlgorithmOptions.SectionName]!.DeepClone();
        request.SshAlgorithms = null;
        mutator.Apply(root, request);
        Assert.True(JsonNode.DeepEquals(before, root[SshAlgorithmOptions.SectionName]));
        request.SshAlgorithms = new() { KeyExchange = "curve25519-sha256" };
        mutator.Apply(root, request);
        Assert.Equal("curve25519-sha256", root[SshAlgorithmOptions.SectionName]!["KeyExchange"]!["Value"]!.GetValue<string>());
        Assert.Equal("Installation description", encryption["Description"]!.GetValue<string>());
        Assert.Equal("aes128-ctr,aes256-ctr", encryption["Value"]!.GetValue<string>());
        Assert.Equal(SshAlgorithmOptions.InitialEncryption, template[SshAlgorithmOptions.SectionName]!["Encryption"]!["Value"]!.GetValue<string>());
    }

    [Fact]
    public async Task SavedDefaultsAreReboundForTheNextHostRequestWithoutRestart()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SshAlgorithms:Encryption:Value"] = "aes128-ctr", ["SshAlgorithms:KeyExchange:Value"] = "curve25519-sha256" }).Build();
        using var services = new ServiceCollection().Configure<SshAlgorithmOptions>(configuration.GetSection(SshAlgorithmOptions.SectionName)).BuildServiceProvider();
        var rest = Stub<IMoveItRestClient>((method, _) => Task.FromResult(HostSshAlgorithmTests.Capture(
            method.Name == nameof(IMoveItRestClient.GetSshEncryptionListV0Async) ? "Encryption" : "KeyExchange")));
        using var first = services.CreateScope();
        var firstCatalog = await new HostCrudCoordinator(rest, null!, null!, first.ServiceProvider.GetRequiredService<IOptionsSnapshot<SshAlgorithmOptions>>()).GetSshAlgorithmsAsync();
        Assert.Equal("aes128-ctr", HostSshAlgorithms.DefaultSelection(firstCatalog.Encryption));
        configuration["SshAlgorithms:Encryption:Value"] = "aes256-ctr,aes128-ctr";
        configuration.Reload();
        using var second = services.CreateScope();
        var secondCatalog = await new HostCrudCoordinator(rest, null!, null!, second.ServiceProvider.GetRequiredService<IOptionsSnapshot<SshAlgorithmOptions>>()).GetSshAlgorithmsAsync();
        Assert.Equal("aes256-ctr,aes128-ctr", HostSshAlgorithms.DefaultSelection(secondCatalog.Encryption));
        Assert.Equal("curve25519-sha256", HostSshAlgorithms.DefaultSelection(secondCatalog.KeyExchange));
        Assert.Equal("aes128-ctr", HostSshAlgorithms.DefaultSelection(firstCatalog.Encryption));
    }

    [Theory]
    [InlineData("retired-cipher")] [InlineData("")] [InlineData("bad name")]
    public async Task UnsupportedOrEmptySelectionsCannotReachTheSettingsWriter(string value)
    {
        var settings = Stub<IAppSettingsConfigurationService>((_, _) => throw new Exception("No settings write expected"));
        var hosts = Stub<IHostCrudCoordinator>((_, _) => Task.FromResult(HostSshAlgorithmTests.Catalog()));
        var result = await Controller(settings).SaveSettings(new() { SshAlgorithms = new() { Encryption = value } }, default, hosts);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UnrelatedSettingsSaveDoesNotRequireAlgorithmDiscovery()
    {
        var writes = 0;
        var settings = Stub<IAppSettingsConfigurationService>((_, _) => { writes++; return Task.FromResult(new ConfigurationSaveResult()); });
        var hosts = Stub<IHostCrudCoordinator>((_, _) => throw new Exception("No discovery expected"));
        Assert.IsType<JsonResult>(await Controller(settings).SaveSettings(new(), default, hosts));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ValidSelectionsUseTheExistingWriterAndRevisionConflictGuard()
    {
        var request = new ConfigurationSettingsSaveRequest { ConfigurationRevision = "stale", SshAlgorithms = new() { Encryption = "aes128-ctr" } };
        var writes = 0;
        var settings = Stub<IAppSettingsConfigurationService>((_, args) =>
        { Assert.Same(request, args[0]); writes++; throw new ConfigurationRevisionConflictException(); });
        var hosts = Stub<IHostCrudCoordinator>((_, _) => Task.FromResult(HostSshAlgorithmTests.Catalog()));
        Assert.IsType<ConflictObjectResult>(await Controller(settings).SaveSettings(request, default, hosts));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task FailedDiscoveryCannotWriteDefaultsAndReturnsAnInlineSafeError()
    {
        var settings = Stub<IAppSettingsConfigurationService>((_, _) => throw new Exception("No settings write expected"));
        var hosts = Stub<IHostCrudCoordinator>((_, _) => Task.FromException<HostSshAlgorithmCatalog>(new MoveItApiException("Internal native details", HttpStatusCode.ServiceUnavailable)));
        Assert.IsType<BadRequestObjectResult>(await Controller(settings).SaveSettings(new() { SshAlgorithms = new() { KeyExchange = "curve25519-sha256" } }, default, hosts));
        var result = Assert.IsType<BadRequestObjectResult>(await Controller(settings).SshAlgorithms(hosts, default));
        Assert.DoesNotContain("Internal native details", JsonSerializer.Serialize(result.Value));
    }

    private static ConfigurationController Controller(IAppSettingsConfigurationService settings) => new(
        settings, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        new Snapshot<UiOptions>(), new Snapshot<GridBehaviorOptions>(), NullLogger<ConfigurationController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    private sealed class Snapshot<T> : IOptionsSnapshot<T> where T : class, new()
    { public T Value { get; } = new(); public T Get(string? name) => Value; }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
