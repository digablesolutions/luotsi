namespace VisitLab.Cli.Infrastructure;

public sealed class TaskDelay(TimeProvider? timeProvider = null) : IDelay
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        var clamped = Math.Max(0, milliseconds);
        DelayMetrics.RecordDelay(clamped);
        return Task.Delay(TimeSpan.FromMilliseconds(clamped), _timeProvider, cancellationToken);
    }
}