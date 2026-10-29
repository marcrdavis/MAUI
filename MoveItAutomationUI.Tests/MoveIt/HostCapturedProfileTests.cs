using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Features.Editors.Hosts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostCapturedProfileTests
{
    private static IAutomationScriptService Scripts(bool unavailable = false) => Stub<IAutomationScriptService>((method, _) => method.Name == nameof(IAutomationScriptService.GetScriptsAsync)
        ? unavailable ? Task.FromException<IReadOnlyList<AutomationScript>>(new MoveItApiException("Inventory unavailable"))
        : Task.FromResult((IReadOnlyList<AutomationScript>)new[] { new AutomationScript { Id = "100401", Name = "Directory Parser" } })
        : throw new NotSupportedException(method.Name));

    [Fact]
    public async Task ReadOnlyHostNeverRequestsNativeSnapshotDespiteOptimisticInventoryFlags()
    {
        var common = new AutomationHost { Id = "42", Name = "Host", TypeCode = "FTP", Type = "FTP", CanEditDelete = true };
        var hosts = Stub<IAutomationHostService>((method, _) => method.Name switch
        {
            nameof(IAutomationHostService.GetHostAsync) => Task.FromResult(common),
            nameof(IAutomationHostService.GetHostDefinitionAsync) => Task.FromResult(new JsonObject { ["FTP"] = new JsonObject { ["ID"] = "42", ["Name"] = "Host" } }),
            nameof(IAutomationHostService.GetHostsAsync) => Task.FromResult<IReadOnlyList<AutomationHost>>([common]),
            _ => throw new Exception("Read-only Host must not call " + method.Name)
        });
        var tests = Stub<IHostConnectionTestService>((method, _) => method.Name == nameof(IHostConnectionTestService.SupportsHostType) ? true : throw new NotSupportedException(method.Name));
        var access = Stub<IMoveItObjectAccessService>((method, _) => method.Name == nameof(IMoveItObjectAccessService.GetObjectAsync)
            ? Task.FromResult(new MoveItObjectAccess(false, false, false)) : throw new NotSupportedException(method.Name));
        var model = await new HostEditorService(hosts, tests, Scripts(), access).GetHostAsync("42");
        Assert.False(model.CanEditMoveItFields);
        Assert.False(model.CanEditConfiguration);
        Assert.False(model.CanEditDelete);
    }

    [Theory]
    [InlineData("S3", "DefDLInteg", "ifsure")]
    [InlineData("AzureBlob", "DefDLInteg", "ignore")]
    [InlineData("SSHFTP", "ProxyType", "SOCKS5")]
    [InlineData("GoogleStorage", "ProxyType", "HTTPS")]
    public void CapturedSettingsKeepUnownedNativeState(string type, string key, string value)
    {
        var host = new JsonObject { ["UseDefStateCaching"] = 0, ["NativeOnly"] = new JsonObject { ["Units"] = "retain" } };
        HostCapturedProfiles.Apply(host, new JsonObject { [key] = value }, type);
        Assert.Equal(value, host[key]!.GetValue<string>());
        Assert.Equal(0, host["UseDefStateCaching"]!.GetValue<int>());
        Assert.Equal("retain", host["NativeOnly"]!["Units"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("AzureBlob", "DefDLInteg", "ifsure")]
    [InlineData("SSHFTP", "ProxyType", "HTTP")]
    [InlineData("SMTP", "DefBucket", "bucket")]
    [InlineData("GoogleStorage", "Secure", "0")]
    [InlineData("AzureBlob", "IsNonAmazonEndPoint", "1")]
    [InlineData("GoogleStorage", "S3AlternativeEndpoint", "storage.example.test")]
    public void CrossTypeOrUnverifiedSettingsAreRejected(string type, string key, string value)
        => Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(new JsonObject(), new JsonObject { [key] = value }, type));

    [Fact]
    public void AlternativeS3ProviderModeIsAppliedBeforeItsUrlAndUsesNativeFieldTypes()
    {
        var host = new JsonObject { ["IsNonAmazonEndPoint"] = 0 };
        HostCapturedProfiles.Apply(host, new JsonObject
        {
            ["S3AlternativeEndpoint"] = "  www.someurl.com  ", ["IsNonAmazonEndPoint"] = true
        }, "S3");
        Assert.Equal(1, host["IsNonAmazonEndPoint"]!.GetValue<int>());
        Assert.Equal("www.someurl.com", host["S3AlternativeEndpoint"]!.GetValue<string>());
        HostCapturedProfiles.Apply(host, new JsonObject { ["IsNonAmazonEndPoint"] = false }, "S3");
        Assert.Equal(0, host["IsNonAmazonEndPoint"]!.GetValue<int>());
        Assert.Equal("www.someurl.com", host["S3AlternativeEndpoint"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(host,
            new JsonObject { ["S3AlternativeEndpoint"] = "unused.example.test" }, "S3"));
    }

    [Fact]
    public void FtpClientCertificateCanBeChangedAndClearedWithoutEnablingTls()
    {
        var host = new JsonObject
        {
            ["Secure"] = "none", ["DefClientCertSHA1"] = "AA BB",
            ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Old Certificate"
        };
        HostCapturedProfiles.Apply(host, new JsonObject
        {
            ["DefClientCertSHA1"] = "CC DD", ["DefClientCertStore"] = "My",
            ["DefClientCertSubject"] = "New Certificate"
        }, "FTP");
        Assert.Equal("CC DD", host["DefClientCertSHA1"]!.GetValue<string>());
        Assert.Equal("My", host["DefClientCertStore"]!.GetValue<string>());
        Assert.Equal("New Certificate", host["DefClientCertSubject"]!.GetValue<string>());
        Assert.Equal("none", host["Secure"]!.GetValue<string>());
        HostCapturedProfiles.Apply(host, new JsonObject
        {
            ["DefClientCertSHA1"] = "", ["DefClientCertStore"] = "", ["DefClientCertSubject"] = ""
        }, "FTP");
        Assert.All(HostResourceContracts.CertificateFields, key => Assert.Equal("", host[key]!.GetValue<string>()));
        Assert.Equal("none", host["Secure"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("6", 6)]
    public void SftpCompressionPersistsTheNativeNumericLevel(string selected, int expected)
    {
        var host = new JsonObject();
        HostCapturedProfiles.Apply(host, new JsonObject { ["CmpL"] = selected }, "SSHFTP");
        Assert.Equal(expected, host["CmpL"]!.GetValue<int>());
    }

    [Fact]
    public void OAuthModeIsAppliedBeforeCredentialFieldsRegardlessOfJsonOrder()
    {
        var host = new JsonObject { ["UseOAuth"] = 0 };
        HostCapturedProfiles.Apply(host, new JsonObject { ["OAuthClientID"] = "client", ["OAuthClientSecret"] = "  secret  ", ["UseOAuth"] = true }, "SMTP");
        Assert.Equal(1, host["UseOAuth"]!.GetValue<int>());
        Assert.Equal("  secret  ", host["OAuthClientSecret"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(host, new JsonObject { ["DefPassword"] = "password" }, "SMTP"));
    }

    [Fact]
    public void NullAndEmptySecretsPreserveNativeStorageAndNeverForwardOldCiphertext()
    {
        foreach (var replacement in new string?[] { null, "" })
        {
            var host = new JsonObject { ["DefPassword"] = "SAVED_CIPHER", ["ProxyPassword"] = "PROXY_CIPHER" };
            var patch = new JsonObject { ["DefPassword"] = replacement };
            HostCapturedProfiles.Apply(host, patch, "SSHFTP");
            Assert.Equal("SAVED_CIPHER", host["DefPassword"]!.GetValue<string>());
            HostCapturedProfiles.PreserveUnsubmittedSecrets(host, patch, "SSHFTP");
            Assert.Null(host["DefPassword"]);
            Assert.Null(host["ProxyPassword"]);
        }
    }

    [Fact]
    public void GooglePrivateKeyKeepsItsExactMultilineText()
    {
        const string key = "  -----BEGIN PRIVATE KEY-----\r\nTEST\r\n-----END PRIVATE KEY-----  ";
        var host = new JsonObject { ["UseOAuth"] = 1 };
        HostCapturedProfiles.Apply(host, new JsonObject { ["OAuthClientSecret"] = key }, "GoogleStorage");
        Assert.Equal(key, host["OAuthClientSecret"]!.GetValue<string>());
    }

    [Fact]
    public void LargeByteLimitsDoNotUseFloatingPointParsing()
    {
        var host = new JsonObject();
        HostCapturedProfiles.Apply(host, new JsonObject { ["DefMxBy"] = "9007199254740993" }, "S3");
        Assert.Equal(9007199254740993L, host["DefMxBy"]!.GetValue<long>());
    }

    [Fact]
    public void SftpFingerprintQueryRetainsKexCharactersAndExcludesSecrets()
    {
        var query = HostSshSecurity.BuildFingerprintQuery(new("42", "sftp.example.test", 22,
            "gss-test+abc==", "WEBStandard", "proxy.example.test", 8080, "user"));
        Assert.Equal("gss-test+abc==", query["kexAlgs"]);
        Assert.Equal("WEBStandard", query["proxyType"]);
        Assert.Equal("8080", query["proxyPort"]);
        Assert.DoesNotContain(query.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
        var direct = HostSshSecurity.BuildFingerprintQuery(new("42", "sftp.example.test", 22));
        Assert.False(direct.ContainsKey("proxyHost"));
        Assert.Throws<InvalidOperationException>(() => HostSshSecurity.BuildFingerprintQuery(new("42", "", 22)));
    }

    [Theory]
    [InlineData("POP3")]
    [InlineData("SMTP")]
    [InlineData("GoogleStorage")]
    [InlineData("SSHFTP")]
    [InlineData("FTP")]
    public async Task EditorShellNeverReturnsStoredSecretsAndUsesTheSharedFieldCatalog(string type)
    {
        var common = new AutomationHost { Id = "42", Name = "Host", TypeCode = type, Type = type, CanEditDelete = true };
        var hosts = Stub<IAutomationHostService>((method, _) => method.Name switch
        {
            nameof(IAutomationHostService.GetHostAsync) => Task.FromResult(common),
            nameof(IAutomationHostService.GetHostConfigurationSnapshotAsync) => Task.FromResult(new HostConfigurationSnapshot(common.Id, common.Name, common.TypeCode, "<Host ID=\"" + common.Id + "\" />")),
            nameof(IAutomationHostService.GetHostDefinitionAsync) => Task.FromResult(new JsonObject { [type] = new JsonObject
                { ["ID"] = "42", ["DefPassword"] = "PRIVATE_SAVED_VALUE", ["OAuthClientSecret"] = "PRIVATE_OAUTH_VALUE", ["UseOAuth"] = 0 } }),
            nameof(IAutomationHostService.GetHostsAsync) => Task.FromResult((IReadOnlyList<AutomationHost>)new[] { common }),
            nameof(IAutomationHostService.GetSshAlgorithmsAsync) => Task.FromResult(HostSshAlgorithmTests.Catalog()),
            nameof(IAutomationHostService.GetSshClientKeysAsync) => Task.FromResult((IReadOnlyList<HostSshClientKeyOption>)new[] { new HostSshClientKeyOption("123", "Friendly Key") }),
            _ => throw new NotSupportedException(method.Name)
        });
        var tests = Stub<IHostConnectionTestService>((method, _) => method.Name == nameof(IHostConnectionTestService.SupportsHostType)
            ? true : throw new NotSupportedException(method.Name));
        var model = await new HostEditorService(hosts, tests, Scripts()).GetHostAsync("42");
        Assert.True(model.CanEditConfiguration);
        Assert.Equal(HostCapturedProfiles.ProfileId(type), model.WriteProfile);
        var json = JsonSerializer.Serialize(model);
        Assert.DoesNotContain("PRIVATE_SAVED_VALUE", json);
        Assert.DoesNotContain("PRIVATE_OAUTH_VALUE", json);
        Assert.Contains(model.ConfigurationSections.SelectMany(section => section.Fields), field => field.Key == "DefPassword" && field.Sensitive && field.Editable);
        if (type == "SSHFTP")
            Assert.Contains(model.ConfigurationSections.SelectMany(section => section.Fields), field => field.Key == "DefClientKeyID" && field.Options!.Any(option => option.Value == "123" && option.Label == "Friendly Key"));
    }

    [Fact]
    public async Task AllEditorProfilesKeepBooleanControlTypesAndReadOnlyPermissions()
    {
        // Generate real service DTOs for the browser layout regression when CI requests them.
        // No native service or credentials are used by these fixtures.
        var fixtures = new Dictionary<string, HostEditorShellViewModel>();
        foreach (var type in new[] { "POP3", "SMTP", "SSHFTP", "S3", "AzureBlob", "GoogleStorage", "FTP", "Share", "siLock", "SharePoint", "FileSystem", "OneDrive" })
        {
            foreach (var scenario in new[] { "basic", "readonly", "tls-none", "s3-alternative", "s3-alternative-readonly", "oauth", "office365", "run-as", "proxy", "client-secret", "cache-always", "cache-task", "cache-timed", "catalog-unavailable", "ftp-explicit", "ftp-ccc", "ftp-control-only", "ftp-control-ccc", "ftp-implicit-ccc", "ftp-custom", "ftp-script", "scripts-unavailable", "s3-profile", "s3-imds", "onedrive-secret" })
            {
                if (scenario.StartsWith("s3-", StringComparison.Ordinal) && type != "S3") continue;
                if (scenario == "onedrive-secret" && type != "OneDrive") continue;
                if (scenario == "tls-none" && type is not ("FTP" or "siLock")) continue;
                if (scenario.StartsWith("s3-alternative", StringComparison.Ordinal) && type != "S3") continue;
                if (scenario == "office365" && type is not ("POP3" or "SMTP")) continue;
                if (scenario == "run-as" && type != "Share") continue;
                if ((scenario.StartsWith("ftp-", StringComparison.Ordinal) || scenario == "scripts-unavailable") && type != "FTP") continue;
                if (scenario == "catalog-unavailable" && type != "SSHFTP") continue;
                if (scenario.StartsWith("cache-", StringComparison.Ordinal) && type == "SMTP") continue;
                if (scenario == "client-secret" && type != "SharePoint") continue;
                if (scenario == "oauth" && type is not ("POP3" or "SMTP" or "GoogleStorage")) continue;
                if (scenario == "proxy" && type is not ("SSHFTP" or "S3" or "AzureBlob" or "GoogleStorage" or "FTP" or "SharePoint")) continue;
                var hostId = type == "FileSystem" ? "0" : "42";
                var common = new AutomationHost
                {
                    Id = hostId, Name = type == "FileSystem" ? "(Default)" : $"Example {type}", Description = "Host editor layout regression",
                    TypeCode = type, Type = type, Secure = "Secure", CanEditDelete = !scenario.EndsWith("readonly", StringComparison.Ordinal)
                };
                var raw = new JsonObject
                {
                    ["ID"] = hostId, ["Host"] = "host.example.test", ["Port"] = 22,
                    ["UNC"] = @"\\server\share", ["DriveLetter"] = "Z:", ["DefUsername"] = "account",
                    ["DefPassword"] = "PRIVATE_SAVED_VALUE", ["OAuthClientSecret"] = "PRIVATE_OAUTH_VALUE",
                    ["Secure"] = type == "FTP" ? "implicit" : "1", ["IgnoreCertProbs"] = 0, ["UseOAuth"] = scenario is "oauth" or "office365" ? 1 : 0,
                    ["OAuthClientID"] = "client", ["OAuthScope"] = "scope", ["OAuthTokenURL"] = "https://auth.example.test/token",
                    ["ProjectID"] = "project", ["DefBucket"] = "bucket", ["DefContainer"] = "container",
                    ["IsNonAmazonEndPoint"] = scenario.StartsWith("s3-alternative", StringComparison.Ordinal) ? 1 : 0,
                    ["S3AlternativeEndpoint"] = "www.someurl.com",
                    ["DefClientKeyID"] = "123", ["ProxyType"] = scenario == "proxy" ? (type is "SSHFTP" or "FTP" ? "SOCKS5" : type == "SharePoint" ? "HTTPS" : "HTTP") : "None",
                    ["ProxyHost"] = "proxy.example.test", ["ProxyPort"] = 8080, ["ProxyUsername"] = "proxy-user",
                    ["ProxyPassword"] = "PRIVATE_PROXY_VALUE", ["UseDefStateCaching"] = scenario.StartsWith("cache-", StringComparison.Ordinal) ? 0 : 1, ["DelOldStateDays"] = 30,
                    ["AdjDST"] = 1, ["UseNotif"] = 1, ["DefWinCopyFileAPI"] = 0, ["DefPreserveFileTimestamp"] = 1,
                    ["DefMxFi"] = 0, ["DefMxBy"] = 0, ["DefConnTimeoutSecs"] = 30, ["DefDataTimeoutSecs"] = 30,
                    ["DefRetryCount"] = 2, ["DefRetryTimeoutSecs"] = 30, ["DefRescanSecs"] = 0,
                    ["DefAddressFrom"] = "sender@example.test", ["DefSort"] = "Filename,A", ["DotNET"] = 1,
                    ["RenameAfterUpload"] = 1, ["TempUploadName"] = "[OrigName].tmp", ["DefSetFileAttr"] = 1, ["DefDestFileAttr"] = "644",
                    ["DefBlindDL"] = 0, ["DefBlindUL"] = 0, ["DefRsm"] = 1, ["DefPassive"] = 1, ["DefReuseSSL"] = 1,
                    ["DefXS"] = 0, ["NATUseServerIPForData"] = 0, ["NATClientIP"] = "", ["DefAB"] = "binary",
                    ["DefDLInteg"] = "never", ["DefMD5Get"] = "Never", ["DefMD5File"] = "[OrigName].md5",
                    ["SSHFingerprint"] = "RSA-SHA2-256: EXAMPLE", ["EncList"] = "aes256-ctr,aes128-ctr",
                    ["KexAlgs"] = "curve25519-sha256,diffie-hellman-group14-sha256", ["CmpL"] = 0, ["OptimizeSSHBuf"] = 1,
                    ["Site"] = "site", ["TenantName"] = "tenant", ["SiteUrlPrefix"] = "/sites/", ["TenantID"] = "tenant", ["ClientID"] = "client",
                    ["UsingAppAuthentication"] = scenario == "client-secret" ? 1 : 2, ["ClientSecret"] = "PRIVATE_CLIENT_SECRET", ["RealmID"] = "realm",
                    ["DefClientCertSHA1"] = "AA BB", ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Test Certificate",
                    ["DefDocumentLibrary"] = "Documents", ["DefDocumentLibraryGuid"] = "11111111-2222-3333-4444-555555555555",
                    ["ProxyIgnoreCertErrors"] = 0, ["CustomDir"] = 0, ["CustomDirColDate"] = 1, ["CustomDirColFilename"] = 1, ["CustomDirSkipTop"] = 0, ["CustomDirSkipBottom"] = 0
                };
                if (scenario is "s3-profile" or "s3-imds") raw["AuthMethod"] = scenario == "s3-profile" ? 1 : 2;
                if (type == "OneDrive")
                {
                    raw["UseOAuth"] = 1;
                    if (scenario == "onedrive-secret") foreach (var key in HostResourceContracts.CertificateFields) raw.Remove(key);
                    else raw.Remove("OAuthClientSecret");
                }
                if (type == "FileSystem")
                {
                    raw["DefRetryCount"] = 3; raw["UseNotif"] = 0; raw["RenameAfterUpload"] = 0;
                    raw["TempUploadName"] = "CTMP[Rnd:4]"; raw["DefPreserveFileTimestamp"] = 0;
                    raw["DelOldStateDays"] = 0;
                }
                if (scenario == "tls-none") raw["Secure"] = type == "FTP" ? "none" : "0";
                if (scenario == "ftp-ccc") raw["Secure"] = "on-ccc";
                if (scenario == "ftp-explicit") raw["Secure"] = "on";
                if (scenario == "ftp-control-only") raw["Secure"] = "tls-c";
                if (scenario == "ftp-control-ccc") raw["Secure"] = "tls-c-ccc";
                if (scenario == "ftp-implicit-ccc") raw["Secure"] = "implicit-ccc";
                if (type == "FTP") raw["Port"] = raw["Secure"]!.GetValue<string>().StartsWith("implicit", StringComparison.Ordinal) ? 990 : 21;
                if (scenario == "office365")
                {
                    raw["OAuthScope"] = HostMailOAuthSettings.Scope;
                    raw["OAuthTokenURL"] = "https://login.microsoftonline.com/tenantid1/oauth2/v2.0/token";
                }
                if (scenario == "run-as") { raw.Remove("DefUsername"); raw.Remove("DefPassword"); }
                if (scenario == "ftp-custom") raw["CustomDir"] = 1;
                if (scenario == "ftp-script" || scenario == "scripts-unavailable") raw["DirScriptID"] = "100401";
                if (scenario.StartsWith("cache-", StringComparison.Ordinal))
                {
                    raw["StateCacheTime"] = scenario == "cache-always" ? -1 : scenario == "cache-task" ? 0 : 15;
                    if (scenario == "cache-timed") raw["StateCacheUnit"] = "Minutes";
                }
                var hosts = Stub<IAutomationHostService>((method, _) => method.Name switch
                {
                    nameof(IAutomationHostService.GetHostAsync) => Task.FromResult(common),
                    nameof(IAutomationHostService.GetHostConfigurationSnapshotAsync) => Task.FromResult(new HostConfigurationSnapshot(common.Id, common.Name, common.TypeCode, "<Host ID=\"" + common.Id + "\" />")),
                    nameof(IAutomationHostService.GetHostDefinitionAsync) => Task.FromResult(new JsonObject { [type] = raw }),
                    nameof(IAutomationHostService.GetHostsAsync) => Task.FromResult((IReadOnlyList<AutomationHost>)new[] { common }),
                    nameof(IAutomationHostService.GetSshAlgorithmsAsync) => scenario == "catalog-unavailable"
                        ? Task.FromException<HostSshAlgorithmCatalog>(new MoveItApiException("PRIVATE_NATIVE_ERROR", System.Net.HttpStatusCode.ServiceUnavailable))
                        : Task.FromResult(HostSshAlgorithmTests.Catalog()),
                    nameof(IAutomationHostService.GetSshClientKeysAsync) => Task.FromResult((IReadOnlyList<HostSshClientKeyOption>)new[] { new HostSshClientKeyOption("123", "Friendly Key") }),
                    _ => throw new NotSupportedException(method.Name)
                });
                var tests = Stub<IHostConnectionTestService>((method, _) => method.Name == nameof(IHostConnectionTestService.SupportsHostType)
                    ? true : throw new NotSupportedException(method.Name));
                var versionedSession = type == "OneDrive" || scenario is "s3-profile" or "s3-imds"
                    ? MoveIt2026HostTests.Sessions(new MoveItSessionState { Info = new MoveItInfo { CentralVersion = "2026.0" } }) : null;
                var host = await new HostEditorService(hosts, tests, Scripts(scenario == "scripts-unavailable"), sessions: versionedSession).GetHostAsync(hostId);
                var fields = host.ConfigurationSections.SelectMany(section => section.Fields).ToArray();
                if (type == "Share")
                {
                    Assert.Equal(scenario == "run-as" ? "run-as" : "credentials", fields.First(field => field.Key == HostAuthenticationSettings.UncModeKey).Value);
                    var drive = fields.First(field => field.Key == "DriveLetter");
                    Assert.Equal("Select", drive.ValueType);
                    Assert.Equal(24, drive.Options!.Count); // None plus D: through Z:.
                    Assert.Equal("", drive.Options[0].Value); Assert.Equal("D:", drive.Options[1].Value); Assert.Equal("Z:", drive.Options[^1].Value);
                }
                if (type is "Share" or "siLock")
                    Assert.Equal(scenario != "readonly", fields.First(field => field.Key == "DefPassword").Editable);
                if (scenario == "office365")
                {
                    Assert.Equal("microsoft365", fields.First(field => field.Key == HostMailOAuthSettings.ProviderKey).Value);
                    Assert.Equal("tenantid1", fields.First(field => field.Key == HostMailOAuthSettings.TenantKey).Value);
                }
                if (scenario == "catalog-unavailable")
                {
                    foreach (var field in fields.Where(field => field.ValueType == "AlgorithmList"))
                    {
                        Assert.False(field.Editable); Assert.Null(field.Options); Assert.Contains("Reload", field.HelpText);
                    }
                    Assert.Contains(fields, field => field.Key == "Host" && field.Editable);
                    Assert.Equal("aes256-ctr,aes128-ctr", fields.First(field => field.Key == "EncList").Value);
                }
                if (type == "FTP")
                {
                    Assert.Equal(scenario == "ftp-custom" ? "custom" : scenario is "ftp-script" or "scripts-unavailable" ? "script" : "automatic", fields.First(field => field.Key == HostFtpDirectoryParsing.ModeKey).Value);
                    var parser = fields.First(field => field.Key == "DirScriptID");
                    Assert.Equal(scenario != "readonly" && scenario != "scripts-unavailable", parser.Editable);
                    Assert.Contains(parser.Options!, option => option.Value == "100401");
                }
                var cacheFlag = fields.FirstOrDefault(field => field.Key == "UseDefStateCaching");
                if (cacheFlag is not null)
                {
                    Assert.Equal("Boolean", cacheFlag.ValueType);
                    Assert.Equal(!scenario.EndsWith("readonly", StringComparison.Ordinal), cacheFlag.Editable);
                    Assert.Equal(scenario.StartsWith("cache-", StringComparison.Ordinal) ? "No" : "Yes", cacheFlag.Value);
                }
                if (type == "GoogleStorage")
                {
                    Assert.DoesNotContain(fields, field => field.Key == "Secure");
                    Assert.DoesNotContain(host.ConfigurationSections, section => section.Key == "security");
                }
                if (type == "FileSystem")
                {
                    Assert.Equal(new[] { "general", "uploads", "limits", "advanced" }, host.ConfigurationSections.Select(section => section.Key));
                    Assert.Equal(scenario != "readonly", host.CanEditConfiguration);
                    Assert.DoesNotContain(fields, field => field.Key is "Host" or "Port" or "DefPassword" or "ID2" or "DefConnTimeoutSecs");
                    Assert.Contains(fields, field => field.Key == "TempUploadName");
                }
                if (type == "S3")
                {
                    var provider = fields.First(field => field.Key == "IsNonAmazonEndPoint");
                    var endpoint = fields.First(field => field.Key == "S3AlternativeEndpoint");
                    Assert.Equal("Boolean", provider.ValueType);
                    Assert.Equal(scenario.StartsWith("s3-alternative", StringComparison.Ordinal) ? "Yes" : "No", provider.Value);
                    Assert.Equal("Text", endpoint.ValueType);
                    Assert.Equal("www.someurl.com", endpoint.Value);
                    Assert.Equal("IsNonAmazonEndPoint", endpoint.ConditionKey);
                    Assert.Equal("true", endpoint.ConditionValue);
                }
                if (scenario.EndsWith("readonly", StringComparison.Ordinal)) Assert.DoesNotContain(fields, field => field.Editable);
                Assert.DoesNotContain("PRIVATE_", JsonSerializer.Serialize(host));
                fixtures[$"{type}-{scenario}"] = new HostEditorShellViewModel { Host = host };
            }
        }
        var fixturePath = Environment.GetEnvironmentVariable("MAUI_HOST_EDITOR_FIXTURE_PATH");
        if (!string.IsNullOrWhiteSpace(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixturePath))!);
            await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(fixtures, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }

    private static T Stub<T>(Func<System.Reflection.MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
