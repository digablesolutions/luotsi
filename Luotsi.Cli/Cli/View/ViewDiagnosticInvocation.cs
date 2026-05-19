namespace Luotsi.Cli.Cli.View;

internal enum ViewDiagnosticAction
{
    Doctor,
    Setup
}

internal sealed class ViewDiagnosticInvocation
{
    private ViewDiagnosticInvocation(ViewDiagnosticAction action, string envelopeCommand, bool fix)
    {
        Action = action;
        EnvelopeCommand = envelopeCommand;
        Fix = fix;
    }

    public ViewDiagnosticAction Action { get; }

    public string EnvelopeCommand { get; }

    public bool Fix { get; }

    public static ViewDiagnosticInvocation? Resolve(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.Command, "view-setup", StringComparison.OrdinalIgnoreCase))
        {
            return new ViewDiagnosticInvocation(ViewDiagnosticAction.Setup, "view-setup", fix: !options.HasFlag("dry-run"));
        }

        if (string.Equals(options.Command, "view-doctor", StringComparison.OrdinalIgnoreCase))
        {
            return options.HasFlag("fix")
                ? new ViewDiagnosticInvocation(ViewDiagnosticAction.Setup, "view-doctor", fix: true)
                : new ViewDiagnosticInvocation(ViewDiagnosticAction.Doctor, "view-doctor", fix: false);
        }

        return null;
    }
}