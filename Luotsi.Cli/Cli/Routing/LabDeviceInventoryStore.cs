using System.Text;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class LabDeviceInventoryStore(IFileSystem fileSystem, TimeProvider timeProvider, IEnvironmentVariables? environment = null)
{
    private const string Schema = "luotsi-device-inventory.v1";

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables? _environment = environment;

    public async Task<LabInventoryDeviceResult> SetAsync(string serial, string? pool, string? capabilitiesCsv, string? owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var requirements = DeviceAdmissionRequirementsParser.Parse(pool, capabilitiesCsv, "--pool", "--capabilities");
        if (!DeviceAdmissionRequirementsParser.HasRequirements(requirements))
        {
            throw new UsageException("lab inventory set requires --pool and/or --capabilities.");
        }

        var record = new LabDeviceInventoryRecord(
            Schema,
            serial.Trim(),
            requirements?.Pool,
            requirements?.Capabilities ?? [],
            string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner.Trim(),
            _timeProvider.GetUtcNow());

        _fileSystem.CreateDirectory(GetInventoryRoot());
        await _fileSystem.WriteAllTextAsync(
            GetInventoryPath(record.Serial),
            JsonSerializer.Serialize(record, AppJson.Options),
            Encoding.UTF8).ConfigureAwait(false);
        return ToResult(record, null);
    }

    public Task<LabInventoryClearResult> ClearAsync(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var path = GetInventoryPath(serial);
        if (!_fileSystem.FileExists(path))
        {
            return Task.FromResult(new LabInventoryClearResult(serial.Trim(), false, null));
        }

        _fileSystem.DeleteFile(path);
        return Task.FromResult(new LabInventoryClearResult(serial.Trim(), true, path));
    }

    public Task<LabInventoryResult> ListAsync(DeviceInventoryResult? liveInventory = null)
    {
        var liveBySerial = liveInventory?.Devices
            .Where(static device => !string.IsNullOrWhiteSpace(device.Serial))
            .ToDictionary(static device => device.Serial!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        var registered = ReadRecords()
            .ToDictionary(static entry => entry.Serial, StringComparer.OrdinalIgnoreCase);
        var allSerials = registered.Keys
            .Concat(liveBySerial.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static serial => serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var devices = allSerials
            .Select(serial => ToResult(
                registered.GetValueOrDefault(serial),
                liveBySerial.GetValueOrDefault(serial)))
            .ToArray();
        return Task.FromResult(new LabInventoryResult(devices.Length, registered.Count, liveBySerial.Count, devices));
    }

    public LabInventoryDeviceResult? TryGetBySerial(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return ReadBySerial().GetValueOrDefault(serial.Trim());
    }

    public IReadOnlyDictionary<string, LabInventoryDeviceResult> ReadBySerial() =>
        ReadRecords()
            .Select(entry => ToResult(entry, null))
            .ToDictionary(static entry => entry.Serial, StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<LabDeviceInventoryRecord> ReadRecords()
    {
        var root = GetInventoryRoot();
        if (!_fileSystem.DirectoryExists(root))
        {
            return [];
        }

        var entries = new List<LabDeviceInventoryRecord>();
        foreach (var file in _fileSystem.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = _fileSystem.OpenRead(file);
                var record = JsonSerializer.Deserialize<LabDeviceInventoryRecord>(stream, AppJson.Options);
                if (record is null ||
                    !string.Equals(record.Schema, Schema, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(record.Serial))
                {
                    continue;
                }

                entries.Add(record with
                {
                    Serial = record.Serial.Trim(),
                    Pool = string.IsNullOrWhiteSpace(record.Pool) ? null : record.Pool.Trim(),
                    Capabilities = record.Capabilities
                        .Where(static capability => !string.IsNullOrWhiteSpace(capability))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Owner = string.IsNullOrWhiteSpace(record.Owner) ? Environment.UserName : record.Owner.Trim()
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                if (ex is JsonException)
                {
                    TryDeleteFile(file);
                }
            }
        }

        return entries
            .OrderBy(static entry => entry.Serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private LabInventoryDeviceResult ToResult(LabDeviceInventoryRecord? record, DeviceState? liveDevice)
    {
        var serial = record?.Serial ?? liveDevice?.Serial ?? string.Empty;
        var capabilities = BuildCapabilities(liveDevice, record?.Capabilities);
        return new LabInventoryDeviceResult(
            serial,
            record is not null,
            record?.Pool,
            capabilities,
            record?.Owner,
            record?.UpdatedAt,
            record is null ? null : GetInventoryPath(serial),
            liveDevice is not null,
            liveDevice?.Availability,
            liveDevice?.Model);
    }

    private static IReadOnlyList<string> BuildCapabilities(DeviceState? liveDevice, IReadOnlyList<string>? registeredCapabilities)
    {
        var capabilities = new List<string>();
        if (liveDevice is not null)
        {
            if (string.Equals(liveDevice.Availability, "available", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("adb");
            }

            if (!string.IsNullOrWhiteSpace(liveDevice.Transport))
            {
                capabilities.Add(liveDevice.Transport);
            }

            if (!string.IsNullOrWhiteSpace(liveDevice.Type))
            {
                capabilities.Add(liveDevice.Type);
            }

            if (!string.IsNullOrWhiteSpace(liveDevice.Model))
            {
                capabilities.Add($"model:{liveDevice.Model}");
            }
        }

        if (registeredCapabilities is not null)
        {
            capabilities.AddRange(registeredCapabilities);
        }

        return capabilities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string GetInventoryPath(string serial) =>
        Path.Join(GetInventoryRoot(), Slugify(serial.Trim()) + ".json");

    private string GetInventoryRoot() =>
        Path.Join(ArtifactWorkspacePaths.ResolveDefaultWorkspaceRoot(_fileSystem, _environment), "lab", "inventory");

    private void TryDeleteFile(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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

internal sealed record LabDeviceInventoryRecord(
    string Schema,
    string Serial,
    string? Pool,
    IReadOnlyList<string> Capabilities,
    string Owner,
    DateTimeOffset UpdatedAt);
