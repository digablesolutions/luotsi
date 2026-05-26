using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactIndexRenderer
{
    private readonly string _root;
    private readonly IFileSystem _fileSystem;
    private readonly ArtifactIndexModelBuilder _modelBuilder;
    private readonly ArtifactMarkdownIndexRenderer _markdownRenderer = new();
    private readonly ArtifactHtmlIndexRenderer _htmlRenderer = new();

    public ArtifactIndexRenderer(string root, IFileSystem fileSystem)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _modelBuilder = new ArtifactIndexModelBuilder(_root, _fileSystem);
    }

    public static int GetArtifactSortGroup(string path) =>
        ArtifactClassifier.GetSortGroup(path);

    public async Task<string> BuildMarkdownIndexAsync(IReadOnlyList<string> files) =>
        await BuildMarkdownIndexAsync(files, new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files)).ConfigureAwait(false);

    public async Task<string> BuildMarkdownIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var model = await _modelBuilder.BuildAsync(files, replaySummaries).ConfigureAwait(false);
        return _markdownRenderer.Render(model);
    }

    public async Task<string> BuildHtmlIndexAsync(IReadOnlyList<string> files) =>
        await BuildHtmlIndexAsync(files, new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files)).ConfigureAwait(false);

    public async Task<string> BuildHtmlIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var model = await _modelBuilder.BuildAsync(files, replaySummaries).ConfigureAwait(false);
        return _htmlRenderer.Render(model);
    }
}
