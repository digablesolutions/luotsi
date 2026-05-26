namespace Luotsi.Cli.Infrastructure.Telemetry;

/// <summary>
/// Tracks ADB retry activity within the current async flow for scenario and command metrics.
/// </summary>
public static class AdbRetryMetrics
{
    private static readonly AsyncLocal<AdbRetryMetricState?> Current = new();

    public static AdbRetryMetricScope BeginScope()
    {
        var state = new AdbRetryMetricState(Current.Value);
        Current.Value = state;
        return new AdbRetryMetricScope(state);
    }

    public static void RecordRetry(string reason, int attemptCount)
    {
        var retryCount = Math.Max(0, attemptCount - 1);
        for (var state = Current.Value; state is not null; state = state.Parent)
        {
            state.CommandRetryCount += retryCount;
            state.CommandWithRetryCount++;
            state.LastRetryReason = reason;
        }
    }

    public sealed class AdbRetryMetricScope : IDisposable
    {
        private readonly AdbRetryMetricState _state;
        private bool _disposed;

        internal AdbRetryMetricScope(AdbRetryMetricState state) => _state = state;

        public int CommandRetryCount => _state.CommandRetryCount;

        public int CommandWithRetryCount => _state.CommandWithRetryCount;

        public string? LastRetryReason => _state.LastRetryReason;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current.Value = _state.Parent;
            _disposed = true;
        }
    }

    internal sealed class AdbRetryMetricState(AdbRetryMetricState? parent)
    {
        public AdbRetryMetricState? Parent { get; } = parent;

        public int CommandRetryCount { get; set; }

        public int CommandWithRetryCount { get; set; }

        public string? LastRetryReason { get; set; }
    }
}

