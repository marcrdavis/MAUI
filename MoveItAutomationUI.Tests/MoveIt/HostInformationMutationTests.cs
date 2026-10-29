using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

// Exercise Information through the real coordinator and the editor's common save lifecycle.
public sealed class HostInformationMutationTests
{
    public static IEnumerable<object[]> Types => HostV0CreationTests.Types;
    private static JsonObject Revision(JsonObject host) => ObjectEditVersion.Named(host["Name"]!.ToString(), host["Desc"]!.ToString());

    [Theory] [MemberData(nameof(Types))]
    public async Task InformationPatchesOnlyItsFieldsAndPreservesIncompleteProtocolSettings(string type)
    {
        var fixture = new Fixture(type); var expected = fixture.Saved.DeepClone().AsObject();
        using var edit = ObjectEditVersion.Require("Host", "42", ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)));
        await fixture.Crud.UpdateBasicDetailsAsync(" 42 ", " Renamed ", "First\nSecond");
        expected["Name"] = "Renamed"; expected["Desc"] = "First\nSecond";
        var submitted = fixture.Submitted![type]!.DeepClone().AsObject();
        foreach (var key in new[] { "DefPassword", "ClientSecret", "OAuthClientSecret", "ProxyPassword" })
        { Assert.Null(submitted[key]); submitted.Remove(key); expected.Remove(key); }
        Assert.True(JsonNode.DeepEquals(expected, submitted));
        Assert.Equal(new[] { "GET", "checkout", "GET", "PUT", "GET", "release" }, fixture.Calls);
        Assert.Equal(0, fixture.ComCalls);
        Assert.Equal(ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)), ObjectEditVersion.SavedVersion);
    }

    [Theory]
    [InlineData("Name")] [InlineData("Desc")]
    public async Task ChangedInformationRejectsUnderCheckoutWithoutPut(string field)
    {
        var fixture = new Fixture("FTP");
        using var edit = ObjectEditVersion.Require("Host", "42", ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)));
        fixture.OnCheckout = host => host[field] = "Concurrent change";
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "Updated"));
        Assert.Null(fixture.Submitted); Assert.Equal(new[] { "GET", "checkout", "GET", "release" }, fixture.Calls);
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task UnrelatedChangesArePreservedFromTheAuthoritativeCheckout(string type)
    {
        var fixture = new Fixture(type);
        using var edit = ObjectEditVersion.Require("Host", "42", ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)));
        fixture.OnCheckout = host => host["NativeOnly"]!["Concurrent"] = "keep";
        await fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "");
        Assert.Equal("keep", fixture.Submitted![type]!["NativeOnly"]!["Concurrent"]!.ToString());
        Assert.Equal("", fixture.Saved["Desc"]!.ToString()); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData("checkout")] [InlineData("permission-body")] [InlineData("permission-wrapper")]
    [InlineData("id")] [InlineData("type")] [InlineData("stale-checkout")]
    public async Task AcquisitionPermissionIdentityAndProjectionFailuresCannotWrite(string failure)
    {
        var fixture = new Fixture("Share") { Failure = failure };
        if (failure.StartsWith("permission", StringComparison.Ordinal)) fixture.OnCheckout = host => host["xAddEditDelete"] = 0;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", ""));
        Assert.Null(fixture.Submitted); Assert.Equal(0, fixture.ComCalls);
        Assert.Equal(failure == "checkout" ? new[] { "GET", "checkout" } : new[] { "GET", "checkout", "GET", "release" }, fixture.Calls);
    }

    [Theory]
    [InlineData("write")] [InlineData("ack")] [InlineData("readback")]
    [InlineData("saved-name")] [InlineData("saved-desc")] [InlineData("saved-id")]
    [InlineData("saved-type")] [InlineData("release")]
    public async Task UncertainSaveAcknowledgementReadbackAndReleaseNeverFallbackOrReplay(string failure)
    {
        var fixture = new Fixture("SMTP") { Failure = failure };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "Updated"));
        Assert.Equal("42", error.HostId); Assert.Equal(1, fixture.Writes); Assert.Equal("release", fixture.Calls.Last());
        Assert.Equal(0, fixture.ComCalls); Assert.DoesNotContain("NATIVE_SECRET", error.ToString());
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task InformationResolvesFlatIndividualDefinitionsWithoutChangingRevisionProjection(string type)
    {
        var fixture = new Fixture(type) { FlatRead = true };
        using var edit = ObjectEditVersion.Require("Host", "42", ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)));
        await fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "Updated");
        Assert.Equal("Renamed", fixture.Saved["Name"]!.ToString()); Assert.Equal("Updated", fixture.Saved["Desc"]!.ToString());
        Assert.Equal(new[] { "GET", "inventory", "checkout", "GET", "inventory", "PUT", "GET", "inventory", "release" }, fixture.Calls);
        Assert.Equal(ObjectEditVersion.Create("Host", "42", Revision(fixture.Saved)), ObjectEditVersion.SavedVersion);
        Assert.Equal(0, fixture.ComCalls); Assert.Equal(1, fixture.Writes);
    }

    [Theory] [InlineData("missing")] [InlineData("ambiguous")] [InlineData("missing-type")]
    public async Task MissingTypeCannotUseANameMatchOrAmbiguousInventoryToAuthorizeInformationWrites(string failure)
    {
        var fixture = new Fixture("SMTP") { FlatRead = true, Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "Updated"));
        Assert.Equal(new[] { "GET", "inventory" }, fixture.Calls); Assert.Equal(0, fixture.Writes); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task CancellationAfterDispatchIsUncertainAndReleaseUsesIndependentToken()
    {
        using var canceled = new CancellationTokenSource(); var fixture = new Fixture("AzureBlob") { Cancel = canceled };
        await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "", canceled.Token));
        Assert.True(canceled.IsCancellationRequested); Assert.True(fixture.ReleasedWithLiveToken); Assert.Equal(1, fixture.Writes); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task DescriptionReadbackAcceptsTransportNewlineNormalization()
    {
        var fixture = new Fixture("FTP") { Failure = "newlines" };
        await fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "First\nSecond");
        Assert.Equal("First\r\nSecond", fixture.Saved["Desc"]!.ToString());
    }

    [Fact]
    public async Task InvalidInputsAndMismatchedHostZeroIdentityCannotReachEitherWriteTransport()
    {
        var fixture = new Fixture("FTP");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Crud.UpdateBasicDetailsAsync(" ", "Name", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", " ", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("42", new string('x',255), ""));
        Assert.Empty(fixture.Calls); Assert.Equal(0, fixture.ComCalls);
        // Host 0 can now save its Description, but a response for another Host can never authorize it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("0", "(Default)", "Updated"));
        Assert.Equal(new[] { "GET" }, fixture.Calls); Assert.Null(fixture.Submitted); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task UnverifiedTypeRetainsItsExplicitComPathBeforeAnyRestCheckout()
    {
        var fixture = new Fixture("AS2"); await fixture.Crud.UpdateBasicDetailsAsync("42", "Renamed", "Updated");
        Assert.Equal(new[] { "GET", "COM" }, fixture.Calls); Assert.Equal(1, fixture.ComCalls); Assert.Null(fixture.Submitted);
    }

    private sealed class Fixture
    {
        public HostCrudCoordinator Crud { get; }
        public JsonObject Saved { get; private set; }
        public JsonObject? Submitted { get; private set; }
        public List<string> Calls { get; } = [];
        public int ComCalls { get; private set; }
        public int Writes { get; private set; }
        public bool FlatRead { get; init; }
        public string? Failure { get; init; }
        public Action<JsonObject>? OnCheckout { get; set; }
        public CancellationTokenSource? Cancel { get; init; }
        public bool ReleasedWithLiveToken { get; private set; }
        public Fixture(string type)
        {
            if (type == "AS2") Saved = new JsonObject();
            else
            {
                using var stream = typeof(HostV0CreationTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.HostV0CreationCaptures.json")!;
                Saved = JsonNode.Parse(stream)![type]!.DeepClone().AsObject();
            }
            Saved["ID"] = "42"; Saved["Name"] = "Original"; Saved["Desc"] = "Old";
            Saved["Group"] = new JsonArray("Production"); Saved["NativeOnly"] = new JsonObject { ["Opaque"] = "keep" };
            Saved["ID2"] = "7"; Saved["xAddEditDelete"] = 1;
            if (type != "SharePoint") Saved["DefPassword"] = "NATIVE_SECRET";
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IMoveItRestClient.GetHostAsync):
                        Calls.Add("GET"); if (Writes > 0 && Failure == "readback") throw new HttpRequestException("Readback failed");
                        return Task.FromResult(FlatRead ? Saved.DeepClone().AsObject() : Wrap(Saved, Writes > 0 && Failure == "saved-type" ? "FTP" : type));
                    case nameof(IMoveItRestClient.GetHostsAsync):
                        Calls.Add("inventory");
                        var match = Wrap(Saved,type);
                        if (Failure == "missing") match[type]!["ID"] = "99";
                        return Task.FromResult<IReadOnlyList<JsonObject>>(Failure == "ambiguous" ? [match, match.DeepClone().AsObject()]
                            : Failure == "missing-type" ? [Saved.DeepClone().AsObject()] : [match]);
                    case nameof(IMoveItRestClient.CheckoutHostV0Async):
                        Calls.Add("checkout"); if (Failure == "checkout") throw new InvalidOperationException("Owned by another editor");
                        OnCheckout?.Invoke(Saved); var checkout = Saved.DeepClone().AsObject();
                        if (Failure == "id") checkout["ID"] = "99";
                        if (Failure == "stale-checkout") checkout["Desc"] = "Stale checkout projection";
                        var wrapper = Wrap(checkout, Failure == "type" ? "FTP" : type);
                        if (Failure == "permission-wrapper") { checkout.Remove("xAddEditDelete"); Saved.Remove("xAddEditDelete"); wrapper = Wrap(checkout,type); wrapper["xAddEditDelete"] = false; }
                        return Task.FromResult(wrapper);
                    case nameof(IMoveItRestClient.UpdateHostV0Async):
                        Calls.Add("PUT"); Writes++; Submitted = ((JsonObject)args[1]!).DeepClone().AsObject(); Saved = Submitted[type]!.DeepClone().AsObject();
                        if (Cancel is not null) { Cancel.Cancel(); throw new OperationCanceledException(); }
                        if (Failure == "write") throw new HttpRequestException("NATIVE_SECRET");
                        if (Failure == "saved-name") Saved["Name"] = "Wrong";
                        if (Failure == "saved-desc") Saved["Desc"] = "Wrong";
                        if (Failure == "saved-id") Saved["ID"] = "99";
                        if (Failure == "newlines") Saved["Desc"] = Saved["Desc"]!.ToString().Replace("\n","\r\n");
                        return Task.FromResult(Failure == "ack" ? new JsonObject() : new JsonObject { ["id"] = "42", ["status"] = "success", ["errorcode"] = 0 });
                    case nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async):
                        Calls.Add("release"); ReleasedWithLiveToken = !((CancellationToken)args[1]!).IsCancellationRequested;
                        if (Failure == "release") throw new HttpRequestException("Release failed");
                        return Task.CompletedTask;
                    default: throw new NotSupportedException(method.Name);
                }
            });
            var com = Stub<IMoveItComClient>((method, _) =>
            {
                ComCalls++; Assert.Equal("AS2", type); Assert.Equal(nameof(IMoveItComClient.UpdateHostBasicDetailsAsync), method.Name);
                Calls.Add("COM"); return Task.CompletedTask;
            });
            var saves = new HostDefinitionSaveService(rest, com, null!, NullLogger<HostDefinitionSaveService>.Instance);
            Crud = new HostCrudCoordinator(rest,com,saves);
        }
        private static JsonObject Wrap(JsonObject host, string type) => new() { [type] = host.DeepClone() };
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
