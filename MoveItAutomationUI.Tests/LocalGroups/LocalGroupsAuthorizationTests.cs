using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.LocalGroups;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.LocalGroups;

public sealed class LocalGroupsAuthorizationTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task LiveBrowserAndBothAdministratorPermissionsAreRequiredBeforeQueueing(bool live, bool mauiAdmin, bool moveItAdmin)
    {
        var state = new MoveItSessionState { ServerKey = "A", EnvironmentKey = "ENV", WebAdminUrl = "https://automation/", ServerHost = "automation", Username = "Admin" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MoveItLocalGroups:Enabled:Value"] = "true", ["MoveItLocalGroups:Agents:0:ServerKey"] = "A",
            ["MoveItLocalGroups:Agents:0:CertificateSha256"] = new('A',64),
            ["MoveItLocalGroups:Agents:0:ConnectionBinding"] = LocalGroupsBinding.ForSession(state)
        }).Build();
        var sessions = Stub<IMoveItSessionService>((name, _) => name switch { "Get" => state, "HasLiveBrowserSession" => live, _ => throw new InvalidOperationException(name) });
        var security = Stub<IMauiSecurityService>((name, _) => name == "IsAdmin" ? mauiAdmin : throw new InvalidOperationException(name));
        var rest = Stub<IMoveItRestClient>((name, _) => name == "GetInfoAsync" ? Task.FromResult(new MoveItInfo { IsAdmin = moveItAdmin }) : throw new InvalidOperationException(name));
        var service = new LocalGroupsService(new(new Snapshots(config)), null!, sessions, rest, security, null!, NullLogger<LocalGroupsService>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExecuteAsync(new() { Operation = "list" }, new ClaimsPrincipal(), default));
    }
    [Fact]
    public void ExistingSettingsClientsDoNotRemoveFeatureRegistrations()
    {
        var root = new JsonObject { [LocalGroupsOptions.SectionName] = new JsonObject { ["AgentHost"] = "agent.example.com", ["Agents"] = new JsonArray(new JsonObject { ["ServerKey"] = "A" }) } };
        var before = root[LocalGroupsOptions.SectionName]!.ToJsonString();
        Mutator().Apply(root, new ConfigurationSettingsSaveRequest());
        Assert.Equal(before, root[LocalGroupsOptions.SectionName]!.ToJsonString());
    }
    [Fact]
    public void FeatureSettingsUseTheExistingDocumentSaveAndServerOwnedBinding()
    {
        var root = new JsonObject { [LocalGroupsOptions.SectionName] = new JsonObject { ["AgentHost"] = "unused.example.com" } };
        var settings = new LocalGroupsSettings { Enabled = true, Agents = [new() { ServerKey = "A", CertificateSha256 = new('A', 64), ConnectionBinding = "untrusted" }] };
        Mutator().Apply(root, new ConfigurationSettingsSaveRequest { MoveItLocalGroups = settings, Servers = [new() { Key = "A", EnvironmentKey = "ENV", ServerHost = "automation", WebAdminUrl = "https://automation/" }] });
        Assert.True(root[LocalGroupsOptions.SectionName]!["Enabled"]!["Value"]!.GetValue<bool>());
        Assert.Null(root[LocalGroupsOptions.SectionName]!["AgentHost"]);
        Assert.Null(root[LocalGroupsOptions.SectionName]!["Agents"]![0]!["ComputerName"]);
        Assert.Equal(LocalGroupsBinding.Compute("A","ENV","https://automation/","automation"),settings.Agents[0].ConnectionBinding);
    }
    private static ConfigurationSettingsMutator Mutator() => new(
        Stub<ISmtpSecretProtector>((name, _) => throw new InvalidOperationException(name)),
        Stub<IAppSettingsDocumentStore>((name, _) => name == "ReadTemplate" ? new JsonObject() : throw new InvalidOperationException(name)));
    private static T Stub<T>(Func<string,object?[]?,object?> handler) where T:class
    { var value=DispatchProxy.Create<T,Proxy>(); ((Proxy)(object)value).Handler=handler; return value; }
    public class Proxy:DispatchProxy
    {
        public Func<string,object?[]?,object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!.Name,args);
    }
    private sealed class Snapshots(IConfigurationRoot configuration):IAppSettingsConfigurationSnapshotReader
    {
        public AppSettingsConfigurationSnapshot Read()=>new(new JsonObject(),configuration,"test");
        public AppSettingsConfigurationSnapshot FromDocument(JsonObject document)=>throw new NotSupportedException();
        public string GetRevision(JsonObject document)=>"test";
    }
}
