using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabLeaseStore(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IEnvironmentVariables? environment = null,
    ILabStateStore? labStateStore = null)
{
    private const int DefaultLeaseTtlSeconds = 3600;
    private const int QueueHeartbeatTtlSeconds = 15;
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILabStateStore _labStateStore = labStateStore ?? LabStateStoreFactory.Create(fileSystem, environment);

    internal DateTimeOffset CurrentTime => _timeProvider.GetUtcNow();

    public async Task<LabLeaseResult> ClaimAsync(string serial, string? owner, int? ttlSec)
    {
        var attempt = await TryClaimAsync(serial, owner, ttlSec).ConfigureAwait(false);
        return attempt.Succeeded
            ? attempt.Lease!
            : throw CreateClaimFailure(attempt);
    }

    internal async Task<LabLeaseClaimAttempt> TryClaimAsync(string serial, string? owner, int? ttlSec, string? queueId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var claimedAt = _timeProvider.GetUtcNow();
        var ttl = ValidateTtl(ttlSec, "lab claim --ttl-sec");
        var activeQueue = ReadActiveQueue(serial);
        var orderedQueue = activeQueue
            .OrderBy(static entry => entry.RequestedAt)
            .ThenBy(static entry => entry.QueueId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var queuedClaim = default(LabQueueEntryResult);

        if (!string.IsNullOrWhiteSpace(queueId))
        {
            var queuePosition = Array.FindIndex(orderedQueue, entry => string.Equals(entry.QueueId, queueId, StringComparison.OrdinalIgnoreCase));
            if (queuePosition < 0)
            {
                throw new InvalidOperationException($"Queued claim '{queueId}' for '{serial}' is no longer active.");
            }

            if (queuePosition > 0)
            {
                return new LabLeaseClaimAttempt(
                    false,
                    null,
                    null,
                    orderedQueue.Length,
                    queuePosition + 1,
                    null,
                    "queued",
                    $"Device '{serial}' still has {queuePosition} queued claim(s) ahead.");
            }

            queuedClaim = orderedQueue[queuePosition];
        }
        else if (orderedQueue.Length > 0)
        {
            return new LabLeaseClaimAttempt(
                false,
                null,
                null,
                orderedQueue.Length,
                orderedQueue.Length + 1,
                null,
                "queued",
                $"Device '{serial}' already has {orderedQueue.Length} queued claim(s) waiting.");
        }

        var leasePath = GetLeasePath(serial);
        if (_fileSystem.FileExists(leasePath))
        {
            var existingLease = TryReadLease(leasePath);
            if (existingLease is not null)
            {
                if (existingLease.ExpiresAt > claimedAt)
                {
                    return new LabLeaseClaimAttempt(
                        false,
                        null,
                        existingLease,
                        orderedQueue.Length,
                        string.IsNullOrWhiteSpace(queueId) ? 0 : 1,
                        existingLease.ExpiresAt,
                        "leased",
                        CreateActiveLeaseConflict(existingLease).Message);
                }

                _fileSystem.DeleteFile(leasePath);
            }
        }

        var result = new LabLeaseResult(
            "lease:" + ShortHash(serial + "|" + claimedAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            serial,
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner,
            claimedAt,
            claimedAt.AddSeconds(ttl),
            leasePath,
            claimedAt);

        _fileSystem.CreateDirectory(GetLeaseRoot());

        try
        {
            await using var stream = _fileSystem.OpenWrite(result.LeaseFile, overwrite: false);
            await JsonSerializer.SerializeAsync(stream, result, AppJson.Options).ConfigureAwait(false);
        }
        catch (IOException) when (_fileSystem.FileExists(result.LeaseFile))
        {
            var concurrentLease = TryReadLease(result.LeaseFile);
            if (concurrentLease is not null && concurrentLease.ExpiresAt > _timeProvider.GetUtcNow())
            {
                return new LabLeaseClaimAttempt(
                    false,
                    null,
                    concurrentLease,
                    orderedQueue.Length,
                    string.IsNullOrWhiteSpace(queueId) ? 0 : 1,
                    concurrentLease.ExpiresAt,
                    "leased",
                    CreateActiveLeaseConflict(concurrentLease).Message);
            }

            throw new Errors.UsageException($"lab claim found an unreadable lease file for '{serial}' at '{result.LeaseFile}'. Release it manually or remove the file before retrying.");
        }

        if (queuedClaim is not null && _fileSystem.FileExists(queuedClaim.QueueFile))
        {
            _fileSystem.DeleteFile(queuedClaim.QueueFile);
        }

        return new LabLeaseClaimAttempt(true, result, null, orderedQueue.Length, string.IsNullOrWhiteSpace(queueId) ? 0 : 1, result.ExpiresAt, "claimed", "Device claimed.");
    }

    public Task<LabLeaseReleaseResult> ReleaseAsync(string leaseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        var lease = ReadActiveLeases().FirstOrDefault(lease => string.Equals(lease.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase));
        if (lease is null)
        {
            return Task.FromResult(new LabLeaseReleaseResult(leaseId, false, null));
        }

        _fileSystem.DeleteFile(lease.LeaseFile);
        return Task.FromResult(new LabLeaseReleaseResult(lease.LeaseId, true, lease.LeaseFile, lease.Serial));
    }

    public Task<LabLeaseReleaseResult> ReleaseSerialAsync(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var lease = ReadActiveLeases().FirstOrDefault(lease => string.Equals(lease.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (lease is null)
        {
            return Task.FromResult(new LabLeaseReleaseResult(string.Empty, false, null, serial));
        }

        _fileSystem.DeleteFile(lease.LeaseFile);
        return Task.FromResult(new LabLeaseReleaseResult(lease.LeaseId, true, lease.LeaseFile, lease.Serial));
    }

    public async Task<LabLeaseExtendResult> ExtendAsync(string leaseId, int? ttlSec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        var lease = ReadActiveLeases().FirstOrDefault(candidate => string.Equals(candidate.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase));
        return lease is null
            ? new LabLeaseExtendResult(leaseId, string.Empty, false, null, null, null)
            : await ExtendLeaseAsync(lease, ttlSec).ConfigureAwait(false);
    }

    public async Task<LabLeaseExtendResult> ExtendSerialAsync(string serial, int? ttlSec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var lease = ReadActiveLeases().FirstOrDefault(candidate => string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        return lease is null
            ? new LabLeaseExtendResult(string.Empty, serial, false, null, null, null)
            : await ExtendLeaseAsync(lease, ttlSec).ConfigureAwait(false);
    }

    public Task<LabLeasesResult> ListAsync()
    {
        var leases = ReadActiveLeases();
        return Task.FromResult(new LabLeasesResult(leases.Count, leases));
    }

    public async Task<LabQueueEntryResult> EnqueueAsync(string serial, string? owner, int waitSec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (waitSec <= 0)
        {
            throw new Errors.UsageException("Queued claims require --claim-wait-sec greater than zero.");
        }

        var now = _timeProvider.GetUtcNow();
        var entry = new LabQueueEntryResult(
            "queue:" + ShortHash(serial + "|" + now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + Guid.NewGuid().ToString("N")),
            serial,
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner,
            now,
            now,
            now.AddSeconds(QueueHeartbeatTtlSeconds),
            now.AddSeconds(waitSec),
            GetQueuePath(serial, now));

        _fileSystem.CreateDirectory(GetQueueRoot());
        await using var stream = _fileSystem.OpenWrite(entry.QueueFile, overwrite: false);
        await JsonSerializer.SerializeAsync(stream, entry, AppJson.Options).ConfigureAwait(false);
        return entry;
    }

    public async Task<LabQueueEntryResult> HeartbeatQueueAsync(string serial, string queueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueId);

        var queue = ReadActiveQueue(serial).FirstOrDefault(entry => string.Equals(entry.QueueId, queueId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Queued claim '{queueId}' is no longer active.");
        var now = _timeProvider.GetUtcNow();
        var updated = queue with
        {
            LastHeartbeatAt = now,
            HeartbeatExpiresAt = now.AddSeconds(QueueHeartbeatTtlSeconds),
            WaitUntil = queue.WaitUntil
        };

        await using var stream = _fileSystem.OpenWrite(updated.QueueFile, overwrite: true);
        await JsonSerializer.SerializeAsync(stream, updated, AppJson.Options).ConfigureAwait(false);
        return updated;
    }

    public Task ReleaseQueueAsync(string queueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueId);

        var queue = ReadActiveQueue().FirstOrDefault(entry => string.Equals(entry.QueueId, queueId, StringComparison.OrdinalIgnoreCase));
        if (queue is not null && _fileSystem.FileExists(queue.QueueFile))
        {
            _fileSystem.DeleteFile(queue.QueueFile);
        }

        return Task.CompletedTask;
    }

    public Task<LabQueueResult> ListQueueAsync()
    {
        var waiters = ReadActiveQueue();
        return Task.FromResult(new LabQueueResult(waiters.Count, waiters));
    }

    public IReadOnlyDictionary<string, LabLeaseResult> ReadActiveLeasesBySerial() =>
        ReadActiveLeases()
            .GroupBy(static lease => lease.Serial, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(static lease => lease.ExpiresAt).First(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> ReadActiveQueueDepthBySerial() =>
        ReadActiveQueue()
            .GroupBy(static entry => entry.Serial, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

    internal int GetQueueDepth(string serial, string? excludingQueueId = null) =>
        ReadActiveQueue(serial)
            .Count(entry => string.IsNullOrWhiteSpace(excludingQueueId) || !string.Equals(entry.QueueId, excludingQueueId, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<LabLeaseResult> ReadActiveLeases()
    {
        var now = _timeProvider.GetUtcNow();
        var root = GetLeaseRoot();
        if (!_fileSystem.DirectoryExists(root))
        {
            return [];
        }

        var leases = new List<LabLeaseResult>();
        foreach (var file in _fileSystem.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = _fileSystem.OpenRead(file);
                var lease = JsonSerializer.Deserialize<LabLeaseResult>(stream, AppJson.Options);
                if (lease is null)
                {
                    continue;
                }

                if (lease.ExpiresAt <= now)
                {
                    _fileSystem.DeleteFile(file);
                    continue;
                }

                leases.Add(lease);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                if (ex is JsonException)
                {
                    TryDeleteFile(file);
                }
            }
        }

        return leases
            .OrderBy(static lease => lease.ExpiresAt)
            .ThenBy(static lease => lease.Serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<LabQueueEntryResult> ReadActiveQueue(string? serial = null)
    {
        var now = _timeProvider.GetUtcNow();
        var root = GetQueueRoot();
        if (!_fileSystem.DirectoryExists(root))
        {
            return [];
        }

        var waiters = new List<LabQueueEntryResult>();
        var searchPattern = string.IsNullOrWhiteSpace(serial)
            ? "*.json"
            : $"{Slugify(serial)}-*.json";

        foreach (var file in _fileSystem.GetFiles(root, searchPattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = _fileSystem.OpenRead(file);
                var queue = JsonSerializer.Deserialize<LabQueueEntryResult>(stream, AppJson.Options);
                if (queue is null)
                {
                    continue;
                }

                if (queue.WaitUntil <= now || queue.HeartbeatExpiresAt <= now)
                {
                    _fileSystem.DeleteFile(file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(serial) || string.Equals(queue.Serial, serial, StringComparison.OrdinalIgnoreCase))
                {
                    waiters.Add(queue);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                if (ex is JsonException)
                {
                    TryDeleteFile(file);
                }
            }
        }

        return waiters
            .OrderBy(static queue => queue.RequestedAt)
            .ThenBy(static queue => queue.QueueId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string GetLeasePath(string serial) =>
        Path.Join(GetLeaseRoot(), Slugify(serial) + ".json");

    private string GetQueuePath(string serial, DateTimeOffset requestedAt) =>
        Path.Join(GetQueueRoot(), $"{Slugify(serial)}-{requestedAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)}-{ShortHash(Guid.NewGuid().ToString("N"))}.json");

    private string GetLeaseRoot() =>
        _labStateStore.GetCollectionRoot("leases");

    private string GetQueueRoot() =>
        _labStateStore.GetCollectionRoot("queue");

    private async Task<LabLeaseExtendResult> ExtendLeaseAsync(LabLeaseResult lease, int? ttlSec)
    {
        var ttl = ValidateTtl(ttlSec, "lab extend --ttl-sec");
        var previousExpiresAt = lease.ExpiresAt;
        var now = _timeProvider.GetUtcNow();
        var updated = lease with
        {
            ExpiresAt = now.AddSeconds(ttl),
            LastHeartbeatAt = now
        };

        await using var stream = _fileSystem.OpenWrite(updated.LeaseFile, overwrite: true);
        await JsonSerializer.SerializeAsync(stream, updated, AppJson.Options).ConfigureAwait(false);
        return new LabLeaseExtendResult(updated.LeaseId, updated.Serial, true, previousExpiresAt, updated.ExpiresAt, updated.LeaseFile);
    }

    private LabLeaseResult? TryReadLease(string path)
    {
        try
        {
            using var stream = _fileSystem.OpenRead(path);
            return JsonSerializer.Deserialize<LabLeaseResult>(stream, AppJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private Errors.UsageException CreateClaimFailure(LabLeaseClaimAttempt attempt) =>
        attempt.Status switch
        {
            "leased" when attempt.BlockingLease is not null =>
                CreateActiveLeaseConflict(attempt.BlockingLease, attempt.QueueDepth),
            "queued" => new Errors.UsageException(attempt.Message),
            _ => new Errors.UsageException("Device claim failed.")
        };

    private Errors.UsageException CreateActiveLeaseConflict(LabLeaseResult lease, int queueDepth = 0)
    {
        var queueDetail = queueDepth > 0
            ? $" Queue depth is {queueDepth}; run `luotsi lab queue` to inspect pending claim waiters."
            : string.Empty;
        return new Errors.UsageException($"Device '{lease.Serial}' is already leased by {lease.Owner} until {lease.ExpiresAt:O} (lease {lease.LeaseId}). Release it first or wait for expiry.{queueDetail}");
    }

    private static int ValidateTtl(int? ttlSec, string optionDescription)
    {
        var ttl = ttlSec.GetValueOrDefault(DefaultLeaseTtlSeconds);
        if (ttl <= 0)
        {
            throw new Errors.UsageException($"{optionDescription} must be greater than zero.");
        }

        return ttl;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
        {
            _ = cleanupEx;
        }
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }
}

internal sealed record LabLeaseClaimAttempt(
    bool Succeeded,
    LabLeaseResult? Lease,
    LabLeaseResult? BlockingLease,
    int QueueDepth,
    int QueuePosition,
    DateTimeOffset? NextCapacityAt,
    string Status,
    string Message);
