using Luotsi.Cli.Errors;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.Cli.View;

internal sealed class ViewProfileCoordinator(IViewProfileStore viewProfileStore)
{
    private readonly IViewProfileStore _viewProfileStore = viewProfileStore ?? throw new ArgumentNullException(nameof(viewProfileStore));

    public async Task ApplyDefaultsAsync(CliOptions options)
    {
        var profileName = options.Get("profile");
        if (string.IsNullOrWhiteSpace(profileName) &&
            (options.HasFlag("last") || string.Equals(options.Command, "reconnect", StringComparison.OrdinalIgnoreCase)))
        {
            profileName = "last";
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        if (!string.Equals(options.Command, "view", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Command, "reconnect", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Command, "doctor", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Command, "view-doctor", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Command, "view-setup", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("--profile is only supported for view, reconnect, doctor, view-doctor, and view-setup.");
        }

        var profile = await _viewProfileStore.LoadAsync(profileName).ConfigureAwait(false)
            ?? throw new UsageException($"View profile '{profileName}' was not found.");
        options.ApplyDefaults(profile.ToOptionDefaults(resetLaunchTuning: options.HasFlag("defaults")));
    }

    public Task SaveIfRequestedAsync(CliOptions options, ViewOptions viewOptions)
    {
        var profileName = options.Get("save-profile");
        return string.IsNullOrWhiteSpace(profileName)
            ? Task.CompletedTask
            : _viewProfileStore.SaveAsync(profileName, ViewProfile.FromResolvedOptions(options, viewOptions));
    }

    public Task SaveConnectedDeviceAsync(string profileName, string deviceSelector, string adbExecutable, string? pollArtifacts) =>
        _viewProfileStore.SaveAsync(profileName, ViewProfile.CreateConnectedDeviceProfile(deviceSelector, adbExecutable, pollArtifacts));

    public Task SaveLastAsync(CliOptions options, ViewOptions viewOptions) =>
        _viewProfileStore.SaveAsync("last", ViewProfile.FromResolvedOptions(options, viewOptions));

    public Task SaveAsync(string profileName, ViewProfile profile) => _viewProfileStore.SaveAsync(profileName, profile);

    public Task<IReadOnlyList<string>> ListAsync() => _viewProfileStore.ListAsync();

    public Task<bool> DeleteAsync(string profileName) => _viewProfileStore.DeleteAsync(profileName);
}
