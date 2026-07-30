using System.Collections.Concurrent;
using Lifecycle;

namespace Lifecycle.Diagnostics;

public sealed record LifecycleTransitionRecord(string Name, LifecycleTransitionEventArgs Transition);
public sealed record LifecycleMetricsSnapshot(long Transitions, long Failures, TimeSpan TotalDuration, IReadOnlyDictionary<LifecycleState, long> StateCounts)
{
    public TimeSpan AverageDuration => Transitions == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(TotalDuration.Ticks / Transitions);
}

/// <summary>Thread-safe bounded transition history and aggregate measurements.</summary>
public sealed class LifecycleDiagnostics : IDisposable
{
    private readonly ConcurrentQueue<LifecycleTransitionRecord> _history = new();
    private readonly ConcurrentDictionary<LifecycleState, long> _states = new();
    private readonly List<(ILifecycleObservable Lifecycle, EventHandler<LifecycleTransitionEventArgs> Completed, EventHandler<LifecycleTransitionEventArgs> Failed)> _subscriptions = [];
    private readonly object _sync = new();
    private readonly int _capacity;
    private long _transitions; private long _failures; private long _durationTicks;
    public LifecycleDiagnostics(int capacity = 1_000) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); _capacity = capacity; }
    public void Track(string name, ILifecycleObservable lifecycle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(lifecycle);
        EventHandler<LifecycleTransitionEventArgs> completed = (_, args) => Record(name, args, false);
        EventHandler<LifecycleTransitionEventArgs> failed = (_, args) => Record(name, args, true);
        lifecycle.Transitioned += completed; lifecycle.Failed += failed;
        lock (_sync) _subscriptions.Add((lifecycle, completed, failed));
    }
    public IReadOnlyList<LifecycleTransitionRecord> GetHistory() => _history.ToArray();
    public LifecycleMetricsSnapshot GetSnapshot() => new(Interlocked.Read(ref _transitions), Interlocked.Read(ref _failures), TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks)), new Dictionary<LifecycleState, long>(_states));
    public void Dispose()
    {
        lock (_sync) { foreach (var sub in _subscriptions) { sub.Lifecycle.Transitioned -= sub.Completed; sub.Lifecycle.Failed -= sub.Failed; } _subscriptions.Clear(); }
    }
    private void Record(string name, LifecycleTransitionEventArgs args, bool failed)
    {
        _history.Enqueue(new(name, args)); while (_history.Count > _capacity) _history.TryDequeue(out _);
        Interlocked.Increment(ref _transitions); Interlocked.Add(ref _durationTicks, args.Duration.Ticks); _states.AddOrUpdate(args.CurrentState, 1, static (_, count) => count + 1);
        if (failed) Interlocked.Increment(ref _failures);
    }
}
