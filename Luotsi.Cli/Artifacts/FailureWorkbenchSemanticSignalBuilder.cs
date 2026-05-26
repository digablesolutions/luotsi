using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal sealed class FailureWorkbenchSemanticSignalBuilder(string root, IFileSystem fileSystem)
{
    public SemanticSignalsModel? Build()
    {
        var signals = new ReplayGraphSignalReader(root, fileSystem).TryRead();
        return signals is null
            ? null
            : new SemanticSignalsModel(
                ArtifactIndexPaths.EscapeHtmlLink(signals.Path),
                signals.Items
                    .Take(5)
                    .Select(static item => new SemanticSignalItemModel(item.Kind, item.Text, item.Command))
                    .ToArray());
    }
}
