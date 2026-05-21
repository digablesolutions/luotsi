using Luotsi.Cli.Cli.View;

namespace Luotsi.Cli.Cli.Routing;

internal enum AppCommandFamily
{
    ProfileList,
    ProfileDelete,
    Doctor,
    Inspect,
    ViewDiagnostics,
    ViewSession,
    HostedCommand
}

internal static class AppCommandFamilyClassifier
{
    public static AppCommandFamilyClassification Classify(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.Command, "profile-list", StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandFamilyClassification(AppCommandFamily.ProfileList);
        }

        if (string.Equals(options.Command, "profile-delete", StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandFamilyClassification(AppCommandFamily.ProfileDelete);
        }

        if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandFamilyClassification(AppCommandFamily.Inspect);
        }

        if (string.Equals(options.Command, "doctor", StringComparison.OrdinalIgnoreCase))
        {
            return new AppCommandFamilyClassification(AppCommandFamily.Doctor);
        }

        var viewDiagnostic = ViewDiagnosticInvocation.Resolve(options);
        if (viewDiagnostic is not null)
        {
            return new AppCommandFamilyClassification(AppCommandFamily.ViewDiagnostics, viewDiagnostic);
        }

        if (IsViewSessionCommand(options.Command))
        {
            return new AppCommandFamilyClassification(AppCommandFamily.ViewSession);
        }

        return new AppCommandFamilyClassification(AppCommandFamily.HostedCommand);
    }

    private static bool IsViewSessionCommand(string? command) =>
        string.Equals(command, "view", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "reconnect", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct AppCommandFamilyClassification(
    AppCommandFamily Family,
    ViewDiagnosticInvocation? ViewDiagnostic = null);