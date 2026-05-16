using System.Xml.Linq;

namespace VisitLab.Cli.Models;

/// <summary>
/// Process result.
/// </summary>
/// <param name="ExitCode">Exit code.</param>
/// <param name="Stdout">Captured stdout.</param>
/// <param name="Stderr">Captured stderr.</param>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Device fingerprint metadata.
/// </summary>
public sealed record DeviceFingerprint(
    string Schema,
    DateTimeOffset CapturedAt,
    string Serial,
    string Model,
    string AndroidRelease,
    string Sdk,
    string Fingerprint,
    string Abi,
    string CurrentFocus);

public sealed record FailureCaptureRequest(string Scope, string? Name, string? File, int? StepIndex, string? StepName, string? Action);

public sealed record FailureArtifact(string Kind, string FileName);

public sealed record FailureCaptureError(string Kind, string Message);

public sealed record FailureArtifactBundle(
    string Schema,
    DateTimeOffset CapturedAt,
    string Scope,
    string? Name,
    string? File,
    int? StepIndex,
    string? StepName,
    string? Action,
    string ErrorType,
    string ErrorMessage,
    IReadOnlyList<FailureArtifact> Artifacts,
    IReadOnlyList<FailureCaptureError> CaptureErrors)
{
    public string? MetadataFile { get; init; }
}

/// <summary>
/// Normalized screen state.
/// </summary>
/// <param name="CapturedAt">Capture time.</param>
/// <param name="ElementCount">Element count.</param>
/// <param name="Elements">Elements.</param>
public sealed record ScreenState(DateTimeOffset CapturedAt, int ElementCount, IReadOnlyList<ScreenElement> Elements);

