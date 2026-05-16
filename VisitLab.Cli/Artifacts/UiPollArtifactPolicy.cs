namespace VisitLab.Cli.Artifacts;

/// <summary>
/// Controls how UI polling commands persist screen-state artifacts while waiting.
/// </summary>
public enum UiPollArtifactPolicy
{
    /// <summary>
    /// Persist only the final successful polling snapshot.
    /// </summary>
    Final,

    /// <summary>
    /// Persist every polling attempt.
    /// </summary>
    PerAttempt,

    /// <summary>
    /// Skip polling snapshots entirely.
    /// </summary>
    None
}