using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphPredicates
{
    public static bool IsFailureNode(ReplayGraphNodeResult node) =>
        string.Equals(node.Kind, "failure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(GetProperty(node, "status"), "failed", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(GetProperty(node, "error_message")) ||
        string.Equals(GetProperty(node, "failure_relevant"), "true", StringComparison.OrdinalIgnoreCase);

    public static string? GetProperty(ReplayGraphNodeResult node, string name) =>
        node.Properties.TryGetValue(name, out var value) ? value : null;

    public static bool Contains(string? source, string value) =>
        source?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}
