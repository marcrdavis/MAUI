using System.Net;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Only New Host shells use this captured POST contract. Configured spreadsheet
// creation and credential-preserving native clones intentionally retain COM.
internal static partial class HostV0Creation
{
    private static readonly IReadOnlyDictionary<string, (string Label, string Fields)> Profiles =
        new Dictionary<string, (string Label, string Fields)>(StringComparer.OrdinalIgnoreCase)
    {
        ["SSHFTP"] = ("SSH/SFTP", """{"DelOldStateDays":0,"UseDefStateCaching":1,"DefUsername":"","DefPassword":null,"Host":"","Port":22,"ID2":"","DefMD5File":"MD5SUM","DefMD5Get":"Never","DefRsm":0,"IgnoreCertProbs":1,"SSHFingerprint":"","DefBlindDL":0,"DefConnTimeoutSecs":120,"DefRescanSecs":0,"RenameAfterUpload":0,"TempUploadName":"CTMP[Rnd:4]","DefMxFi":0,"DefMxBy":0,"DefClientKeyID":"0","AdjDST":0,"CmpL":6,"DefSetFileAttr":0,"DefPreserveFileTimestamp":0,"DefDestFileAttr":"","OptimizeSSHBuf":1,"ProxyType":"None","DefAccount":null}"""),
        ["Share"] = ("UNC Share", """{"DelOldStateDays":0,"UseDefStateCaching":1,"UNC":"","DriveLetter":"","UseNotif":0,"DefRescanSecs":0,"DefMxFi":0,"DefMxBy":0,"AdjDST":1,"DefWinCopyFileAPI":1,"ID2":"","DefPreserveFileTimestamp":0}"""),
        ["siLock"] = ("MOVEit Transfer", """{"DelOldStateDays":0,"UseDefStateCaching":1,"DefUsername":"","DefPassword":null,"Host":"","Port":443,"ID2":"","DotNET":1,"UseNotif":0,"ScriptPath":"/","Secure":1,"IgnoreCertProbs":0,"DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefSort":"Filename,A","DefMxFi":0,"DefMxBy":0,"DefClientCertSHA1":"","DefClientCertStore":"","DefClientCertSubject":"","DefAccount":null}"""),
        ["FTP"] = ("FTP/FTPS", """{"DelOldStateDays":0,"UseDefStateCaching":1,"DefUsername":"","DefPassword":null,"Host":"","Port":21,"ID2":"","DefMD5File":"MD5SUM","DefMD5Get":"Never","DefRsm":0,"Secure":"none","IgnoreCertProbs":0,"DefAccount":null,"DefAB":"binary","DefPassive":1,"DefBlindDL":0,"DefBlindUL":0,"DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefRescanSecs":0,"RenameAfterUpload":0,"TempUploadName":"CTMP[Rnd:4]","DefMxFi":0,"DefMxBy":0,"DefClientCertSHA1":"","DefClientCertStore":"","DefClientCertSubject":"","AdjDST":0,"DefXS":0,"DefReuseSSL":0,"NATUseServerIPForData":1,"NATClientIP":"","AddComSignon":"","AddCom":"","AddComAX":"","ProxyType":"None"}"""),
        ["POP3"] = ("POP3 (Incoming Email)", """{"DelOldStateDays":0,"UseDefStateCaching":1,"DefUsername":"","DefPassword":null,"Host":"","Port":110,"ID2":"","DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"Secure":0,"IgnoreCertProbs":0,"UseOAuth":0}"""),
        ["SMTP"] = ("SMTP (Outgoing Email)", """{"DefUsername":"","DefPassword":null,"Host":"","Port":25,"ID2":"","DefAddressFrom":"","DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"Secure":0,"IgnoreCertProbs":0,"UseOAuth":0}"""),
        ["S3"] = ("Amazon S3", """{"DelOldStateDays":0,"UseDefStateCaching":1,"AdjDST":0,"DefUsername":"","DefPassword":"","DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefMxBy":0,"DefMxFi":0,"DefRescanSecs":0,"ProxyType":"None","Secure":1,"DefDLInteg":"never","DefBucket":""}"""),
        ["AzureBlob"] = ("Azure Blob", """{"DelOldStateDays":0,"UseDefStateCaching":1,"AdjDST":0,"DefUsername":"","DefPassword":"","DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefMxBy":0,"DefMxFi":0,"DefRescanSecs":0,"ProxyType":"None","Secure":1,"DefDLInteg":"never","DefContainer":""}"""),
        ["SharePoint"] = ("SharePoint", """{"DelOldStateDays":0,"UseDefStateCaching":1,"AdjDST":0,"DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefMxBy":0,"DefMxFi":0,"DefRescanSecs":0,"ProxyType":"None","Secure":1,"TenantName":"","SiteUrlPrefix":"/sites/","Site":"","UsingAppAuthentication":2,"ClientID":"","TenantID":"","DefDocumentLibrary":"","DefDocumentLibraryGuid":"","DefClientCertSHA1":"","DefClientCertStore":"","DefClientCertSubject":""}"""),
        ["OneDrive"] = ("Microsoft OneDrive", """{"DelOldStateDays":0,"UseDefStateCaching":1,"AdjDST":0,"DefUsername":"","DefPassword":null,"DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefMxBy":0,"DefMxFi":0,"DefRescanSecs":0,"ProxyType":"None","Secure":1,"DefBucket":"","UseOAuth":1,"TenantID":"","OAuthClientID":"","DefClientCertSHA1":"","DefClientCertStore":"","DefClientCertSubject":""}"""),
        ["GoogleStorage"] = ("Google Storage", """{"DelOldStateDays":0,"UseDefStateCaching":1,"AdjDST":0,"DefUsername":"","DefPassword":"","DefConnTimeoutSecs":60,"DefDataTimeoutSecs":60,"DefMxBy":0,"DefMxFi":0,"DefRescanSecs":0,"ProxyType":"None","Secure":1,"DefBucket":"","UseOAuth":0}"""),
    };

