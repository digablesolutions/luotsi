using System.Threading;

namespace VisitLab.Cli.Infrastructure;

/// <summary>
/// Tracks delay usage within the current async flow for step-level timing attribution.
/// </summary>
public static class DelayMetrics
{
    private static readonly AsyncLocal<DelayMeasurementState?> Current = new();

    /// <summary>
    /// Begins a delay measurement scope for the current async flow.
    /// </summary>
    /// <returns>Disposable scope exposing the accumulated delay in milliseconds.</returns>
    public static DelayMeasurementScope BeginScope()
    {
        var state = new DelayMeasurementState(Current.Value);
        Current.Value = state;
        return new DelayMeasurementScope(state);
    }

    /// <summary>
    /// Records a delay against the current async measurement scope, if any.
    /// </summary>
    /// <param name="milliseconds">Delay duration in milliseconds.</param>
    public static void RecordDelay(int milliseconds)
    {
        var clamped = Math.Max(0, milliseconds);
        for (var state = Current.Value; state is not null; state = state.Parent)
        {
            state.TotalMilliseconds += clamped;
        }
    }

    /// <summary>
    /// Disposable measurement scope for accumulated delay time.
    /// </summary>
    public sealed class DelayMeasurementScope : IDisposable
    {
        private readonly DelayMeasurementState _state;
        private bool _disposed;

        internal DelayMeasurementScope(DelayMeasurementState state) => _state = state;

        /// <summary>
        /// Gets the total delay time recorded within this scope in milliseconds.
        /// </summary>
        public int TotalMilliseconds => _state.TotalMilliseconds;

        /// <inheritdoc />
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

    internal sealed class DelayMeasurementState(DelayMeasurementState? parent)
    {
        public DelayMeasurementState? Parent { get; } = parent;

        public int TotalMilliseconds { get; set; }
    }
}