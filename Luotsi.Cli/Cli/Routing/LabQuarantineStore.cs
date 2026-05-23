using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabQuarantineStore(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<LabQuarantineResult> QuarantineAsync(string serial, string reason, string? owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var result = new LabQuarantineResult(
            serial,
            reason.Trim(),
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner.Trim(),
            _timeProvider.GetUtcNow(),
            GetQuarantinePath(serial));
        await _fileSystem.WriteAllTextAsync(result.QuarantineFile, JsonSerializer.Serialize(result, AppJson.Options), Encoding.UTF8).ConfigureAwait(false);
        return result;
    }

    public Task<LabQuarantineReleaseResult> ReleaseAsync(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var path = GetQuarantinePath(serial);
        if (!_fileSystem.FileExists(path))
        {
            return Task.FromResult(new LabQuarantineReleaseResult(serial, false, null));
        }

        _fileSystem.DeleteFile(path);
        return Task.FromResult(new LabQuarantineReleaseResult(serial, true, path));
    }

    public Task<LabQuarantinesResult> ListAsync()
    {
        var quarantines = ReadQuarantines();
        return Task.FromResult(new LabQuarantinesResult(quarantines.Count, quarantines));
    }

    public IReadOnlyDictionary<string, LabQuarantineResult> ReadBySerial() =>
        ReadQuarantines().ToDictionary(static item => item.Serial, StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<LabQuarantineResult> ReadQuarantines()
    {
        var root = GetQuarantineRoot();
        if (!_fileSystem.DirectoryExists(root))
        {
            return [];
        }

        var quarantines = new List<LabQuarantineResult>();
        foreach (var file in _fileSystem.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = _fileSystem.OpenRead(file);
                var quarantine = JsonSerializer.Deserialize<LabQuarantineResult>(stream, AppJson.Options);
                if (quarantine is not null)
                {
                    quarantines.Add(quarantine);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Debug.WriteLine($"Failed to read lab quarantine '{file}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return quarantines.OrderBy(static item => item.Serial, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string GetQuarantinePath(string serial) =>
        Path.Join(GetQuarantineRoot(), Slugify(serial) + ".json");

    private string GetQuarantineRoot() =>
        Path.Join(_fileSystem.GetTempPath(), "luotsi", "lab-quarantines");

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