    public static async Task<MoveItCreatedHost> CreateAsync(IMoveItRestClient rest, string name, string type, CancellationToken cancellationToken, string? unc = null, SshAlgorithmOptions? defaults = null, string? resourceGroup = null)
    {
        var hostName = (name ?? "").Trim();
        if (hostName.Length is 0 or > 254) throw new InvalidOperationException("Host Name must contain 1–254 characters.");
        var typeCode = Profiles.Keys.FirstOrDefault(key => key.Equals((type ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select a supported Host Type.");
        var profile = Profiles[typeCode];
        var host = JsonNode.Parse("""{"ID":"","Desc":"","DefRetryCount":0,"DefRetryTimeoutSecs":30,"UsedByCount":0,"OverridesTotal":0,"OverridesNoPerm":false,"Group":[]}""")!.AsObject();
        host["Name"] = hostName;
        if (!string.IsNullOrWhiteSpace(resourceGroup)) host["Group"] = new JsonArray(resourceGroup.Trim());
        foreach (var field in JsonNode.Parse(profile.Fields)!.AsObject()) host[field.Key] = field.Value?.DeepClone();
        if (typeCode == "Share") host["UNC"] = ValidateUnc(unc);
        if (typeCode == "SSHFTP")
        {
            var algorithms = await HostSshAlgorithms.LoadAsync(rest, cancellationToken, defaults);
            host["EncList"] = HostSshAlgorithms.DefaultSelection(algorithms.Encryption);
            host["KexAlgs"] = HostSshAlgorithms.DefaultSelection(algorithms.KeyExchange);
        }
        var payload = new JsonObject { [typeCode] = host };
        cancellationToken.ThrowIfCancellationRequested();
        JsonObject response;
        try { response = await rest.CreateHostV0Async(payload, cancellationToken); }
        catch (MoveItAuthenticationException) { throw; }
        catch (MoveItApiException ex) when (ex.StatusCode is not null && (int)ex.StatusCode is >= 400 and < 500 && ex.StatusCode != HttpStatusCode.RequestTimeout) { throw; }
        catch (Exception ex) { throw new HostV0CreationOutcomeException(null, hostName, ex, "POST"); }

        var id = Text(response, "id");
        var stage = "acknowledgement";
        try
        {
            if (!UsableId(id) || Text(response, "status") != "success" || Text(response, "errorcode") != "0")
                throw new HostCreationReadbackException("invalid-acknowledgement");
            stage = "readback";
            await VerifyCreatedAsync(rest, id, hostName, typeCode, cancellationToken);
            return new MoveItCreatedHost(id, hostName, profile.Label, typeCode);
        }
        catch (Exception ex) { throw new HostV0CreationOutcomeException(UsableId(id) ? id : null, hostName, ex, stage); }
    }

    internal static string ValidateUnc(string? value)
    {
        var unc = (value ?? "").Trim();
        if (unc.Length == 0) throw new InvalidOperationException("Resource UNC is required.");
        if (!unc.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Resource UNC must begin with \\\\.");
        return unc;
    }

    private static bool UsableId(string id) => id.Length > 0 && id is not ("0" or "-1");
    private static string Text(JsonObject value, string key) => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(value, key)).Trim();
}

public sealed class HostV0CreationOutcomeException : InvalidOperationException
{
    public string? HostId { get; }
    public string HostName { get; }
    public string VerificationStage { get; }
    public string VerificationReason { get; }
    public HostV0CreationOutcomeException(string? id, string name, Exception inner, string stage = "creation")
        : base((string.IsNullOrWhiteSpace(id)
            ? "MAUI could not confirm whether the Host was created. Inspect Hosts before trying to create it again."
            : stage == "readback"
                ? $"MOVEit created Host '{name}' (ID {id}), but MAUI could not verify its saved details. Inspect the existing Host before creating another copy."
                : $"Host '{name}' (ID {id}) may have been created, but its saved state could not be verified. Inspect that Host before creating another copy.")
            + $" Verification: {stage} ({Reason(inner)}).", inner)
        => (HostId, HostName, VerificationStage, VerificationReason) = (id, name, stage, Reason(inner));

    private static string Reason(Exception error) => error switch
    {
        HostCreationReadbackException failure => failure.Reason,
        MoveItApiException api when api.StatusCode is not null => "HTTP-" + (int)api.StatusCode,
        OperationCanceledException => "canceled",
        _ => "transport-or-response-error"
    };
}
