using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskBrowseDirectoryClassificationTests
{
    private const string LibraryGuid = "11111111-2222-3333-4444-555555555555";
    public static IEnumerable<object[]> BrowseCases()
    {
        foreach (var type in new[] { "FileSystem", "Share", "siLock", "FTP", "SSHFTP", "S3", "AzureBlob", "GoogleStorage", "SharePoint" })
            foreach (var kind in new[] { "Source", "Destination" }) yield return new object[] { type, kind, false };
        foreach (var kind in new[] { "Source", "Destination" }) yield return new object[] { "FTP", kind, true };
    }

    [Theory]
    [MemberData(nameof(BrowseCases))]
    public async Task NativeDirectoryFlagsSurviveTaskAndHostBrowse(string type, string kind, bool secure)
    {
        var probe = new BrowseProbe(type, secure);
        var result = await probe.TaskBrowse(kind);
        AssertListing(result.Items, type == "FileSystem" ? @"C:\incoming\child" : "/incoming/child");
        Assert.Equal(type == "FileSystem" ? 0 : 1, probe.Releases);
        if (type != "FileSystem")
        {
            var hostResult = await new AutomationHostBrowseService(probe.Rest, new TraditionalTaskHostProfileCatalog(),
                NullLogger<AutomationHostBrowseService>.Instance).BrowseAsync("42", "/incoming", rootName: RootName(type), rootId: LibraryGuid);
            Assert.True(hostResult.Items.First(item => item.Name == "child").IsDirectory);
            Assert.False(hostResult.Items.First(item => item.Name == "report.csv").IsDirectory);
            Assert.Equal(2, probe.Releases);
        }
        Assert.All(probe.Requests, request => Assert.True(request["showfiles"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task ExportServiceProducedBrowseFixturesForRenderedEditorChecks()
    {
        var fixtures = new Dictionary<string, object>();
        foreach (var values in BrowseCases())
        {
            var type = (string)values[0]; var kind = (string)values[1]; var secure = (bool)values[2];
            var result = await new BrowseProbe(type, secure).TaskBrowse(kind);
            AssertListing(result.Items, type == "FileSystem" ? @"C:\incoming\child" : "/incoming/child");
            fixtures[$"{type}-{kind}{(secure ? "-TLS" : "")}"] = result;
        }
        var path = Environment.GetEnvironmentVariable("MAUI_SYNC_EDITOR_FIXTURE_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "TaskBrowseFixtures.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(fixtures, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }

    [Theory]
    [InlineData("1", true)] [InlineData("true", true)] [InlineData("\"1\"", true)] [InlineData("\"TRUE\"", true)]
    [InlineData("0", false)] [InlineData("false", false)] [InlineData("\"0\"", false)] [InlineData("\"false\"", false)]
    public async Task ExplicitFlagIsAuthoritativeInEitherCollection(string flag, bool directory)
    {
        foreach (var collection in new[] { "Dirs", "Files" })
        {
            var response = new JsonObject { [collection] = new JsonObject { [collection == "Dirs" ? "Dir" : "File"] =
                new JsonObject { ["Name"] = "child", ["isdir"] = JsonNode.Parse(flag), ["Size"] = 42 } } };
            var result = await new BrowseProbe("SSHFTP", response: response).TaskBrowse("Source");
            Assert.Single(result.Items);
            Assert.Equal(directory, result.Items[0].IsFolder);
            Assert.Equal(directory ? "/incoming/child" : "", result.Items[0].Path);
            Assert.Equal(directory ? "" : "42", result.Items[0].Size);
        }
    }

    [Theory]
    [InlineData("SSHFTP")] [InlineData("S3")] [InlineData("AzureBlob")] [InlineData("GoogleStorage")] [InlineData("SharePoint")]
    public async Task SeparateCollectionsAndScalarFoldersRemainBrowsable(string type)
    {
        var response = JsonNode.Parse("""{"Dirs":{"Dir":["child",{"Name":"other"}]},"Files":{"File":{"Name":"report.csv","Size":42}}}""")!.AsObject();
        var result = await new BrowseProbe(type, response: response).TaskBrowse("Destination");
        Assert.Equal(3, result.Items.Count);
        Assert.True(result.Items[0].IsFolder); Assert.True(result.Items[1].IsFolder);
        Assert.Equal("/incoming/child", result.Items.First(item => item.Name == "child").Path);
        Assert.False(result.Items.First(item => item.Name == "report.csv").IsFolder);
    }

    [Theory]
    [InlineData("FileSystem")] [InlineData("Share")] [InlineData("SSHFTP")]
    public async Task ExplicitNativeFolderPathsUseTheProtocolNormalizer(string type)
    {
        var path = type == "FileSystem" ? @"C:\incoming\child" : type == "Share" ? @"\\server\share\incoming\child" : "/incoming/child";
        var response = new JsonObject { ["Files"] = new JsonObject { ["File"] = new JsonObject
            { ["Name"] = "child", ["IsDir"] = 1, ["FullPath"] = path } } };
        var result = await new BrowseProbe(type, response: response).TaskBrowse("Source");
        Assert.True(result.Items[0].IsFolder);
        Assert.Equal(type == "FileSystem" ? @"C:\incoming\child" : "/incoming/child", result.Items[0].Path);
    }

    private static void AssertListing(IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> items, string folderPath)
    {
        Assert.Equal(2, items.Count);
        Assert.Equal("child", items[0].Name); Assert.True(items[0].IsFolder);
        Assert.Equal(folderPath, items[0].Path); Assert.Equal("", items[0].Size);
        Assert.Equal("report.csv", items[1].Name); Assert.False(items[1].IsFolder); Assert.Equal("42", items[1].Size);
    }

    [Theory]
    [InlineData("FileSystem")] [InlineData("Share")] [InlineData("siLock")] [InlineData("FTP")] [InlineData("SSHFTP")]
    public async Task NativeSeparateDirectoryPathsRemainBrowsable(string type)
    {
        var path = type == "FileSystem" ? @"C:\incoming\child" : type == "Share" ? @"\\server\share\incoming\child" : "/incoming/child";
        var response = new JsonObject { ["Dirs"] = new JsonObject { ["Dir"] = new JsonObject { ["DirName"] = path } },
            ["Files"] = new JsonObject { ["File"] = "report.csv" } };
        var result = await new BrowseProbe(type, response: response).TaskBrowse("Destination");
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].IsFolder);
        Assert.Equal(type == "FileSystem" ? @"C:\incoming\child" : "/incoming/child", result.Items[0].Path);
        Assert.Equal("report.csv", result.Items[1].Name); Assert.False(result.Items[1].IsFolder);
    }

    [Fact]
    public async Task ParentEntriesAreSkippedWithoutHidingDotNamedFilesAndFolders()
    {
        var response = JsonNode.Parse("""{"Files":{"File":[{"Name":".","IsDir":1},{"Name":"..","IsDir":1},{"Name":".hidden","IsDir":1},{"Name":".profile","IsDir":0}]}}""")!.AsObject();
        var result = await new BrowseProbe("SSHFTP", response: response).TaskBrowse("Source");
        Assert.Equal(new[] { ".hidden", ".profile" }, result.Items.Select(item => item.Name).ToArray());
        Assert.True(result.Items[0].IsFolder); Assert.False(result.Items[1].IsFolder);
    }

    private static string RootName(string type) => type switch { "S3" or "GoogleStorage" => "bucket", "AzureBlob" => "container", "SharePoint" => "Docs", _ => "" };

    private sealed class BrowseProbe
    {
        public IMoveItRestClient Rest { get; }
        public List<JsonObject> Requests { get; } = new();
        public int Releases { get; private set; }
        private readonly string _type;
        public BrowseProbe(string type, bool secure = false, JsonObject? response = null)
        {
            _type = type;
            var host = new JsonObject { ["ID"] = "42", ["Name"] = "Browse host", ["Host"] = "host.example.test", ["Port"] = 22,
                ["UNC"] = @"\\server\share", ["DefUsername"] = "account", ["Secure"] = secure ? "on" : "off",
                ["DefBucket"] = "bucket", ["DefContainer"] = "container", ["DefDocumentLibrary"] = "Docs", ["DefDocumentLibraryGuid"] = LibraryGuid,
                ["UsingAppAuthentication"] = 2, ["TenantName"] = "tenant", ["ClientID"] = "client", ["TenantID"] = "tenant-id",
                ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Certificate", ["DefClientCertSHA1"] = "AA BB" };
            Rest = DispatchProxy.Create<IMoveItRestClient, HostSharedCrudTests.CrudProxy>();
            ((HostSharedCrudTests.CrudProxy)(object)Rest).Handler = (method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.CheckoutHostV0Async) or nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(new JsonObject { [type] = host.DeepClone() }),
                nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Released(),
                _ when method.Name.StartsWith("Browse", StringComparison.Ordinal) => Browse((JsonObject)args[0]!),
                _ => throw new NotSupportedException(method.Name)
            };
            Task Released() { Releases++; return Task.CompletedTask; }
            Task<JsonObject> Browse(JsonObject request)
            {
                Requests.Add(request.DeepClone().AsObject());
                var result = response?.DeepClone().AsObject() ?? JsonNode.Parse("""{"Files":{"File":[{"Name":"report.csv","IsDir":0,"Size":42},{"Name":"child","IsDir":1,"Size":999}]}}""")!.AsObject();
                result["CurrentDir"] = type == "FileSystem" ? @"C:\incoming" : type == "Share" ? @"\\server\share\incoming" : "/incoming";
                return Task.FromResult(result);
            }
        }
        public Task<TraditionalTaskEditorFolderBrowseResult> TaskBrowse(string kind) => new TraditionalTaskEditorFolderBrowseService(Rest,
            NullLogger<TraditionalTaskEditorFolderBrowseService>.Instance).BrowseAsync("100", _type == "FileSystem" ? "0" : "42", kind,
            _type == "FileSystem" ? @"C:\incoming" : "/incoming", bucket: "bucket", container: "container", documentLibrary: "Docs", documentLibraryGuid: LibraryGuid);
    }
}
