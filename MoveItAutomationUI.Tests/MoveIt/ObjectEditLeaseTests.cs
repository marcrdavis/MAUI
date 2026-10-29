using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ObjectEditLeaseTests
{
    [Theory]
    [InlineData("Task", "101")]
    [InlineData("Host", "101")]
    [InlineData("Host", "0")]
    [InlineData("Script", "101")]
    [InlineData("DateList", "101")]
    public async Task SecondOpenUsesNativeLockDetailsAndPreservesTheOriginalEditor(string type, string id)
    {
        using var f = new Fixture();
        var original = await f.Open(type, id);
        var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Open(type, id));
        Assert.Equal("This object has been checked out to native-owner since 2026-10-24 17:27:53", error.Message);
        Assert.True(f.Transport.Acquires == 2); Assert.True(f.Transport.ForcedAcquires == 0);
        await f.Service.HeartbeatAsync(original.LeaseId, f.Owner, default);
        await f.Service.CloseAsync(original.LeaseId, f.Owner, default);
        Assert.True(f.Transport.Releases == 1); Assert.Empty(f.Journal.Records);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    public async Task RetryAfterARequestFailureUsesNormalNativeCheckout(string type)
    {
        using var f = new Fixture(); f.Transport.LoseAcquire = true;
        await Assert.ThrowsAsync<IOException>(() => f.Open(type));
        f.Transport.LoseAcquire = false;
        var locked = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Open(type));
        Assert.Contains("native-owner since 2026-10-24 17:27:53", locked.Message);
        Assert.DoesNotContain("MAUI", locked.Message);
        f.Transport.ClearCheckout(type, "101"); // The native owner has released checkout.
        var current = await f.Open(type);
        Assert.True(current.Required); Assert.True(f.Transport.ForcedAcquires == 0);
        await f.Service.CloseAsync(current.LeaseId, f.Owner, default);
        Assert.Empty(f.Journal.Records);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    public async Task AReleasedNativeCheckoutReplacesTheOldLocalEditorWithoutForce(string type)
    {
        using var f = new Fixture(); var original = await f.Open(type);
        f.Transport.ClearCheckout(type, "101");
        var current = await f.Open(type);
        Assert.NotEqual(original.LeaseId, current.LeaseId);
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(original.LeaseId, f.Owner, default));
        await f.Service.CloseAsync(original.LeaseId, f.Owner, default);
        Assert.True(f.Transport.Releases == 0); Assert.True(f.Transport.ForcedAcquires == 0);
        await f.Service.HeartbeatAsync(current.LeaseId, f.Owner, default);
        await f.Service.CloseAsync(current.LeaseId, f.Owner, default);
        Assert.True(f.Transport.Releases == 1); Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public void ExistingJournalFormatKeepsTheInternalPendingRequestMarker()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<ObjectEditLeaseRecord>("{\"Held\":false,\"Uncertain\":true,\"OpenedUtc\":\"2026-10-24T17:27:53Z\"}")!;
        Assert.True(restored.PendingRequest);
        var serialized = System.Text.Json.JsonSerializer.Serialize(restored);
        Assert.DoesNotContain("OpenedUtc", serialized);
        Assert.Contains("\"Uncertain\":true", serialized);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    public async Task NativeLockRejectionRetiresTheOldLeaseWithoutCleanupOfTheNewOwner(string type)
    {
        using var f = new Fixture(); var lease = await f.Open(type);
        using (var operation = await f.Service.EnterAsync(type, "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => ObjectCheckout.ExecuteAsync(type, "101", _ => Task.CompletedTask,
                _ => Task.FromException<bool>(new MoveItApiException("This section has been checked out to alice since 2026-10-24 12:00:00", HttpStatusCode.Conflict)),
                _ => throw new InvalidOperationException("Do not release the new owner"), default));
            Assert.Contains("alice since", error.Message);
        }
        Assert.True(f.Transport.Acquires == 1); Assert.True(f.Transport.Releases == 0); Assert.Empty(f.Journal.Records);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HostConnectionAndPathTestsBorrowTheOpeningLeaseAndPreserveItOnFailure(bool path, bool fail)
    {
        using var f = new Fixture();
        var lease = await f.Open("Host");
        var acquires = 0; var releases = 0; var browses = 0;
        JsonObject Host() => new() { ["Share"] = new JsonObject { ["ID"] = "101", ["Name"] = "Share",
            ["UNC"] = @"\\server\share", ["DefPreserveFileTimestamp"] = 1 } };
        var rest = DispatchProxy.Create<IMoveItRestClient, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)rest).Handler = (method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(Host()),
            nameof(IMoveItRestClient.CheckoutHostV0Async) => Acquire(),
            nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release(),
            nameof(IMoveItRestClient.BrowseFileSystemHostV0Async) => Browse(),
            _ => throw new NotSupportedException(method.Name)
        };
        Task<JsonObject> Acquire() { acquires++; return Task.FromResult(Host()); }
        Task Release() { releases++; return Task.CompletedTask; }
        Task<JsonObject> Browse() { browses++; return fail ? Task.FromException<JsonObject>(new MoveItApiException("Browse unavailable")) : Task.FromResult(new JsonObject()); }
        var tests = new HostConnectionTestService(rest, NullLogger<HostConnectionTestService>.Instance);
        Task<HostConnectionTestResult> Test() => path ? tests.TestHostPathAsync("101", "folder") : tests.TestHostAsync("101");
        using (var operation = await f.Service.EnterAsync("Host", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            Assert.Equal(fail ? "Failed" : "Passed", (await Test()).Status);
            Assert.Equal(0, acquires); Assert.Equal(0, releases);
        }
        Assert.Equal(1, browses); Assert.Equal(1, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases);
        await f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default);
        await Assert.ThrowsAsync<InvalidOperationException>(Test); // An unrelated caller cannot steal this editor's checkout.
        Assert.Equal(1, browses);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        await Test();
        Assert.Equal(1, acquires); Assert.Equal(1, releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowseBorrowsOpeningCheckoutWithoutNativeReleaseOrReacquisition(bool fail)
    {
        using var f = new Fixture();
        var lease = await f.Service.OpenAsync("Host", "101", f.Owner, default);
        using (var operation = await f.Service.EnterAsync("Host", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            Task<string> Browse() => ObjectCheckout.ReadAsync<string>("Host", "101",
                _ => throw new Exception("Browse must borrow checkout."),
                _ => fail ? Task.FromException<string>(new InvalidOperationException("Listing failed.")) : Task.FromResult("listing"),
                _ => throw new Exception("Browse must not release the editor checkout."), default);
            if (fail) await Assert.ThrowsAsync<InvalidOperationException>(Browse);
            else Assert.Equal("listing", await Browse());
            Assert.True(ObjectCheckout.HasLifetimeFor("Host", "101"));
            Assert.Equal(1, f.Transport.Acquires);
            Assert.Equal(0, f.Transport.Releases);
        }
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Equal(1, f.Transport.Releases);
    }

    [Fact]
    public async Task RetiredOwnershipCannotAcquireOrDeleteNativeCheckoutAfterRestart()
    {
        using var f = new Fixture();
        f.Journal.Records["retired"] = new ObjectEditLeaseRecord { LeaseId = "retired", Type = "Task", Id = "101",
            Server = f.Owner.Session.WebAdminUrl, Username = f.Owner.Session.Username, Relinquished = true, Held = true, PendingRequest = true };
        await f.Service.StartAsync(default);
        Assert.Equal(0, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases); Assert.Empty(f.Journal.Records);
        await f.Service.StopAsync(default);
    }

    [Theory]
    [InlineData("Task", "101")]
    [InlineData("Host", "101")]
    [InlineData("Host", "0")]
    [InlineData("Script", "101")]
    [InlineData("DateList", "101")]
    public async Task ExplicitAdminOverrideRetiresOldLeaseWithoutDeletingAnotherEditorsCheckout(string type, string id)
    {
        using var f = new Fixture();
        var original = await f.Service.OpenAsync(type, id, f.Owner, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.OverrideAsync(type, id, f.Owner, default));
        Assert.Equal(1, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var recovered = await f.Service.OverrideAsync(type, id, f.Owner, default);
        Assert.True(f.Transport.ForcedAcquires == 1);
        Assert.True(recovered.Required); Assert.NotEqual(original.LeaseId, recovered.LeaseId);
        Assert.Equal(2, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases);
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(original.LeaseId, f.Owner, default));
        using (var operation = await f.Service.EnterAsync(type, id, recovered.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
            Assert.Equal("saved", await ObjectCheckout.ExecuteAsync(type, id, _ => throw new Exception("Must use recovered checkout"),
                _ => Task.FromResult("saved"), _ => throw new Exception("Lease owns release"), default));
        Assert.Equal(3, f.Transport.Acquires); Assert.Equal(1, f.Transport.Releases);
        Assert.True(f.Transport.ForcedAcquires == 1); // Save reacquisition must remain unforced.
        await f.Service.CloseAsync(recovered.LeaseId, f.Owner, default);
        Assert.Equal(2, f.Transport.Releases); Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task RevisionConflictBeforeWritingDoesNotReleaseOrReacquireNativeCheckout()
    {
        using var f = new Fixture(); var lease = await f.Open();
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
            await Assert.ThrowsAsync<ObjectEditConflictException>(() => ObjectCheckout.ExecuteAsync<string>("Task", "101",
                _ => throw new Exception("Reuse opening checkout"), _ => throw new ObjectEditConflictException("Task"),
                _ => throw new Exception("Do not release a stolen checkout"), default));
        Assert.Equal(1, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Equal(0, f.Transport.Releases); Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task AdminOverrideCanReserveAnEditWhenTheDefaultLifetimeSettingIsOff()
    {
        using var f = new Fixture(false); f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var lease = await f.Service.OverrideAsync("Host", "0", f.Owner, default);
        Assert.True(lease.Required); Assert.Equal(1, f.Transport.Acquires);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Equal(1, f.Transport.Releases);
    }

    [Fact]
    public async Task FailedAdminOverrideDoesNotDeleteNativeCheckoutOrKeepAnActiveLease()
    {
        using var f = new Fixture(); await f.Open(); f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        f.Transport.NativeRejection = true;
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Service.OverrideAsync("Task", "101", f.Owner, default));
        Assert.True(f.Transport.ForcedAcquires == 1);
        Assert.Equal(0, f.Transport.Releases); Assert.Empty(f.Journal.Records);
    }

    [Theory]
    [InlineData("Task", "0")]
    [InlineData("Host", "-1")]
    [InlineData("Host", "00")]
    [InlineData("Script", "0")]
    [InlineData("DateList", "0")]
    public async Task OnlyTheCanonicalHostZeroIdentityIsAllowedForBuiltInObjects(string type, string id)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.OpenAsync(type, id, f.Owner, default));
        Assert.Equal(0, f.Transport.Acquires); Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task DisabledSettingDoesNotAcquireOrJournal()
    {
        using var f = new Fixture(false);
        Assert.False((await f.Open()).Required);
        Assert.Equal(0, f.Transport.Acquires);
        Assert.Empty(f.Journal.Records);
    }

    [Theory]
    [InlineData("Task", "101")]
    [InlineData("Host", "101")]
    [InlineData("Host", "0")]
    public async Task OpenSaveReacquireAndCloseShareOneOwnershipLifecycle(string type, string id)
    {
        using var f = new Fixture();
        var lease = await f.Service.OpenAsync(type, id, f.Owner, default);
        Assert.True(lease.Required);
        Assert.Equal(1, f.Transport.Acquires);
        using (var operation = await f.Service.EnterAsync(type, id, lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            Assert.True(ObjectCheckout.CanEditCheckedOut(type, id, false));
            Assert.False(ObjectCheckout.CanEditCheckedOut(type, "999", false));
            var saved = await ObjectCheckout.ExecuteAsync(type, id, _ => throw new Exception("Must reuse the opening checkout"),
                _ => Task.FromResult("saved"), _ => throw new Exception("Lifetime service owns release"), default);
            Assert.Equal("saved", saved);
        }
        Assert.Equal(2, f.Transport.Acquires);
        Assert.Equal(1, f.Transport.Releases);
        Assert.Single(f.Journal.Records);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Equal(2, f.Transport.Releases);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task NativeSchedulesHandOffCheckoutVerifyInsideComAndRestoreLifetimeOwnership()
    {
        using var f = new Fixture();
        const string before = "<Task ID='101' Name='Task'><Schedules /></Task>";
        const string after = "<Task ID='101' Name='Task'><Schedules><Schedule ID='11' /></Schedules></Task>";
        f.Transport.RevisionXml = before;
        var lease = await f.Open();
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            var saved = await ObjectCheckout.ExecuteNativeAsync("Task", "101", _ =>
            {
                ObjectCheckout.VerifyNativeLifetimeRevision("Task", "101", before);
                Assert.Throws<ObjectEditConflictException>(() => ObjectCheckout.VerifyNativeLifetimeRevision("Task", "101", "<Task ID='101' Name='Changed' />"));
                Assert.Equal(1, f.Transport.Releases);
                f.Transport.RevisionXml = after;
                return Task.FromResult(after);
            }, xml => xml, default);
            Assert.Equal(after, saved);
        }
        Assert.Equal(2, f.Transport.Acquires);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Equal(2, f.Transport.Releases);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    public async Task InterruptedPutRecoveryRequiresExplicitAdministratorBeforeReleasing(string type)
    {
        using var f = new Fixture();
        var lease = await f.Open(type);
        var interrupted = new Journal();
        using (var operation = await f.Service.EnterAsync(type, "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            await ObjectCheckout.ExecuteAsync(type, "101", _ => Task.CompletedTask, async _ =>
            {
                var record = f.Journal.Records[lease.LeaseId];
                Assert.False(record.Held);
                Assert.True(record.PendingRequest);
                // Snapshot the durable state at the point where PUT can release checkout
                // and another editor could acquire it before a process restart.
                await interrupted.WriteAsync(record, default);
                return true;
            }, _ => Task.CompletedTask, default);
        }
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        var transport = new Transport { RejectAcquire = true };
        using var recovery = new ObjectEditLeaseService(transport, interrupted, f.Options,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), f.Clock, NullLogger<ObjectEditLeaseService>.Instance);
        await recovery.StartAsync(default);
        await recovery.SweepAsync(default);
        Assert.Equal(0, transport.Releases);
        Assert.Single(interrupted.Records);
        transport.RejectAcquire = false;
        await recovery.SweepAsync(default);
        Assert.Equal(0, transport.Acquires); Assert.Equal(0, transport.Releases);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var replacement = await recovery.OverrideAsync(type, "101", f.Owner, default);
        await recovery.CloseAsync(replacement.LeaseId, f.Owner, default);
        Assert.Empty(interrupted.Records);
        await recovery.StopAsync(default);
    }

    [Fact]
    public async Task JournalFailureBeforePutBlocksTheWriteAndExpiresTheLeaseForRecovery()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        var edited = false;
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            f.Journal.FailWrite = true;
            var error = await Assert.ThrowsAsync<ObjectEditLeaseException>(() => ObjectCheckout.ExecuteAsync("Task", "101",
                _ => Task.CompletedTask, _ => { edited = true; return Task.FromResult(true); }, _ => Task.CompletedTask, default));
            Assert.False(error.MayHaveSaved);
        }
        Assert.False(edited);
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default));
        f.Journal.FailWrite = false;
        await f.Service.SweepAsync(default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task FailedNativeHandoffDoesNotInvokeComAndInvalidatesTheEditingLease()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        var edited = false;
        f.Transport.FailRelease = true;
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            var error = await Assert.ThrowsAsync<ObjectEditLeaseException>(() => ObjectCheckout.ExecuteNativeAsync("Task", "101",
                _ => { edited = true; return Task.FromResult("<Task />"); }, xml => xml, default));
            Assert.False(error.MayHaveSaved);
        }
        Assert.False(edited);
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default));
        f.Transport.FailRelease = false;
        await f.Service.SweepAsync(default);
        Assert.Single(f.Journal.Records);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var replacement = await f.Service.OverrideAsync("Task", "101", f.Owner, default);
        await f.Service.CloseAsync(replacement.LeaseId, f.Owner, default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task FailedBrowserCloseWaitsForExplicitRecoveryWhenReleaseAcknowledgementWasLost()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        f.Transport.FailRelease = true;
        await Assert.ThrowsAsync<IOException>(() => f.Service.CloseAsync(lease.LeaseId, f.Owner, default));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default));
        f.Transport.FailRelease = false;
        await f.Service.SweepAsync(default);
        Assert.True(f.Transport.Acquires == 1); Assert.Single(f.Journal.Records);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var replacement = await f.Service.OverrideAsync("Task", "101", f.Owner, default);
        await f.Service.CloseAsync(replacement.LeaseId, f.Owner, default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task CancelledShutdownStillStopsWorkerAndPreservesBusyCheckoutForRecovery()
    {
        using var f = new Fixture();
        await f.Service.StartAsync(default);
        var lease = await f.Open();
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        {
            using var shutdown = new CancellationTokenSource();
            shutdown.Cancel();
            await f.Service.StopAsync(shutdown.Token);
            await f.Service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(f.Journal.Records);
            Assert.Equal(0, f.Transport.Releases);
        }
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
    }

    [Fact]
    public async Task DuplicateEditorsAndUnownedWorkCannotStealOrReleaseCheckout()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Open());
        var other = f.Owner with { SessionId = "different-browser" };
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(lease.LeaseId, other, default));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.CloseAsync(lease.LeaseId, other, default));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.EnterAsync("Host", "101", lease.LeaseId, f.Owner, default));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.EnterAsync("Task", "999", lease.LeaseId, f.Owner, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectCheckout.ExecuteAsync("Task", "101",
            _ => throw new Exception("Must not acquire"), _ => Task.FromResult(true), _ => throw new Exception("Must not release"), default));
        Assert.Equal(0, f.Transport.Releases);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
    }

    [Fact]
    public async Task HeartbeatExtendsLeaseButCannotExtendLoginOrReviveAnExpiredLease()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        f.Clock.Advance(TimeSpan.FromMinutes(4));
        await f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default);
        f.Clock.Advance(TimeSpan.FromMinutes(4));
        await f.Service.SweepAsync(default);
        Assert.Equal(0, f.Transport.Releases);
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(lease.LeaseId, f.Owner, default));
        await f.Service.SweepAsync(default);
        Assert.Equal(1, f.Transport.Releases);

        var second = await f.Open("Host");
        for (var i = 0; i < 4; i++) { f.Clock.Advance(TimeSpan.FromMinutes(4)); await f.Service.HeartbeatAsync(second.LeaseId, f.Owner, default); }
        f.Clock.Advance(TimeSpan.FromMinutes(4));
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.HeartbeatAsync(second.LeaseId, f.Owner, default));
        await f.Service.SweepAsync(default);
        Assert.Equal(2, f.Transport.Releases);
    }

    [Fact]
    public async Task SaveGapConflictBlocksFurtherSavesAndKeepsRecoveryOwnership()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        f.Transport.ChangeRevision = true;
        using (var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            var error = await Assert.ThrowsAsync<ObjectEditLeaseException>(() => ObjectCheckout.ExecuteAsync("Task", "101",
                _ => Task.CompletedTask, _ => Task.FromResult(true), _ => Task.CompletedTask, default));
            Assert.True(error.MayHaveSaved);
        }
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default));
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
    }

    [Fact]
    public async Task DefinitiveCheckoutRejectionNeverCallsRelease()
    {
        using var f = new Fixture();
        f.Transport.RejectAcquire = true;
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Open());
        await f.Service.SweepAsync(default);
        Assert.Equal(0, f.Transport.Releases);
        Assert.Empty(f.Journal.Records);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    public async Task NativeErrorInSuccessfulHttpResponseNeverKeepsACleanupReservation(string type)
    {
        using var f = new Fixture();
        f.Transport.NativeRejection = true;
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Service.OpenAsync(type, "101", f.Owner, default));
        await f.Service.SweepAsync(default);
        Assert.Empty(f.Journal.Records); Assert.Equal(0, f.Transport.Releases);
        f.Transport.NativeRejection = false;
        var lease = await f.Service.OpenAsync(type, "101", f.Owner, default);
        await f.Service.CloseAsync(lease.LeaseId, f.Owner, default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task LostAcquireAcknowledgementRequiresExplicitAdministratorBeforeCleanup()
    {
        using var f = new Fixture();
        f.Transport.LoseAcquire = true;
        await Assert.ThrowsAsync<IOException>(() => f.Open());
        Assert.Single(f.Journal.Records);
        f.Transport.LoseAcquire = false;
        f.Transport.RejectAcquire = true;
        await f.Service.SweepAsync(default);
        Assert.Equal(0, f.Transport.Releases);
        Assert.Single(f.Journal.Records);
        f.Transport.RejectAcquire = false;
        await f.Service.SweepAsync(default);
        Assert.Equal(1, f.Transport.Acquires); Assert.Equal(0, f.Transport.Releases);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var replacement = await f.Service.OverrideAsync("Task", "101", f.Owner, default);
        await f.Service.CloseAsync(replacement.LeaseId, f.Owner, default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task FailedReleaseIsRetriedAndLogoutClosesEveryEditorInThatSession()
    {
        using var f = new Fixture();
        var task = await f.Open();
        await f.Open("Host");
        f.Transport.FailRelease = true;
        await f.Service.ReleaseSessionAsync(f.Owner.SessionId, default);
        Assert.Equal(2, f.Journal.Records.Count);
        f.Transport.FailRelease = false;
        await f.Service.SweepAsync(default);
        Assert.True(f.Journal.Records.Count == 2);
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        foreach (var type in new[] { "Task", "Host" }) {
            var replacement = await f.Service.OverrideAsync(type, "101", f.Owner, default);
            await f.Service.CloseAsync(replacement.LeaseId, f.Owner, default);
        }
        Assert.Empty(f.Journal.Records);
        // A delayed browser close is idempotent after server cleanup.
        await f.Service.CloseAsync(task.LeaseId, f.Owner, default);
    }

    [Fact]
    public async Task FreshLoginCannotSilentlyTakeOverRecordedCheckout()
    {
        using var f = new Fixture();
        var old = await f.Open();
        var fresh = f.Owner with { SessionId = "new-login" };
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Service.OpenAsync("Task", "101", fresh, default));
        f.Clock.Advance(TimeSpan.FromMinutes(6));
        f.Transport.RejectAcquire = true;
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Service.OpenAsync("Task", "101", fresh, default));
        Assert.Equal(0, f.Transport.Releases);
        f.Transport.RejectAcquire = false;
        f.Owner.Session.Info = new MoveItInfo { IsAdmin = true };
        var replacement = await f.Service.OverrideAsync("Task", "101", fresh, default);
        Assert.NotEqual(old.LeaseId, replacement.LeaseId);
        Assert.Equal(0, f.Transport.Releases);
        await f.Service.CloseAsync(replacement.LeaseId, fresh, default);
        Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task TurningSettingOffDoesNotLoseAnAlreadyHeldLease()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        f.Options.CurrentValue.CheckoutObjectsForDurationOfEdit.Value = false;
        using var operation = await f.Service.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default);
        Assert.NotNull(ObjectEditLeaseService.CheckoutLifetime(operation));
    }

    [Fact]
    public async Task StartupRecoversARecordedCheckoutAndNeverRestoresAnEditableSession()
    {
        using var f = new Fixture();
        var lease = await f.Open();
        Assert.True(f.Journal.Records[lease.LeaseId].Held);
        // Simulate process loss: dispose only the in-memory reservation; retain the durable record.
        f.Journal.Live!.Reservation!.Dispose();
        using var recovery = new ObjectEditLeaseService(f.Transport, f.Journal, f.Options,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), f.Clock, NullLogger<ObjectEditLeaseService>.Instance);
        await recovery.StartAsync(default);
        await recovery.SweepAsync(default);
        Assert.Empty(f.Journal.Records);
        Assert.Equal(1, f.Transport.Releases);
        await Assert.ThrowsAsync<ObjectEditLeaseException>(() => recovery.EnterAsync("Task", "101", lease.LeaseId, f.Owner, default));
        await recovery.StopAsync(default);
    }

    [Fact]
    public async Task RecoveryJournalEncryptsTokensNeverPersistsPasswordsAndSkipsOnlyCorruptRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "maui-lease-journal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var journal = new ObjectEditLeaseJournal(new Environment(root), protection, NullLogger<ObjectEditLeaseJournal>.Instance);
            var record = new ObjectEditLeaseRecord { LeaseId = Guid.NewGuid().ToString("N"), Server = "https://moveit.test", Username = "editor", Type = "Task", Id = "101", Held = true };
            journal.ProtectSession(record, new MoveItSessionState { WebAdminUrl = record.Server, Username = record.Username,
                AccessToken = "secret-access-token", RefreshToken = "secret-refresh-token", Password = "never-persist-this-password", CleanupOwnerSessionId = "private-session-id" });
            await journal.WriteAsync(record, default);
            var directory = Path.Combine(root, "Data", "ObjectEditLeases");
            var json = await File.ReadAllTextAsync(Path.Combine(directory, record.LeaseId + ".json"));
            foreach (var secret in new[] { "secret-access-token", "secret-refresh-token", "never-persist-this-password", "private-session-id" }) Assert.DoesNotContain(secret, json);
            var restored = journal.ReadSession(record);
            Assert.Equal("secret-access-token", restored.AccessToken);
            Assert.Equal("secret-refresh-token", restored.RefreshToken);
            Assert.Equal("private-session-id", restored.CleanupOwnerSessionId);
            Assert.Empty(restored.Password);
            await File.WriteAllTextAsync(Path.Combine(directory, "invalid.json"), "{broken");
            Assert.Single(journal.Load());
            record.Server = "https://different.test";
            Assert.Throws<InvalidOperationException>(() => journal.ReadSession(record));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Environment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MAUI.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable _server;
        public Clock Clock { get; } = new();
        public Transport Transport { get; } = new();
        public Journal Journal { get; } = new();
        public Options Options { get; }
        public ObjectEditLeaseOwner Owner { get; }
        public ObjectEditLeaseService Service { get; }
        public Fixture(bool enabled = true)
        {
            Options = new(enabled);
            Owner = new("browser-session", new MoveItSessionState { WebAdminUrl = "https://" + Guid.NewGuid().ToString("N"), Username = "editor", LastUserActivityUtc = Clock.GetUtcNow(), AccessToken = "access" });
            _server = ObjectCheckout.ForServer(Owner.Session.WebAdminUrl);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MoveIt:IdleSessionTimeoutMinutes:Value"] = "30" }).Build();
            Service = new(Transport, Journal, Options, ConfigurationStartupState.Capture(configuration), Clock, NullLogger<ObjectEditLeaseService>.Instance);
        }
        public Task<ObjectEditLeaseResult> Open(string type = "Task", string id = "101") => Service.OpenAsync(type, id, Owner, default);
        public void Dispose() { Journal.Live?.Reservation?.Dispose(); Service.Dispose(); _server.Dispose(); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
    private sealed class Options(bool enabled) : IOptionsMonitor<UiOptions>
    {
        public UiOptions CurrentValue { get; } = new() { CheckoutObjectsForDurationOfEdit = new() { Value = enabled } };
        public UiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<UiOptions, string?> listener) => null;
    }
    private sealed class Transport : IObjectEditLeaseTransport
    {
        private readonly HashSet<(string Type, string Id)> _checkouts = new();
        public void ClearCheckout(string type, string id) => _checkouts.Remove((type, id));
        public int Acquires, Releases, Revisions, ForcedAcquires;
        public bool RejectAcquire, LoseAcquire, FailRelease, ChangeRevision, NativeRejection;
        public string? RevisionXml;
        public Task AcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        {
            Acquires++;
            if (NativeRejection) throw new ObjectCheckoutRejectedException("Native checkout rejected.");
            if (RejectAcquire || !_checkouts.Add((type, id)))
                throw new ObjectCheckoutRejectedException("This section has been checked out to native-owner since 2026-10-24 17:27:53");
            if (LoseAcquire) throw new IOException("Acknowledgement lost");
            return Task.CompletedTask;
        }
        public Task ReleaseAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        { Releases++; if (FailRelease) throw new IOException("Offline"); _checkouts.Remove((type, id)); return Task.CompletedTask; }
        public Task ForceAcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        {
            if (session.Info?.IsAdmin != true) throw new InvalidOperationException("Only a MOVEit administrator can override checkout.");
            Acquires++; ForcedAcquires++;
            if (NativeRejection) throw new ObjectCheckoutRejectedException("Native checkout rejected.");
            _checkouts.Add((type, id));
            if (LoseAcquire) throw new IOException("Acknowledgement lost");
            return Task.CompletedTask;
        }
        public Task<string> RevisionAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
            => Task.FromResult(RevisionXml is not null ? ObjectEditVersion.Create(type, id, ObjectEditVersion.Definition(new System.Text.Json.Nodes.JsonObject(), RevisionXml))
                : ChangeRevision ? (++Revisions).ToString() : "revision");
    }
    private sealed class Journal : IObjectEditLeaseJournal
    {
        public Dictionary<string, ObjectEditLeaseRecord> Records { get; } = new();
        public bool FailWrite;
        public ObjectEditLeaseRecord? Live;
        public IEnumerable<ObjectEditLeaseRecord> Load() => Records.Values.ToArray();
        public Task WriteAsync(ObjectEditLeaseRecord record, CancellationToken ct)
        {
            if (FailWrite) throw new IOException("Recovery journal unavailable");
            Live = record;
            Records[record.LeaseId] = System.Text.Json.JsonSerializer.Deserialize<ObjectEditLeaseRecord>(System.Text.Json.JsonSerializer.Serialize(record))!;
            return Task.CompletedTask;
        }
        public void Delete(string id) => Records.Remove(id);
        public void ProtectSession(ObjectEditLeaseRecord record, MoveItSessionState session) { }
        public MoveItSessionState ReadSession(ObjectEditLeaseRecord record) => new() { Username = record.Username, WebAdminUrl = record.Server };
    }
}
