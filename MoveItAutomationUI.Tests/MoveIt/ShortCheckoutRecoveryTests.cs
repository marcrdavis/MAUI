using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ShortCheckoutRecoveryTests
{
    [Fact]
    public async Task ANewNativeLockAfterAcquisitionRetiresOurIntentWithoutDeletingItsOwner()
    {
        var journal = new Journal(); using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session()); var releases = 0;
        var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => ObjectCheckout.ExecuteAsync("Host", "42", _ => Task.CompletedTask,
            _ => Task.FromException<bool>(new MoveItApiException("This section has been checked out to alice since 2026-10-24 12:00:00", System.Net.HttpStatusCode.Conflict)),
            _ => { releases++; return Task.CompletedTask; }, default));
        Assert.Contains("alice since", error.Message); Assert.True(releases == 0); Assert.Empty(journal.Records);
    }
    private static MoveItSessionState Session() => new() { WebAdminUrl = "https://moveit.test", Username = "user", AccessToken = "token" };

    [Fact]
    public async Task LostAcquisitionIsJournaledWithoutWritingOrReleasingAnUnverifiedCheckout()
    {
        var journal = new Journal(); using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session());
        var writes = 0; var releases = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectCheckout.ExecuteAsync("Task", "42",
            _ => { Assert.NotEmpty(journal.Records); return Task.FromException(new HttpRequestException("Lost acknowledgement")); },
            _ => { writes++; return Task.FromResult(true); }, _ => { releases++; return Task.CompletedTask; }, default));
        Assert.True(writes == 0 && releases == 0); Assert.True(Assert.Single(journal.Records).Value.PendingRequest);
    }
    [Fact]
    public async Task DefinitiveRejectionDoesNotKeepRecoveryIntentOrReleaseAnotherCheckout()
    {
        var journal = new Journal(); using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session());
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => ObjectCheckout.ExecuteAsync<bool>("Host", "42",
            _ => Task.FromException(new ObjectCheckoutRejectedException("Owned by someone else")),
            _ => throw new Exception("No write"), _ => throw new Exception("No release"), default));
        Assert.Empty(journal.Records);
    }
    [Fact]
    public async Task JournalFailurePreventsCheckoutDispatch()
    {
        var journal = new Journal { FailWrites = true }; using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session()); var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectCheckout.ExecuteAsync("Task", "42",
            _ => { calls++; return Task.CompletedTask; }, _ => Task.FromResult(true), _ => Task.CompletedTask, default));
        Assert.True(calls == 0);
    }
    [Fact]
    public async Task LostReleaseRetainsARecordAndDoesNotRepeatACommittedWrite()
    {
        var journal = new Journal(); using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session()); var writes = 0; var releases = 0;
        await Assert.ThrowsAsync<ObjectCheckoutReleaseException>(() => ObjectCheckout.ExecuteAsync("Script", "42", _ => Task.CompletedTask,
            _ => { writes++; return Task.FromResult(true); }, _ => { releases++; return Task.FromException(new HttpRequestException("Lost release")); }, default));
        await recovery.SweepAsync(default);
        Assert.True(writes == 1 && releases == 1); Assert.True(Assert.Single(journal.Records).Value.PendingRequest);
    }
    [Fact]
    public async Task ConfirmedNativeReleaseRemainsSuccessfulWhenLocalJournalDeletionNeedsRetry()
    {
        var journal = new Journal { FailDeletes = true }; using var recovery = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        using var scope = ObjectCheckout.ForRecovery(recovery, Session());
        Assert.True(await ObjectCheckout.ExecuteAsync("MOVEit Settings", "-1", _ => Task.CompletedTask, _ => Task.FromResult(true), _ => Task.CompletedTask, default));
        Assert.True(Assert.Single(journal.Records).Value.Relinquished);
        journal.FailDeletes = false; await recovery.SweepAsync(default); Assert.Empty(journal.Records);
    }
    [Fact]
    public async Task RestartRetainsUnknownOwnershipUntilAnExplicitAcquisitionIsAcknowledged()
    {
        var journal = new Journal();
        using (var before = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance))
            await before.BeginAsync("Host", "42", Session(), default);
        using var after = new ShortCheckoutRecoveryService(journal, NullLogger<ShortCheckoutRecoveryService>.Instance);
        await after.StartAsync(default);
        try
        {
            await after.SweepAsync(default); Assert.NotEmpty(journal.Records);
            // A different object is not authority to clear the recorded checkout.
            await after.OwnershipConfirmedAsync("Host", "43", Session(), default); Assert.NotEmpty(journal.Records);
            await after.OwnershipConfirmedAsync("Host", "42", Session(), default); Assert.Empty(journal.Records);
        }
        finally { await after.StopAsync(default); }
    }
    private sealed class Journal : IObjectEditLeaseJournal
    {
        public Dictionary<string, ObjectEditLeaseRecord> Records { get; } = new();
        public bool FailWrites, FailDeletes;
        public IEnumerable<ObjectEditLeaseRecord> Load() => Records.Values.ToArray();
        public Task WriteAsync(ObjectEditLeaseRecord record, CancellationToken ct)
        {
            if (FailWrites) throw new IOException("Denied");
            Records[record.LeaseId] = JsonSerializer.Deserialize<ObjectEditLeaseRecord>(JsonSerializer.Serialize(record))!;
            return Task.CompletedTask;
        }
        public void Delete(string id) { if (FailDeletes) throw new IOException("Busy"); Records.Remove(id); }
        public void ProtectSession(ObjectEditLeaseRecord record, MoveItSessionState session) => record.ProtectedSession = "protected tokens";
        public MoveItSessionState ReadSession(ObjectEditLeaseRecord record) => Session();
    }
}
