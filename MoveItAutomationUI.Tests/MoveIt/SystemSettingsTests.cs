using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class SystemSettingsTests
{
    // Native Part 1 capture. The unknown nested value detects accidental projection-only PUTs.
    private const string Capture = """
    {"Globals":{"AllowMultRuns":0,"AS2LogDirectory":"","AuditXferRetries":1,"DebugLevel":60,"DefStateCacheTime":-1,
    "EventLogAuditLevel":0,"EventLogLevel":0,"EventLogName":"Application","LogFile":"MOVEitC.log","LogToDisk":1,
    "LogToTaskLog":1,"TaskLogDir":"","SFTPLib":"IPWorks","MaxLogFileSize":10485760,"MaxSimulTasks":20,
    "PollIntervalSecs":5,"SuppressHashing":0,"SuppressQueuedTasksAtStartup":0,"NumTaskLogsToKeep":5,
    "FutureSetting":{"values":[1,"preserve",null]}}}
    """;
    private static JsonObject Native() => JsonNode.Parse(Capture)!.AsObject();
    private static SystemLoggingSettings Logging() => SystemLoggingSettings.Read(Native()["Globals"]!.AsObject());

    [Fact]
    public void BrowserProjectionRoundTripsNativeAs2NameAndLongFileSize()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var value = Logging() with { AS2LogDirectory = @"C:\ASx", MaxLogFileSize = 2147483648L };
        var json = JsonSerializer.SerializeToNode(new SystemSettingsState(value, "revision"), options)!;
        Assert.Equal(value.AS2LogDirectory, json["logging"]!["as2LogDirectory"]!.GetValue<string>());
        Assert.Contains(json["logging"]!.AsObject(), item => item.Key == "as2LogDirectory");
        Assert.DoesNotContain(json["logging"]!.AsObject(), item => item.Key == "aS2LogDirectory");
        var request = JsonSerializer.Deserialize<SystemSettingsSaveRequest>(json.ToJsonString(), options)!;
        Assert.Equal(value, request.Logging);
        Assert.Equal("revision", request.EditVersion);
        var native = request.Logging!.Apply(Native()["Globals"]!.AsObject());
        Assert.Equal(value.AS2LogDirectory, native["AS2LogDirectory"]!.GetValue<string>());
        Assert.False(native.ContainsKey("as2LogDirectory"));
    }

    [Fact]
    public async Task OpeningUsesProtectedCheckoutAndCleanupWithoutWriting()
    {
        using var native = new NativeHandler();
        var state = await Service(native).ReadAsync(default);
        Assert.Equal(Logging(), state.Logging);
        Assert.NotEmpty(state.EditVersion);
        Assert.Equal(new[] { "POST /api/v0/globals/checkout", "DELETE /api/v0/globals/checkout" }, native.Calls);
        Assert.True(native.Writes == 0);
        Assert.Null(Assert.Single(native.CheckoutInputs));
    }

    [Fact]
    public async Task RecoveryForcesCheckoutThenAllowsTheSettingsFormToLoadNormally()
    {
        using var native = new NativeHandler { DenyCheckout = true }; var service = Service(native);
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => service.ReadAsync(default));
        var recovered = await service.RecoverCheckoutAsync(default);
        Assert.NotEmpty(recovered.EditVersion); Assert.Equal(Logging(), recovered.Logging);
        Assert.True(native.CheckoutInputs[1]!["force"]!.GetValue<bool>()); Assert.True(native.Writes == 0);
        Assert.NotEmpty((await service.ReadAsync(default)).EditVersion);
        Assert.Null(native.CheckoutInputs[2]);
    }

    [Fact]
    public async Task SaveUsesFullFreshEnvelopePreservesOtherSectionsAndSupportsTwoGigabytes()
    {
        using var native = new NativeHandler();
        var service = Service(native);
        var state = await service.ReadAsync(default);
        native.Calls.Clear();
        var changed = state.Logging with { DebugLevel = 70, MaxLogFileSize = 2147483648L, LogFile = "system.log",
            TaskLogDir = @"C:\Logs", AS2LogDirectory = @"C:\ASx", NumTaskLogsToKeep = 6, AuditXferRetries = false };
        var result = await service.SaveLoggingAsync(changed, state.EditVersion, default);
        Assert.Equal(changed, result.Logging);
        Assert.NotEqual(state.EditVersion, result.EditVersion);
        Assert.True(native.Writes == 1);
        Assert.Equal(new[] { "POST /api/v0/globals/checkout", "PUT /api/v0/globals", "DELETE /api/v0/globals/checkout", "POST /api/v0/globals/checkout", "DELETE /api/v0/globals/checkout" }, native.Calls);
        var payload = native.Data["Globals"]!.AsObject();
        Assert.Equal(2147483648L, payload["MaxLogFileSize"]!.GetValue<long>());
        Assert.Equal(0, payload["AuditXferRetries"]!.GetValue<int>());
        var original = Native()["Globals"]!.AsObject();
        foreach (var name in new[] { "AllowMultRuns", "DefStateCacheTime", "EventLogAuditLevel", "EventLogLevel", "EventLogName", "SFTPLib", "MaxSimulTasks", "PollIntervalSecs", "SuppressHashing", "SuppressQueuedTasksAtStartup", "FutureSetting" })
            Assert.True(JsonNode.DeepEquals(original[name], payload[name]), name);
    }

    [Fact]
    public async Task SaveConfirmsStateWhenNativePutRetainsItsCheckout()
    {
        using var native = new NativeHandler { PutRetainsCheckout = true };
        var service = Service(native); var state = await service.ReadAsync(default);
        var changed = state.Logging with { DebugLevel = 70 };
        Assert.Equal(changed, (await service.SaveLoggingAsync(changed, state.EditVersion, default)).Logging);
        Assert.False(native.CheckedOut);
        Assert.True(native.Writes == 1);
    }

    [Fact]
    public async Task RejectedReadbackCheckoutDoesNotReleaseAnotherOwnersLock()
    {
        using var native = new NativeHandler(); var service = Service(native); var state = await service.ReadAsync(default);
        native.DenyReadback = true; native.Calls.Clear();
        await Assert.ThrowsAsync<SystemSettingsSaveUncertainException>(() => service.SaveLoggingAsync(state.Logging with { DebugLevel = 70 }, state.EditVersion, default));
        Assert.True(native.Writes == 1);
        Assert.Equal(new[] { "POST /api/v0/globals/checkout", "PUT /api/v0/globals", "DELETE /api/v0/globals/checkout", "POST /api/v0/globals/checkout" }, native.Calls);
    }

    [Fact]
    public async Task ConcurrentChangeAnywhereInGlobalsBlocksWriteAndStillReleasesCheckout()
    {
        using var native = new NativeHandler();
        var service = Service(native); var state = await service.ReadAsync(default);
        native.Data["Globals"]!["MaxSimulTasks"] = 25;
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => service.SaveLoggingAsync(state.Logging with { DebugLevel = 70 }, state.EditVersion, default));
        Assert.True(native.Writes == 0);
        Assert.EndsWith("DELETE /api/v0/globals/checkout", native.Calls.Last());
    }

    [Theory]
    [InlineData(-1, 0, 5)]
    [InlineData(61, 1024, 5)]
    [InlineData(60, 2147483649L, 5)]
    [InlineData(60, -1, 5)]
    [InlineData(60, 1024, -1)]
    public async Task InvalidLoggingDoesNotAcquireCheckout(int level, long bytes, int count)
    {
        using var native = new NativeHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(native).SaveLoggingAsync(Logging() with { DebugLevel = level, MaxLogFileSize = bytes, NumTaskLogsToKeep = count }, "version", default));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void LoggingPatchDoesNotMutateInputAndUsesNativeBooleanNumbers()
    {
        var current = Native()["Globals"]!.AsObject(); var before = current.ToJsonString();
        var changed = Logging() with { LogToDisk = false, LogToTaskLog = false, AuditXferRetries = false, LogFile = "", TaskLogDir = "", AS2LogDirectory = "", MaxLogFileSize = 0 };
        var patched = changed.Apply(current);
        Assert.Equal(before, current.ToJsonString());
        Assert.Equal(0, patched["LogToDisk"]!.GetValue<int>());
        Assert.Equal(changed, SystemLoggingSettings.Read(patched));
    }

    [Theory]
    [InlineData("LogFile")]
    [InlineData("TaskLogDir")]
    [InlineData("AS2LogDirectory")]
    [InlineData("AuditXferRetries")]
    [InlineData("DebugLevel")]
    public void MissingNativeFieldsAreNotInvented(string name)
    {
        var data = Native()["Globals"]!.AsObject(); data.Remove(name);
        Assert.Throws<InvalidOperationException>(() => SystemLoggingSettings.Read(data));
    }

    [Fact]
    public async Task MissingEnvelopeIsAnErrorAndReleasesOwnedCheckout()
    {
        using var native = new NativeHandler { Data = new JsonObject() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(native).ReadAsync(default));
        Assert.True(native.Calls.Count == 2);
    }

    [Fact]
    public async Task DeniedCheckoutDoesNotWriteOrReleaseSomeoneElsesCheckout()
    {
        using var native = new NativeHandler { DenyCheckout = true };
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => Service(native).ReadAsync(default));
        Assert.True(native.Calls.Count == 1);
    }

    [Fact]
    public async Task DefinitiveValidationFailureIsNotRetriedAndCheckoutIsReleased()
    {
        using var native = new NativeHandler(); var service = Service(native); var state = await service.ReadAsync(default);
        native.WriteStatus = HttpStatusCode.UnprocessableEntity;
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => service.SaveLoggingAsync(state.Logging with { DebugLevel = 70 }, state.EditVersion, default));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.True(native.Writes == 1);
        Assert.EndsWith("DELETE /api/v0/globals/checkout", native.Calls.Last());
    }

    [Theory]
    [InlineData("ack")]
    [InlineData("readback")]
    [InlineData("network")]
    [InlineData("cleanup")]
    [InlineData("cancel")]
    public async Task UncertainSavesAreNeverReplayedAndCleanupUsesIndependentToken(string fault)
    {
        using var native = new NativeHandler(); var service = Service(native); var state = await service.ReadAsync(default);
        native.Fault = fault;
        await Assert.ThrowsAsync<SystemSettingsSaveUncertainException>(() => service.SaveLoggingAsync(state.Logging with { DebugLevel = 70 }, state.EditVersion, default));
        Assert.True(native.Writes == 1);
        Assert.False(native.CleanupCancelled);
        Assert.EndsWith("DELETE /api/v0/globals/checkout", native.Calls.Last());
    }

    [Fact]
    public async Task CancelledSaveStillReleasesCheckoutWithIndependentCancellation()
    {
        using var native = new NativeHandler(); var service = Service(native); var state = await service.ReadAsync(default);
        using var request = new CancellationTokenSource();
        native.Fault = "cancel"; native.AfterPut = request.Cancel;
        await Assert.ThrowsAsync<SystemSettingsSaveUncertainException>(() => service.SaveLoggingAsync(state.Logging with { DebugLevel = 70 }, state.EditVersion, request.Token));
        Assert.True(request.IsCancellationRequested);
        Assert.False(native.CleanupCancelled);
        Assert.True(native.Writes == 1);
        Assert.EndsWith("DELETE /api/v0/globals/checkout", native.Calls.Last());
    }

    [Fact]
    public void ConfigurationMutationPersistsToggleWithDefinitionAndBindsForNextRequest()
    {
        var store = DispatchProxy.Create<IAppSettingsDocumentStore, TemplateProxy>();
        var mutator = new ConfigurationSettingsMutator(null!, store);
        var root = new JsonObject();
        mutator.Apply(root, new ConfigurationSettingsSaveRequest { UseMauiSystemSettingsEditor = true });
        var setting = root["Editors"]!["UseMauiSystemSettingsEditor"]!;
        Assert.True(setting["Value"]!.GetValue<bool>());
        Assert.Equal("Use MAUI Settings Editor", setting["Name"]!.GetValue<string>());
        Assert.True(setting["Experimental"]!.GetValue<bool>());
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()))).Build();
        Assert.True(configuration.GetSection(MauiEditorOptions.SectionName).Get<MauiEditorOptions>()!.UseMauiSystemSettingsEditor.Value);
        mutator.Apply(root, new ConfigurationSettingsSaveRequest { UseMauiSystemSettingsEditor = false });
        Assert.False(root["Editors"]!["UseMauiSystemSettingsEditor"]!["Value"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NativeAdministratorIsRequiredBeforeAnyRequest()
    {
        using var native = new NativeHandler();
        var service = Service(native, false);
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => service.ReadAsync(default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task DisabledFeatureRejectsForgedEditorRequest()
    {
        using var native = new NativeHandler();
        var controller = new SystemSettingsEditorController(Service(native), new ConfigurationBuilder().Build(), NullLogger<SystemSettingsEditorController>.Instance);
        Assert.IsType<NotFoundObjectResult>(await controller.Shell(default));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void BothEndpointsRequireAdminPostAndAntiforgeryAndFeatureDefaultsOff()
    {
        Assert.False(new MauiEditorOptions().UseMauiSystemSettingsEditor.Value);
        Assert.Contains(typeof(SystemSettingsEditorController).GetCustomAttributes<AuthorizeAttribute>(), item => item.Policy == MauiSecurityPolicies.Admin);
        foreach (var name in new[] { "Shell", "Save" })
        {
            var method = typeof(SystemSettingsEditorController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemLoggingSettings>("{}"));
    }

    internal static SystemSettingsService Service(NativeHandler handler, bool admin = true)
    {
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        ((SessionProxy)(object)sessions).Admin = admin;
        var client = new MoveItRestClient(new Factory(handler), sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        return new SystemSettingsService(client, sessions);
    }
    public class TemplateProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IAppSettingsDocumentStore.ReadTemplate)
            ? new JsonObject { [MauiEditorOptions.SectionName] = JsonSerializer.SerializeToNode(new MauiEditorOptions()) }
            : throw new NotSupportedException(method?.Name);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    public class SessionProxy : DispatchProxy
    {
        public bool Admin { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Info = new MoveItInfo { IsAdmin = Admin } }
            : throw new NotSupportedException(method?.Name);
    }
    internal sealed class NativeHandler : HttpMessageHandler
    {
        public JsonObject Data { get; set; } = Native();
        public JsonObject Policy { get; set; } = JsonNode.Parse("""
        {"IPLockoutEnabled":false,"IPLockoutTries":5,"IPLockoutTime":5,"IPLockoutUnits":"minutes","IPAutoUnlockEnabled":true,"IPUnlockTime":15,"IPUnlockUnits":"minutes",
        "UserLockoutEnabled":false,"UserLockoutTries":5,"UserLockoutTime":5,"UserLockoutUnits":"minutes","UserAutoUnlockEnabled":true,"UserUnlockTime":15,"UserUnlockUnits":"minutes","FuturePolicy":"preserve"}
        """)!.AsObject();
        public List<string> Trusted { get; } = ["1.1.1.1"];
        public List<(string Method, JsonObject Body)> PolicyRequests { get; } = [];
        public string PolicyFault { get; set; } = "";
        public int PolicyWrites { get; private set; }
        public int TrustedWrites { get; private set; }
        public int FailTrustedWrite { get; set; }
        public List<string> Calls { get; } = [];
        public List<JsonObject?> CheckoutInputs { get; } = [];
        public Dictionary<string, JsonObject> Lockouts { get; } = new(StringComparer.Ordinal)
        {
            ["ip"] = JsonNode.Parse("""{"items":[{"IP":"192.168.198.1","Stamp":"2026-10-22 08:25:49"}]}""")!.AsObject(),
            ["user"] = JsonNode.Parse("""{"items":[{"User":"miuser","Stamp":"2026-10-22 08:25:49"}]}""")!.AsObject()
        };
        public string UnlockFault { get; set; } = "";
        public int LockoutDeletes { get; private set; }
        public JsonArray? LastUnlockBody { get; private set; }
        public int Writes { get; private set; }
        public bool DenyCheckout { get; set; }
        public bool DenyReadback { get; set; }
        public bool PutRetainsCheckout { get; set; }
        public bool CheckedOut { get; private set; }
        public HttpStatusCode WriteStatus { get; set; } = HttpStatusCode.OK;
        public string Fault { get; set; } = "";
        public bool CleanupCancelled { get; private set; }
        public Action? AfterPut { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add(request.Method + " " + path);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            if (path.StartsWith("/api/v1/lockouts/", StringComparison.Ordinal))
            {
                var kind = path.Split('/').Last(); Assert.True(kind is "ip" or "user");
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Null(request.Content);
                    if (UnlockFault == "load") return Reply("{\"message\":\"Lockouts unavailable\"}", HttpStatusCode.InternalServerError);
                    if (LockoutDeletes > 0 && UnlockFault == "readback-network") throw new HttpRequestException("Readback failed");
                    if (LockoutDeletes > 0 && UnlockFault == "malformed-readback") return Reply("{\"items\":null}");
                    return Reply(Lockouts[kind].ToJsonString());
                }
                Assert.Equal(HttpMethod.Delete, request.Method); LockoutDeletes++;
                LastUnlockBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsArray();
                Assert.True(LastUnlockBody.Count > 0);
                if (UnlockFault == "validation") return Reply("{\"message\":\"Unlock rejected\"}", HttpStatusCode.UnprocessableEntity);
                var field = kind == "ip" ? "IP" : "User";
                var values = LastUnlockBody.Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                var items = Lockouts[kind]["items"]!.AsArray();
                if (UnlockFault != "unchanged")
                    for (var index = items.Count - 1; index >= 0; index--) if (values.Contains(items[index]![field]!.GetValue<string>())) items.RemoveAt(index);
                if (UnlockFault == "network") throw new HttpRequestException("Unlock response lost");
                if (UnlockFault == "cancel") throw new OperationCanceledException("Unlock response cancelled");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/v0/systemSettings/iplockout")
            {
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Null(request.Content); var data = Policy.ToJsonString();
                    return Reply(data);
                }
                Assert.Equal(HttpMethod.Post, request.Method);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                PolicyRequests.Add(("POST", body)); PolicyWrites++;
                if (PolicyFault == "validation") return Reply("{\"message\":\"Invalid policy\"}", HttpStatusCode.UnprocessableEntity);
                if (PolicyFault != "readback") Policy = body;
                if (PolicyFault == "network") throw new HttpRequestException("Connection dropped after policy POST");
                if (PolicyFault == "cancel") throw new OperationCanceledException("Request ended after policy POST");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/v0/systemSettings/trustedip")
            {
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Null(request.Content);
                    return Reply(new JsonObject { ["TrustedIP"] = new JsonArray(Trusted.Select(ip => (JsonNode)new JsonObject { ["ip"] = ip }).ToArray()) }.ToJsonString());
                }
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                PolicyRequests.Add((request.Method.Method, body)); TrustedWrites++;
                Assert.True(body.Count == 1); Assert.True(body["TrustedIP"]!.AsArray().Count == 1);
                if (FailTrustedWrite == TrustedWrites) throw new HttpRequestException("Trusted IP response lost");
                var address = body["TrustedIP"]![0]!.GetValue<string>();
                if (request.Method == HttpMethod.Post) Trusted.Add(address);
                else { Assert.Equal(HttpMethod.Delete, request.Method); Trusted.Remove(address); }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Null(request.Content); CleanupCancelled = ct.IsCancellationRequested;
                if (Fault != "cleanup") CheckedOut = false;
                return Reply(Fault == "cleanup" ? "{\"status\":\"error\",\"errorcode\":999,\"message\":\"Cleanup failed\"}" : "{\"status\":\"error\",\"errorcode\":4015}");
            }
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/api/v0/globals/checkout", path);
                var input = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
                CheckoutInputs.Add(input); var force = input?["force"]?.GetValue<bool>() == true;
                if (!force && (DenyCheckout || DenyReadback && Writes > 0 || CheckedOut))
                    return Reply("{\"status\":\"error\",\"errorcode\":409,\"message\":\"Checked out\"}");
                if (force) DenyCheckout = false;
                CheckedOut = true;
                return Reply(Data.ToJsonString());
            }
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/api/v0/globals", path);
            Writes++;
            if (WriteStatus != HttpStatusCode.OK) return Reply("{\"message\":\"Invalid settings\"}", WriteStatus);
            Data = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if (!PutRetainsCheckout) CheckedOut = false;
            AfterPut?.Invoke();
            if (Fault == "readback") Data["Globals"]!["DebugLevel"] = 0;
            if (Fault == "network") throw new HttpRequestException("Connection dropped after PUT");
            if (Fault == "cancel") throw new OperationCanceledException("Request ended after PUT");
            return Reply(Fault == "ack" ? "{}" : "{\"id\":\"-1\",\"status\":\"success\",\"errorcode\":0,\"refreshRequired\":true}");
        }
        private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