/// <summary>
/// Normalized UI element from uiautomator XML.
/// </summary>
public sealed record ScreenElement(
    string? Text,
    string? ContentDescription,
    string? ResourceId,
    string? ClassName,
    bool Enabled,
    bool Clickable,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    /// <summary>
    /// Gets whether the element is useful for agent reasoning.
    /// </summary>
    public bool IsUseful => !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(ContentDescription) || Clickable;

    /// <summary>
    /// Gets the X center.
    /// </summary>
    public int CenterX => (Left + Right) / 2;

    /// <summary>
    /// Gets the Y center.
    /// </summary>
    public int CenterY => (Top + Bottom) / 2;

    /// <summary>
    /// Gets a stable-ish identifier for debugging.
    /// </summary>
    public string StableId => string.Join("|", new[] { Text, ContentDescription, ResourceId, ClassName }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// Creates an element from a UIAutomator XML node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <returns>Screen element.</returns>
    public static ScreenElement From(XElement node)
    {
        var bounds = ParseBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]");
        return new ScreenElement(
            (string?)node.Attribute("text"),
            (string?)node.Attribute("content-desc"),
            (string?)node.Attribute("resource-id"),
            (string?)node.Attribute("class"),
            bool.TryParse((string?)node.Attribute("enabled"), out var enabled) && enabled,
            bool.TryParse((string?)node.Attribute("clickable"), out var clickable) && clickable,
            bounds.Left,
            bounds.Top,
            bounds.Right,
            bounds.Bottom);
    }

    /// <summary>
    /// Returns whether this element matches text.
    /// </summary>
    /// <param name="value">Text to find.</param>
    /// <returns>True on text or content-desc match.</returns>
    public bool Matches(string value) =>
        string.Equals(Text, value, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ContentDescription, value, StringComparison.OrdinalIgnoreCase) ||
        (Text?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ContentDescription?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);

    public bool IsExactMatch(string value) =>
        string.Equals(Text, value, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ContentDescription, value, StringComparison.OrdinalIgnoreCase);

    public int GetMatchScore(string value)
    {
        var score = IsExactMatch(value)
            ? 300
            : Matches(value)
                ? 200
                : 0;

        if (score == 0)
        {
            return 0;
        }

        if (ClassName?.Contains("EditText", StringComparison.OrdinalIgnoreCase) is true)
        {
            score -= 150;
        }

        if (Clickable)
        {
            score += 25;
        }

        if (!Enabled)
        {
            score -= 25;
        }

        return score;
    }

    private static Bounds ParseBounds(string value)
    {
        var numbers = value.Split(['[', ']', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }
}

/// <summary>
/// Rectangle bounds.
/// </summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge.</param>
public sealed record Bounds(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Scenario playbook file.
/// </summary>
/// <param name="Name">Scenario name.</param>
/// <param name="Steps">Scenario steps.</param>
/// <param name="Variables">Optional scenario variables.</param>
public sealed record ScenarioFile(string Name, IReadOnlyList<ScenarioStep> Steps, IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>
/// Scenario playbook step.
/// </summary>
/// <param name="Name">Optional step name.</param>
/// <param name="Action">Action name.</param>
/// <param name="Text">Text argument.</param>
/// <param name="Code">Keyevent argument.</param>
/// <param name="Step">Semantic step argument.</param>
/// <param name="Label">Optional artifact or tap target label.</param>
/// <param name="Event">Domain event name.</param>
/// <param name="Contains">Event detail substrings.</param>
/// <param name="DetailsPattern">Optional event details regex.</param>
/// <param name="Below">Anchor label for below assertions.</param>
/// <param name="With">Anchor label for alignment assertions.</param>
/// <param name="Package">Optional package override.</param>
/// <param name="TimeoutSec">Timeout in seconds.</param>
/// <param name="Milliseconds">Sleep duration.</param>
/// <param name="X">Absolute X coordinate.</param>
/// <param name="Y">Absolute Y coordinate.</param>
/// <param name="XRatio">Relative X coordinate.</param>
/// <param name="YRatio">Relative Y coordinate.</param>
/// <param name="PostTapDelayMs">Post-tap delay for coordinate taps.</param>
/// <param name="RequireKeyboard">Whether text input requires the keyboard.</param>
/// <param name="HeaderLogo">Whether a double-tap should target the header logo.</param>
/// <param name="MaxGapPx">Maximum vertical gap for below assertions.</param>
/// <param name="MaxDeltaPx">Maximum horizontal delta for alignment assertions.</param>
/// <param name="MaxTopInsetPx">Maximum top inset for version assertions.</param>
/// <param name="MaxRightInsetPx">Maximum right inset for version assertions.</param>
/// <param name="IntervalMs">Interval between double taps or keyed characters.</param>
/// <param name="ObserveFromPreviousStep">Whether an assertEvent step should start observing from the previous step's start time.</param>
/// <param name="ContinueOnError">Whether the scenario should continue after a step failure.</param>
public sealed record ScenarioStep(
    string? Name,
    string Action,
    string? Text,
    string? Code,
    string? Step,
    string? Label = null,
    string? Event = null,
    IReadOnlyList<string>? Contains = null,
    string? DetailsPattern = null,
    string? Below = null,
    string? With = null,
    string? Package = null,
    int? TimeoutSec = null,
    int? Milliseconds = null,
    int? X = null,
    int? Y = null,
    double? XRatio = null,
    double? YRatio = null,
    int? PostTapDelayMs = null,
    bool? RequireKeyboard = null,
    bool? HeaderLogo = null,
    int? MaxGapPx = null,
    int? MaxDeltaPx = null,
    int? MaxTopInsetPx = null,
    int? MaxRightInsetPx = null,
    int? IntervalMs = null,
    bool? ObserveFromPreviousStep = null,
    bool? ContinueOnError = null);

/// <summary>
/// JSON command envelope.
/// </summary>
public sealed record CommandEnvelope(bool Ok, string? Command, DateTimeOffset StartedAt, DateTimeOffset EndedAt, object? Data, ArtifactData Artifacts, ErrorInfo? Error)
{
    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Schema => "visit-lab-command.v1";

    /// <summary>
    /// Gets duration in milliseconds.
    /// </summary>
    public long DurationMs => (long)(EndedAt - StartedAt).TotalMilliseconds;
}

/// <summary>
/// Structured error information.
/// </summary>
public sealed record ErrorInfo(string Type, string Message, string Category)
{
    /// <summary>
    /// Creates error info from an exception.
    /// </summary>
    /// <param name="exception">Exception.</param>
    /// <param name="category">Error category.</param>
    /// <returns>Error info.</returns>
    public static ErrorInfo From(Exception exception, string category) => new(exception.GetType().FullName ?? exception.GetType().Name, exception.Message, category);

    /// <summary>
    /// Classifies an error message.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <returns>Error category.</returns>
    public static string Classify(string message)
    {
        if (message.Contains("must be an integer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not valid JSON", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Missing required option", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unknown command", StringComparison.OrdinalIgnoreCase))
        {
            return "usage_error";
        }

        if (message.Contains("waiting for log", StringComparison.OrdinalIgnoreCase))
        {
            return "log_wait_timeout";
        }

        if (message.Contains("device step", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("device action ready", StringComparison.OrdinalIgnoreCase))
        {
            return "oracle_timeout";
        }

        if (message.Contains("Timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "selector_or_screen_state";
        }

        if (message.Contains("not the foreground app", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("trying to start process", StringComparison.OrdinalIgnoreCase))
        {
            return "configuration_error";
        }

        return "scenario_error";
    }
}

