using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabLeaseClaimCoordinator(LabLeaseStore leaseStore, IDelay delay, TimeProvider timeProvider)
{
    private const int MaxPollDelayMs = 1000;
    private readonly LabLeaseStore _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<LabLeaseResult> ClaimAsync(string serial, string? owner, int ttlSec, int claimWaitSec)
    {
        if (claimWaitSec < 0)
        {
            throw new UsageException("--claim-wait-sec must be zero or greater.");
        }

        if (claimWaitSec == 0)
        {
            return await _leaseStore.ClaimAsync(serial, owner, ttlSec).ConfigureAwait(false);
        }

        var immediateAttempt = await _leaseStore.TryClaimAsync(serial, owner, ttlSec).ConfigureAwait(false);
        if (immediateAttempt.Succeeded)
        {
            return immediateAttempt.Lease!;
        }

        var queueEntry = await _leaseStore.EnqueueAsync(serial, owner, claimWaitSec).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var attempt = await _leaseStore.TryClaimAsync(serial, owner, ttlSec, queueEntry.QueueId).ConfigureAwait(false);
                if (attempt.Succeeded)
                {
                    return attempt.Lease!;
                }

                var now = _timeProvider.GetUtcNow();
                if (queueEntry.WaitUntil <= now)
                {
                    throw CreateWaitTimeout(serial, queueEntry, attempt);
                }

                await _delay.DelayAsync(GetPollDelayMilliseconds(now, queueEntry.WaitUntil)).ConfigureAwait(false);
                now = _timeProvider.GetUtcNow();
                if (queueEntry.WaitUntil <= now)
                {
                    throw CreateWaitTimeout(serial, queueEntry, attempt);
                }

                queueEntry = await _leaseStore.HeartbeatQueueAsync(serial, queueEntry.QueueId).ConfigureAwait(false);
            }
        }
        finally
        {
            await _leaseStore.ReleaseQueueAsync(queueEntry.QueueId).ConfigureAwait(false);
        }
    }

    private static UsageException CreateWaitTimeout(string serial, LabQueueEntryResult queueEntry, LabLeaseClaimAttempt attempt)
    {
        var blockingLease = attempt.BlockingLease is null
            ? string.Empty
            : $" Lease still belongs to {attempt.BlockingLease.Owner} until {attempt.BlockingLease.ExpiresAt:O}.";
        var queueDetail = attempt.QueuePosition > 1
            ? $" {attempt.QueuePosition - 1} queued claim(s) were still ahead."
            : attempt.QueueDepth > 0
                ? $" Queue depth remained {attempt.QueueDepth}."
                : string.Empty;
        return new UsageException($"Timed out after waiting until {queueEntry.WaitUntil:O} to claim device '{serial}'.{blockingLease}{queueDetail}");
    }

    private static int GetPollDelayMilliseconds(DateTimeOffset now, DateTimeOffset waitUntil)
    {
        var remaining = waitUntil - now;
        if (remaining <= TimeSpan.Zero)
        {
            return 1;
        }

        return (int)Math.Max(1, Math.Min(MaxPollDelayMs, remaining.TotalMilliseconds));
    }
}
