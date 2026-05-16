using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

internal sealed class SessionViewRenderer(
    IViewRenderer? innerRenderer,
    TimeProvider timeProvider,
    TimeSpan rendererStatsInterval,
    TimeSpan statsEventInterval,
    Func<ViewStats, Task> onStatsAsync) : IViewRenderer
{
    private readonly IViewRenderer? _innerRenderer = innerRenderer;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly TimeSpan _rendererStatsInterval = rendererStatsInterval >= TimeSpan.Zero
        ? rendererStatsInterval
        : throw new ArgumentOutOfRangeException(nameof(rendererStatsInterval));
    private readonly TimeSpan _statsEventInterval = statsEventInterval >= TimeSpan.Zero
        ? statsEventInterval
        : throw new ArgumentOutOfRangeException(nameof(statsEventInterval));
    private readonly Func<ViewStats, Task> _onStatsAsync = onStatsAsync ?? throw new ArgumentNullException(nameof(onStatsAsync));
    private readonly object _rendererStatsGate = new();
    private readonly object _statsGate = new();

    private ViewStats? _pendingRendererStats;
    private ViewStats? _pendingStats;
    private DateTimeOffset? _lastRendererStatsForwardedAt;
    private DateTimeOffset? _lastStatsEmittedAt;

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default) =>
        _innerRenderer?.InitializeAsync(displayInfo, cancellationToken) ?? Task.CompletedTask;

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default) =>
        _innerRenderer?.PresentAsync(frame, cancellationToken) ?? Task.CompletedTask;

    public async Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var now = _timeProvider.GetUtcNow();
        var rendererStatsToForward = CaptureRendererStats(stats, now);
        if (rendererStatsToForward is not null && _innerRenderer is not null)
        {
            await _innerRenderer.UpdateStatsAsync(rendererStatsToForward, cancellationToken).ConfigureAwait(false);
        }

        var statsToEmit = CaptureJsonStats(stats, now);
        if (statsToEmit is not null)
        {
            await _onStatsAsync(statsToEmit).ConfigureAwait(false);
        }
    }

    public Task UpdateChromeAsync(ViewChromeState chrome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        return _innerRenderer?.UpdateChromeAsync(chrome, cancellationToken) ?? Task.CompletedTask;
    }

    public async Task FlushPendingStatsAsync()
    {
        var now = _timeProvider.GetUtcNow();
        var rendererStatsToForward = FlushPendingRendererStats(now);
        if (rendererStatsToForward is not null && _innerRenderer is not null)
        {
            await _innerRenderer.UpdateStatsAsync(rendererStatsToForward).ConfigureAwait(false);
        }

        var statsToEmit = FlushPendingJsonStats(now);
        if (statsToEmit is not null)
        {
            await _onStatsAsync(statsToEmit).ConfigureAwait(false);
        }
    }

    private ViewStats? CaptureRendererStats(ViewStats stats, DateTimeOffset now)
    {
        if (_innerRenderer is null)
        {
            return null;
        }

        if (_rendererStatsInterval == TimeSpan.Zero)
        {
            return stats;
        }

        ViewStats? statsToForward = null;
        lock (_rendererStatsGate)
        {
            _pendingRendererStats = stats;
            if (_lastRendererStatsForwardedAt is null || now - _lastRendererStatsForwardedAt.Value >= _rendererStatsInterval)
            {
                statsToForward = _pendingRendererStats;
                _pendingRendererStats = null;
                _lastRendererStatsForwardedAt = now;
            }
        }

        return statsToForward;
    }

    private ViewStats? CaptureJsonStats(ViewStats stats, DateTimeOffset now)
    {
        if (_statsEventInterval == TimeSpan.Zero)
        {
            return null;
        }

        ViewStats? statsToEmit = null;
        lock (_statsGate)
        {
            _pendingStats = stats;
            if (_lastStatsEmittedAt is null || now - _lastStatsEmittedAt.Value >= _statsEventInterval)
            {
                statsToEmit = _pendingStats;
                _pendingStats = null;
                _lastStatsEmittedAt = now;
            }
        }

        return statsToEmit;
    }

    private ViewStats? FlushPendingRendererStats(DateTimeOffset now)
    {
        if (_innerRenderer is null || _rendererStatsInterval == TimeSpan.Zero)
        {
            return null;
        }

        lock (_rendererStatsGate)
        {
            var statsToForward = _pendingRendererStats;
            if (statsToForward is null)
            {
                return null;
            }

            _pendingRendererStats = null;
            _lastRendererStatsForwardedAt = now;
            return statsToForward;
        }
    }

    private ViewStats? FlushPendingJsonStats(DateTimeOffset now)
    {
        if (_statsEventInterval == TimeSpan.Zero)
        {
            return null;
        }

        lock (_statsGate)
        {
            var statsToEmit = _pendingStats;
            if (statsToEmit is null)
            {
                return null;
            }

            _pendingStats = null;
            _lastStatsEmittedAt = now;
            return statsToEmit;
        }
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) =>
        _innerRenderer?.WaitForCloseAsync(cancellationToken) ?? Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
