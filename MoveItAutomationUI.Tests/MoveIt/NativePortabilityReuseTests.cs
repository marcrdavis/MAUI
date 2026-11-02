using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using static MoveItAutomationUI.Services.MoveIt.MauiNativePortabilityService;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class NativePortabilityReuseTests
{
    private static readonly HashSet<string> NoRoots = new(StringComparer.OrdinalIgnoreCase);
    private static ImportObjectRef Object(string type, string id = "1", string name = "Shared", string subtype = "")
        => new(type, subtype.Length == 0 ? type : subtype, id, name, new JsonObject { ["ID"] = id, ["Name"] = name, ["Lang"] = "PS1" });
    private static DestinationInventory Inventory(AutomationHost[]? hosts = null, AutomationScript[]? scripts = null,
        AutomationDateList[]? dates = null, AutomationKeyCertificate[]? keys = null) => new([], hosts ?? [], scripts ?? [], dates ?? [], keys ?? []);

    [Theory]
    [InlineData("1")] [InlineData("20")]
    public void CompatibleHostMatchesReuseAcrossNativeIds(string id)
    {
        var target = ResolveReuseTarget(Object("Host", subtype: "SSHFTP"), Inventory(hosts:
            [new() { Id = id, Name = "SHARED", TypeCode = "SSHFTP", CanEditDelete = false }]), NoRoots);
        Assert.Equal(new ReuseTarget(id, "SHARED"), target);
    }

    [Fact]
    public void DefaultLocalHostUsesItsNativeIdentity()
        => Assert.Equal("0", ResolveReuseTarget(Object("Host", "0", "Source Local", "FileSystem"), Inventory(hosts:
            [new() { Id = "0", Name = "Destination Local", TypeCode = "FileSystem" }]), NoRoots)!.Id);

    [Theory]
    [InlineData("FTP")] [InlineData("")]
    public void DifferentOrUnknownHostTypeCannotBeReused(string targetType)
        => Assert.Null(ResolveReuseTarget(Object("Host", subtype: "SSHFTP"), Inventory(hosts:
            [new() { Id = "20", Name = "Shared", TypeCode = targetType }]), NoRoots));

    [Fact]
    public void SelectedRootsAndAllTasksKeepExistingChoices()
    {
        var obj = Object("Host", subtype: "FTP");
        Assert.Null(ResolveReuseTarget(obj, Inventory(hosts: [new() { Id = "1", Name = "Shared", TypeCode = "FTP" }]),
            new HashSet<string>(["Host|1"], StringComparer.OrdinalIgnoreCase)));
        Assert.Null(ResolveReuseTarget(Object("Task"), Inventory(), NoRoots));
    }

    [Fact]
    public void NameOrIdAmbiguityAndUnrelatedIdCollisionCannotDefaultToReuse()
    {
        var obj = Object("Host", subtype: "FTP");
        foreach (var hosts in new AutomationHost[][]
        {
            [new() { Id = "1", Name = "Unrelated", TypeCode = "FTP" }],
            [new() { Id = "1", Name = "Unrelated", TypeCode = "FTP" }, new() { Id = "2", Name = "Shared", TypeCode = "FTP" }],
            [new() { Id = "2", Name = "Shared", TypeCode = "FTP" }, new() { Id = "3", Name = "SHARED", TypeCode = "FTP" }]
        }) Assert.Null(ResolveReuseTarget(obj, Inventory(hosts: hosts), NoRoots));
    }

    [Theory]
    [InlineData("PS1", true)] [InlineData("VBScript", false)] [InlineData("", false)]
    public void ScriptsRequireCompatibleLanguage(string language, bool compatible)
    {
        var target = ResolveReuseTarget(Object("Script"), Inventory(scripts:
            [new() { Id = "20", Name = "Shared", LanguageCode = language }]), NoRoots);
        Assert.Equal(compatible, target is not null);
    }

    [Theory]
    [InlineData("PowerShell", "PS1")] [InlineData("VBS", "VBScript")]
    public void ScriptReuseUsesTheInventoryLanguageNormalization(string nativeLanguage, string inventoryLanguage)
    {
        var obj = Object("Script"); obj.Node["Lang"] = nativeLanguage;
        Assert.NotNull(ResolveReuseTarget(obj, Inventory(scripts: [new() { Id = "20", Name = "Shared", LanguageCode = inventoryLanguage }]), NoRoots));
    }

    [Fact]
    public void UnknownSourceScriptLanguageAndDifferentBuiltInIdentityCannotReuse()
    {
        var obj = Object("Script"); obj.Node.Remove("Lang");
        Assert.Null(ResolveReuseTarget(obj, Inventory(scripts: [new() { Id = "20", Name = "Shared", LanguageCode = "PS1" }]), NoRoots));
        Assert.Null(ResolveReuseTarget(Object("Script"), Inventory(scripts: [new() { Id = "20", Name = "Shared", LanguageCode = "PS1", IsBuiltIn = true }]), NoRoots));
        Assert.NotNull(ResolveReuseTarget(Object("Script"), Inventory(scripts: [new() { Id = "1", Name = "Shared", LanguageCode = "PS1", IsBuiltIn = true }]), NoRoots));
    }

    [Fact]
    public void DateListsReuseAUniqueNamedDestinationWithoutEditPermission()
        => Assert.Equal("20", ResolveReuseTarget(Object("DateList"), Inventory(dates:
            [new() { Id = "20", Name = "Shared", CanEditDelete = false }]), NoRoots)!.Id);

    [Fact]
    public void UnresolvedDestinationIdentitiesCannotOfferReuse()
    {
        Assert.Null(ResolveReuseTarget(Object("DateList"), Inventory(dates: [new() { Name = "Shared" }]), NoRoots));
        Assert.Null(ResolveReuseTarget(Object("SSHClientKey"), Inventory(keys: [new() { Id = "1", Kind = KeyCertificateKind.Ssh }]), NoRoots));
    }

    [Theory]
    [InlineData("SSHClientKey", KeyCertificateKind.Ssh, "ssh|1")]
    [InlineData("SSLClientCert", KeyCertificateKind.Tls, "tls|1")]
    [InlineData("PGPKey", KeyCertificateKind.Pgp, "pgp|1")]
    public void KeysKeepTheVerifiedNativeIdContract(string type, KeyCertificateKind kind, string rowId)
    {
        var obj = Object(type);
        Assert.Equal(rowId, ResolveReuseTarget(obj, Inventory(keys: [new() { Id = "1", RowId = rowId, Name = "Destination Label", Kind = kind }]), NoRoots)!.Id);
        Assert.Null(ResolveReuseTarget(obj, Inventory(keys: [new() { Id = "20", RowId = "other", Name = "Shared", Kind = kind }]), NoRoots));
    }

    [Fact]
    public void ReuseSubmissionRequiresAConflictExactTargetAndCompatibleResource()
    {
        var obj = Object("Host", subtype: "FTP");
        var inventory = Inventory(hosts: [new() { Id = "20", Name = "Shared", TypeCode = "FTP" }]);
        var duplicates = new[] { new DuplicateRef("Host", "1", ["name"]) };
        Dictionary<string, NativePortabilityConflictResolution> Choices(string id)
            => new() { ["Host|1"] = new("Host", "1", "useexisting", "", id) };
        Assert.Single(BuildReusePlans([obj], duplicates, Choices("20"), inventory, NoRoots));
        Assert.Throws<InvalidOperationException>(() => BuildReusePlans([obj], duplicates, Choices("21"), inventory, NoRoots));
        Assert.Throws<InvalidOperationException>(() => BuildReusePlans([obj], [], Choices("20"), inventory, NoRoots));
        Assert.Throws<InvalidOperationException>(() => BuildReusePlans([obj], duplicates, Choices("20"), inventory, new HashSet<string>(["Host|1"])));
    }

    [Fact]
    public void RevalidationRejectsRemovedReplacedRenamedOrIncompatibleTargets()
    {
        var plan = new ReusePlan(Object("Host", subtype: "FTP"), new("20", "Shared"));
        ValidateReusePlans([plan], Inventory(hosts: [new() { Id = "20", Name = "Shared", TypeCode = "FTP" }]), NoRoots);
        foreach (var hosts in new AutomationHost[][]
        {
            [], [new() { Id = "21", Name = "Shared", TypeCode = "FTP" }],
            [new() { Id = "20", Name = "Renamed", TypeCode = "FTP" }],
            [new() { Id = "20", Name = "Shared", TypeCode = "SSHFTP" }]
        }) Assert.Throws<InvalidOperationException>(() => ValidateReusePlans([plan], Inventory(hosts: hosts), NoRoots));
    }

    [Fact]
    public void ExclusionRemapsSharedStructuredReferencesAndPreservesLiteralAndProtectedContent()
    {
        const string xml = """
            <Exported Scheme="3"><Tasks><Task ID="100" Name="Task" Active="1" SchedDateListID="3">
              <Source HostID="1"/><Dest HostID="1"/><Process ScriptID="2" CustomScriptID="2" DirScriptID="2"/>
              <Schedule><DateListRef ID="3"/></Schedule><Parameter Name="PGPEncryptKey" value="0x99"/>
              <Parameter Name="GlobalByName" value="keep"/><Group>Finance</Group>
              <Description>HostID="1" ScriptID="2" [Host:1]</Description><SourceCode>literal HostID="1"</SourceCode>
              </Task><Task ID="101" Name="Other"><Source HostID="1"/></Task></Tasks>
              <Hosts><FTP ID="1" Name="Shared" DefPassword="encrypted-source"/><SSHFTP ID="8" Name="Imported" DefClientKeyID="9" DefPassword="encrypted-keep"/></Hosts>
              <Scripts><Script ID="2" Name="Script" Lang="PS1"/></Scripts><DateLists><DateList ID="3" Name="Dates"/></DateLists>
              <SSHClientKeys><SSHClientKey ID="9" Name="Key">encrypted-key</SSHClientKey></SSHClientKeys>
              <PGPKeys><PGPKey ID="0x99">encrypted-pgp</PGPKey></PGPKeys></Exported>
            """;
        ReusePlan[] plans = [new(Object("Host", "1", subtype: "FTP"), new("20", "Shared")),
            new(Object("Script", "2"), new("21", "Script")), new(Object("DateList", "3"), new("22", "Dates")),
            new(Object("PGPKey", "0x99"), new("pgp|0x99", "PGP"))];
        var doc = XDocument.Parse(PrepareReuseImportXml(xml, plans, [Object("Task", "100"), Object("Task", "101"), Object("Host", "8", subtype: "SSHFTP"), Object("SSHClientKey", "9")]));
        Assert.All(doc.Descendants().Attributes("HostID"), attribute => Assert.Equal("20", attribute.Value));
        foreach (var name in new[] { "ScriptID", "CustomScriptID", "DirScriptID" }) Assert.Equal("21", doc.Descendants("Process").Single().Attribute(name)!.Value);
        Assert.Equal("22", doc.Descendants("Task").First().Attribute("SchedDateListID")!.Value);
        Assert.Equal("22", doc.Descendants("DateListRef").Single().Attribute("ID")!.Value);
        Assert.Equal("100", doc.Descendants("Task").First().Attribute("ID")!.Value);
        Assert.Equal("1", doc.Descendants("Task").First().Attribute("Active")!.Value);
        Assert.Equal("Task", doc.Descendants("Task").First().Attribute("Name")!.Value);
        Assert.Equal("Finance", doc.Descendants("Group").Single().Value);
        Assert.Contains("HostID=\"1\"", doc.Descendants("Description").Single().Value, StringComparison.Ordinal);
        Assert.Equal("literal HostID=\"1\"", doc.Descendants("SourceCode").Single().Value);
        Assert.Equal("encrypted-keep", Assert.Single(doc.Root!.Element("Hosts")!.Elements()).Attribute("DefPassword")!.Value);
        Assert.Equal("encrypted-key", doc.Descendants("SSHClientKey").Single().Value);
        Assert.Equal("keep", Assert.Single(doc.Descendants("Parameter"), element => (string?)element.Attribute("Name") == "GlobalByName").Attribute("value")!.Value);
        Assert.Empty(doc.Root.Element("Scripts")!.Elements()); Assert.Empty(doc.Root.Element("DateLists")!.Elements()); Assert.Empty(doc.Root.Element("PGPKeys")!.Elements());
    }

    [Theory]
    [InlineData("SSHClientKey", "SSHClientKeys", "ssh|9")]
    [InlineData("SSLClientCert", "SSLClientCerts", "tls|9")]
    [InlineData("PGPKey", "PGPKeys", "pgp|9")]
    public void ExistingKeyExclusionsPreserveExplicitEmptyTasks(string type, string container, string target)
    {
        var xml = $"<Exported><Tasks></Tasks><Hosts><FTP ID=\"1\"/></Hosts><{container}><{type} ID=\"9\">encrypted</{type}></{container}></Exported>";
        var result = PrepareReuseImportXml(xml, [new(Object(type, "9"), new(target, "Existing"))], [Object("Host", "1")]);
        Assert.Contains("<Tasks></Tasks>", result, StringComparison.Ordinal);
        Assert.Empty(XDocument.Parse(result).Root!.Element(container)!.Elements());
    }

    [Fact]
    public void MissingDuplicatedOrWrongNativeDefinitionsFailBeforeEncoding()
    {
        var plan = new ReusePlan(Object("Host", subtype: "FTP"), new("20", "Shared"));
        foreach (var hosts in new[] { "", "<FTP ID=\"1\"/><FTP ID=\"1\"/>", "<SSHFTP ID=\"1\"/>" })
            Assert.Throws<InvalidOperationException>(() => PrepareReuseImportXml($"<Exported><Tasks></Tasks><Hosts>{hosts}</Hosts></Exported>", [plan], []));
    }

    [Fact]
    public void DestinationIdCollisionWithAnotherImportedObjectCannotRedirectReuse()
        => Assert.Throws<InvalidOperationException>(() => PrepareReuseImportXml(
            "<Exported><Tasks></Tasks><Hosts><FTP ID=\"1\"/><FTP ID=\"20\"/></Hosts></Exported>",
            [new(Object("Host", subtype: "FTP"), new("20", "Shared"))], [Object("Host", "20", subtype: "FTP")]));

    [Fact]
    public void AuthoritativeReencodeMustPreserveExclusionsAndRemainingObjects()
    {
        var task = Object("Task", "100"); var host = Object("Host", "1");
        ValidateReuseDecodedObjects([task], [task]);
        Assert.Throws<InvalidOperationException>(() => ValidateReuseDecodedObjects([task], [task, host]));
        Assert.Throws<InvalidOperationException>(() => ValidateReuseDecodedObjects([task], [host]));
        Assert.Throws<InvalidOperationException>(() => ValidateReuseDecodedObjects([task, host], [task, task]));
        Assert.Throws<InvalidOperationException>(() => ValidateReuseDecodedObjects([task], [Object("Task", "100", "Unexpected Name")]));
    }
}
