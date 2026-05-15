namespace VisitLab.Cli;

public sealed class TaskDelay(TimeProvider? timeProvider = null) : IDelay
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
        Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), _timeProvider, cancellationToken);
}