using System.Text;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabQuarantineStore(IFileSystem fileSystem, TimeProvider timeProvider, IEnvironmentVariables? environment = null)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables? _environment = environment;

    public async Task<LabQuarantineResult> QuarantineAsync(string serial, string reason, string? owner, string source = "manual")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _fileSystem.CreateDirectory(GetQuarantineRoot());
        var result = new LabQuarantineResult(
            serial,
            reason.Trim(),
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner.Trim(),
            _timeProvider.GetUtcNow(),
            GetQuarantinePath(serial),
            source);
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

    public LabQuarantineResult? TryGetBySerial(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return ReadBySerial().GetValueOrDefault(serial);
    }

    public Task<LabQuarantineReleaseResult> ReleaseAutomaticAsync(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var quarantine = TryGetBySerial(serial);
        if (quarantine is null || !string.Equals(quarantine.Source, "automatic", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new LabQuarantineReleaseResult(serial, false, null));
        }

        _fileSystem.DeleteFile(quarantine.QuarantineFile);
        return Task.FromResult(new LabQuarantineReleaseResult(serial, true, quarantine.QuarantineFile));
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
            }
        }

        return quarantines.OrderBy(static item => item.Serial, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string GetQuarantinePath(string serial) =>
        Path.Join(GetQuarantineRoot(), Slugify(serial) + ".json");

    private string GetQuarantineRoot() =>
        Path.Join(ArtifactWorkspacePaths.ResolveDefaultWorkspaceRoot(_fileSystem, _environment), "lab", "quarantines");

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
