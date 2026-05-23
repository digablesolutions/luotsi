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

        var result = new LabLeaseResult(
            "lease:" + ShortHash(serial + "|" + claimedAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            serial,
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner,
            claimedAt,
            claimedAt.AddSeconds(ttl),
            GetLeasePath(serial));
        await _fileSystem.WriteAllTextAsync(result.LeaseFile, JsonSerializer.Serialize(result, AppJson.Options), Encoding.UTF8).ConfigureAwait(false);
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
        return Task.FromResult(new LabLeaseReleaseResult(leaseId, true, lease.LeaseFile));
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
            }
        }

        return leases.OrderBy(static lease => lease.ExpiresAt).ThenBy(static lease => lease.Serial, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string GetLeasePath(string serial) =>
        Path.Join(GetLeaseRoot(), Slugify(serial) + ".json");

    private string GetLeaseRoot() =>
        Path.Join(_fileSystem.GetTempPath(), "luotsi", "lab-leases");

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
