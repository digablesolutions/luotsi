using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Luotsi.Cli.Models;

/// <summary>
/// Process result.
/// </summary>
/// <param name="ExitCode">Exit code.</param>
/// <param name="Stdout">Captured stdout.</param>
/// <param name="Stderr">Captured stderr.</param>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

public sealed record AdbRecoveryActionResult(string Command, int ExitCode, string Stdout, string Stderr);

public sealed record AdbRetryInfo(string Reason, int AttemptCount, IReadOnlyList<AdbRecoveryActionResult> RecoveryActions);

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
    private static readonly CompareInfo TextCompareInfo = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions TextCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;
    private static readonly Encoding? Cp437Encoding = CreateCp437Encoding();

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
        TextEquals(Text, value) ||
        TextEquals(ContentDescription, value) ||
        TextContains(Text, value) ||
        TextContains(ContentDescription, value);

    public bool IsExactMatch(string value) =>
        TextEquals(Text, value) ||
        TextEquals(ContentDescription, value);

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

    public int GetSelectorMatchScore(ScreenElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (!selector.HasCriteria)
        {
            return 0;
        }

        var score = 0;

        if (!string.IsNullOrWhiteSpace(selector.Text))
        {
            var textScore = Math.Max(
                GetTextFieldMatchScore(Text, selector.Text, selector.TextMatch, exactScore: 400, containsScore: 200),
                GetTextFieldMatchScore(ContentDescription, selector.Text, selector.TextMatch, exactScore: 380, containsScore: 180));
            if (textScore == 0)
            {
                return 0;
            }

            score += textScore;
        }

        if (!string.IsNullOrWhiteSpace(selector.ContentDescription))
        {
            var contentDescriptionScore = GetTextFieldMatchScore(ContentDescription, selector.ContentDescription, selector.ContentDescriptionMatch, exactScore: 360, containsScore: 160);
            if (contentDescriptionScore == 0)
            {
                return 0;
            }

            score += contentDescriptionScore;
        }

        if (!string.IsNullOrWhiteSpace(selector.ResourceId))
        {
            var resourceIdScore = GetOrdinalFieldMatchScore(ResourceId, selector.ResourceId, selector.ResourceIdMatch, exactScore: 320, containsScore: 150);
            if (resourceIdScore == 0)
            {
                return 0;
            }

            score += resourceIdScore;
        }

        if (!string.IsNullOrWhiteSpace(selector.ClassName))
        {
            var classNameScore = GetOrdinalFieldMatchScore(ClassName, selector.ClassName, selector.ClassNameMatch, exactScore: 220, containsScore: 100);
            if (classNameScore == 0)
            {
                return 0;
            }

            score += classNameScore;
        }

        if (selector.Region is not null)
        {
            if (!CenterInside(selector.Region))
            {
                return 0;
            }

            score += 25;
        }

        if (Clickable)
        {
            score += 10;
        }

        if (Enabled)
        {
            score += 5;
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

    private static bool TextEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return TextCompareInfo.Compare(NormalizeForComparison(left), NormalizeForComparison(right), TextCompareOptions) == 0;
    }

    private static bool TextContains(string? source, string? value)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TextCompareInfo.IndexOf(NormalizeForComparison(source), NormalizeForComparison(value), TextCompareOptions) >= 0;
    }

    private bool CenterInside(Bounds region) =>
        CenterX >= region.Left &&
        CenterX <= region.Right &&
        CenterY >= region.Top &&
        CenterY <= region.Bottom;

    private static int GetTextFieldMatchScore(string? source, string value, string? matchMode, int exactScore, int containsScore)
    {
        if (TextEquals(source, value))
        {
            return exactScore;
        }

        if (IsContainsMode(matchMode) && TextContains(source, value))
        {
            return containsScore;
        }

        return 0;
    }

    private static int GetOrdinalFieldMatchScore(string? source, string value, string? matchMode, int exactScore, int containsScore)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (string.Equals(source, value, StringComparison.Ordinal))
        {
            return exactScore;
        }

        if (IsContainsMode(matchMode) && source.Contains(value, StringComparison.Ordinal))
        {
            return containsScore;
        }

        return 0;
    }

    private static bool IsContainsMode(string? matchMode) =>
        string.Equals(matchMode, ScreenElementMatchModes.Contains, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeForComparison(string value)
    {
        var repaired = TryRepairCp437Utf8Mojibake(value);
        return repaired.Normalize(NormalizationForm.FormKC);
    }

    private static string TryRepairCp437Utf8Mojibake(string value)
    {
        if (Cp437Encoding is null || !LooksLikeCp437Utf8Mojibake(value))
        {
            return value;
        }

        try
        {
            var bytes = Cp437Encoding.GetBytes(value);
            var repaired = Encoding.UTF8.GetString(bytes);
            return repaired.Contains('\uFFFD', StringComparison.Ordinal) ? value : repaired;
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    private static bool LooksLikeCp437Utf8Mojibake(string value) =>
        value.Any(static character => character is >= '\u2500' and <= '\u259F');

    private static Encoding? CreateCp437Encoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(437);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public static class ScreenElementMatchModes
{
    public const string Exact = "exact";
    public const string Contains = "contains";
}

public sealed record ScreenElementSelector(
    string? Text = null,
    string TextMatch = ScreenElementMatchModes.Contains,
    string? ContentDescription = null,
    string ContentDescriptionMatch = ScreenElementMatchModes.Exact,
    string? ResourceId = null,
    string ResourceIdMatch = ScreenElementMatchModes.Exact,
    string? ClassName = null,
    string ClassNameMatch = ScreenElementMatchModes.Exact,
    Bounds? Region = null,
    bool AllowAmbiguous = false)
{
    [JsonIgnore]
    public bool HasCriteria =>
        !string.IsNullOrWhiteSpace(Text) ||
        !string.IsNullOrWhiteSpace(ContentDescription) ||
        !string.IsNullOrWhiteSpace(ResourceId) ||
        !string.IsNullOrWhiteSpace(ClassName) ||
        Region is not null;

    public string Describe()
    {
        var parts = new List<string>();
        AddPart(parts, "text", Text, TextMatch);
        AddPart(parts, "content_description", ContentDescription, ContentDescriptionMatch);
        AddPart(parts, "resource_id", ResourceId, ResourceIdMatch);
        AddPart(parts, "class_name", ClassName, ClassNameMatch);
        if (Region is not null)
        {
            parts.Add($"region={Region.Left},{Region.Top},{Region.Right},{Region.Bottom}");
        }

        return parts.Count == 0 ? "<empty selector>" : string.Join(", ", parts);
    }

    private static void AddPart(List<string> parts, string name, string? value, string matchMode)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}:{matchMode}={value}");
        }
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
/// <param name="Tags">Optional runner tags used for filtering and reporting.</param>
/// <param name="Setup">Optional setup steps that run before scenario steps.</param>
/// <param name="Teardown">Optional teardown steps that run after setup and scenario steps.</param>
/// <param name="Metadata">Optional scenario context used for diagnostics and device compatibility warnings.</param>
public sealed record ScenarioFile(
    string Name,
    IReadOnlyList<ScenarioStep> Steps,
    IReadOnlyDictionary<string, string>? Variables = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ScenarioStep>? Setup = null,
    IReadOnlyList<ScenarioStep>? Teardown = null,
    ScenarioMetadata? Metadata = null);

public sealed record ScenarioMetadata(
    string? Package = null,
    string? Activity = null,
    string? Notes = null,
    ScenarioDeviceMetadata? Device = null,
    ScenarioLayoutMetadata? Layout = null);

public sealed record ScenarioDeviceMetadata(
    string? Serial = null,
    string? Model = null,
    string? AndroidRelease = null,
    string? Sdk = null);

public sealed record ScenarioLayoutMetadata(
    int? Width = null,
    int? Height = null,
    string? Orientation = null);

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
/// <param name="Activity">Activity/component argument for app lifecycle actions.</param>
/// <param name="Uri">URI argument for startUri.</param>
/// <param name="Permission">Android permission argument for grant/revoke actions.</param>
/// <param name="IntentAction">Intent action override for startUri.</param>
/// <param name="Wait">Whether app/URI starts should wait for launch completion.</param>
/// <param name="ThirdPartyOnly">Whether package listing should include only third-party apps.</param>
/// <param name="ExpectedWidth">Expected screenshot width for visual assertions.</param>
/// <param name="ExpectedHeight">Expected screenshot height for visual assertions.</param>
/// <param name="ExpectedSha256">Expected screenshot SHA-256 for visual assertions.</param>
/// <param name="ExpectedSha256File">Host-local file containing the expected screenshot SHA-256.</param>
/// <param name="BaselineFile">Host-local baseline screenshot path used for full-image visual assertions.</param>
/// <param name="UpdateBaseline">Whether assertScreenshot should write the captured screenshot as the new baseline.</param>
/// <param name="RegionX">Optional screenshot assertion region left coordinate.</param>
/// <param name="RegionY">Optional screenshot assertion region top coordinate.</param>
/// <param name="RegionWidth">Optional screenshot assertion region width.</param>
/// <param name="RegionHeight">Optional screenshot assertion region height.</param>
/// <param name="ExpectedRegionSha256">Expected SHA-256 of normalized RGBA pixels in the screenshot region.</param>
/// <param name="ExpectedRegionSha256File">Host-local file containing the expected region SHA-256.</param>
/// <param name="Selector">Optional structured UI selector for element-backed actions.</param>
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
    bool? ContinueOnError = null,
    string? Activity = null,
    string? Uri = null,
    string? Permission = null,
    string? IntentAction = null,
    bool? Wait = null,
    bool? ThirdPartyOnly = null,
    int? ExpectedWidth = null,
    int? ExpectedHeight = null,
    string? ExpectedSha256 = null,
    string? ExpectedSha256File = null,
    string? BaselineFile = null,
    bool? UpdateBaseline = null,
    int? RegionX = null,
    int? RegionY = null,
    int? RegionWidth = null,
    int? RegionHeight = null,
    string? ExpectedRegionSha256 = null,
    string? ExpectedRegionSha256File = null,
    ScreenElementSelector? Selector = null);

/// <summary>
/// JSON command envelope.
/// </summary>
public sealed record CommandEnvelope(bool Ok, string? Command, DateTimeOffset StartedAt, DateTimeOffset EndedAt, object? Data, ArtifactData Artifacts, BuildProvenance Provenance, ErrorInfo? Error)
{
    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Schema => ResultSchemas.CommandEnvelope;

    /// <summary>
    /// Gets duration in milliseconds.
    /// </summary>
    public long DurationMs => (long)(EndedAt - StartedAt).TotalMilliseconds;
}

/// <summary>
/// Host and build metadata captured with machine-readable outputs.
/// </summary>
public sealed record BuildProvenance(
    string Tool,
    string Version,
    string? CommitSha,
    string? Branch,
    string? Repository,
    string? CiProvider,
    string? CiRunId,
    string Os,
    string Architecture,
    string Framework);

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

        if (message.Contains("adb command timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("adb wait-for-device", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("adb readiness", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("adb device readiness", StringComparison.OrdinalIgnoreCase))
        {
            return "configuration_error";
        }

        if (message.Contains("Android view helper package was not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("LUOTSI_VIEW_HELPER_APK", StringComparison.OrdinalIgnoreCase))
        {
            return "configuration_error";
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
