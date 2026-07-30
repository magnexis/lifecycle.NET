using System.Collections.Concurrent;
using Lifecycle;

namespace Lifecycle.Diagnostics;

/// <summary>Observes failed objects and attempts recovery followed by restart without blocking transition events.</summary>
public sealed class LifecycleSupervisor : IDisposable
{
    private readonly LifecycleSupervisorOptions _options;
    private readonly ConcurrentDictionary<ILifecycleObservable, byte> _recovering = new();
    private readonly List<(ILifecycleObservable Lifecycle, EventHandler<LifecycleTransitionEventArgs> Handler)> _subscriptions = [];
    private readonly object _sync = new();
    private bool _disposed;

    public LifecycleSupervisor(LifecycleSupervisorOptions? options = null)
    {
        _options = options ?? new LifecycleSupervisorOptions();
        if (_options.MaxRecoveryAttempts < 1) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.RecoveryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }
    public event EventHandler<LifecycleTransitionEventArgs>? Recovered;
    public event EventHandler<LifecycleTransitionEventArgs>? RecoveryExhausted;
    public void Supervise(ILifecycleObservable lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(lifecycle);
        EventHandler<LifecycleTransitionEventArgs> handler = (_, args) => { if (args.CurrentState == LifecycleState.Failed) _ = RecoverAsync(lifecycle); };
        lifecycle.Failed += handler; lock (_sync) _subscriptions.Add((lifecycle, handler));
    }
    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; foreach (var sub in _subscriptions) sub.Lifecycle.Failed -= sub.Handler; _subscriptions.Clear(); }
    }
    private async Task RecoverAsync(ILifecycleObservable lifecycle)
    {
        if (!_recovering.TryAdd(lifecycle, 0)) return;
        try
        {
            LifecycleTransitionEventArgs? lastFailure = null;
            for (var attempt = 1; attempt <= _options.MaxRecoveryAttempts; attempt++)
            {
                if (attempt > 1) await Task.Delay(_options.RecoveryDelay).ConfigureAwait(false);
                try
                {
                    await lifecycle.RecoverAsync().ConfigureAwait(false);
                    await lifecycle.StartAsync().ConfigureAwait(false);
                    Recovered?.Invoke(this, new(LifecycleOperation.Recover, LifecycleState.Failed, lifecycle.State, TimeSpan.Zero));
                    return;
                }
                catch (LifecycleTransitionException) when (lifecycle.State != LifecycleState.Failed) { return; }
                catch (Exception exception) { lastFailure = new(LifecycleOperation.Recover, LifecycleState.Failed, LifecycleState.Failed, TimeSpan.Zero, exception); }
            }
            if (lastFailure is not null) RecoveryExhausted?.Invoke(this, lastFailure);
        }
        finally { _recovering.TryRemove(lifecycle, out _); }
    }
}
