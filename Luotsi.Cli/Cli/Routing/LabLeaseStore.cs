using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabLeaseStore(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private const int DefaultLeaseTtlSeconds = 3600;
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<LabLeaseResult> ClaimAsync(string serial, string? owner, int? ttlSec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var claimedAt = _timeProvider.GetUtcNow();
        var ttl = ttlSec.GetValueOrDefault(DefaultLeaseTtlSeconds);
        if (ttl <= 0)
        {
            throw new Errors.UsageException("lab claim --ttl-sec must be greater than zero.");
        }

        var leasePath = GetLeasePath(serial);
        if (_fileSystem.FileExists(leasePath))
        {
            var existingLease = TryReadLease(leasePath);
            if (existingLease is not null)
            {
                if (existingLease.ExpiresAt > claimedAt)
                {
                    throw CreateActiveLeaseConflict(existingLease);
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
            leasePath);

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
                throw CreateActiveLeaseConflict(concurrentLease);
            }

            throw new Errors.UsageException($"lab claim found an unreadable lease file for '{serial}' at '{result.LeaseFile}'. Release it manually or remove the file before retrying.");
        }

        return result;
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
        return Task.FromResult(new LabLeaseReleaseResult(leaseId, true, lease.LeaseFile, lease.Serial));
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

        var lease = ReadActiveLeases().FirstOrDefault(lease => string.Equals(lease.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase));
        return lease is null
            ? new LabLeaseExtendResult(leaseId, string.Empty, false, null, null, null)
            : await ExtendLeaseAsync(lease, ttlSec).ConfigureAwait(false);
    }

    public async Task<LabLeaseExtendResult> ExtendSerialAsync(string serial, int? ttlSec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var lease = ReadActiveLeases().FirstOrDefault(lease => string.Equals(lease.Serial, serial, StringComparison.OrdinalIgnoreCase));
        return lease is null
            ? new LabLeaseExtendResult(string.Empty, serial, false, null, null, null)
            : await ExtendLeaseAsync(lease, ttlSec).ConfigureAwait(false);
    }

    public Task<LabLeasesResult> ListAsync()
    {
        var leases = ReadActiveLeases();
        return Task.FromResult(new LabLeasesResult(leases.Count, leases));
    }

    public IReadOnlyDictionary<string, LabLeaseResult> ReadActiveLeasesBySerial() =>
        ReadActiveLeases()
            .GroupBy(static lease => lease.Serial, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(static lease => lease.ExpiresAt).First(), StringComparer.OrdinalIgnoreCase);

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
                    try
                    {
                        _fileSystem.DeleteFile(file);
                    }
                    catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                    {
                        // Ignore cleanup failures so a corrupt lease file does not break listing.
                        _ = cleanupEx;
                    }
                }
            }
        }

        return leases.OrderBy(static lease => lease.ExpiresAt).ThenBy(static lease => lease.Serial, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string GetLeasePath(string serial) =>
        Path.Join(GetLeaseRoot(), Slugify(serial) + ".json");

    private string GetLeaseRoot() =>
        Path.Join(_fileSystem.GetTempPath(), "luotsi", "lab-leases");

    private async Task<LabLeaseExtendResult> ExtendLeaseAsync(LabLeaseResult lease, int? ttlSec)
    {
        var ttl = ttlSec.GetValueOrDefault(DefaultLeaseTtlSeconds);
        if (ttl <= 0)
        {
            throw new Errors.UsageException("lab extend --ttl-sec must be greater than zero.");
        }

        var previousExpiresAt = lease.ExpiresAt;
        var updated = lease with { ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(ttl) };
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

    private static Errors.UsageException CreateActiveLeaseConflict(LabLeaseResult lease) =>
        new($"Device '{lease.Serial}' is already leased by {lease.Owner} until {lease.ExpiresAt:O} (lease {lease.LeaseId}). Release it first or wait for expiry.");

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
