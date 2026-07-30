namespace Lifecycle;

public enum LifecycleState { Created, Initializing, Initialized, Starting, Running, Pausing, Paused, Resuming, Stopping, Stopped, Restarting, Failed, Recovering, Disposed }
public enum LifecycleOperation { Initialize, Start, Pause, Resume, Stop, Restart, Dispose, Recover }
public enum LifecycleHealth { Unknown, Healthy, Degraded, Unhealthy }

public interface ILifecycle
{
    LifecycleState State { get; }
    LifecycleHealth Health { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
    Task RecoverAsync(CancellationToken cancellationToken = default);
    Task DisposeAsync(CancellationToken cancellationToken = default);
    Task WaitForStateAsync(LifecycleState state, CancellationToken cancellationToken = default);
    Task WaitForRunningAsync(CancellationToken cancellationToken = default);
    Task WaitForStoppedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Exposes lifecycle transition notifications to diagnostics and host integrations.</summary>
public interface ILifecycleObservable : ILifecycle
{
    event EventHandler<LifecycleTransitionEventArgs>? Transitioning;
    event EventHandler<LifecycleTransitionEventArgs>? Transitioned;
    event EventHandler<LifecycleTransitionEventArgs>? Failed;
}

/// <summary>Allows callers to mark in-flight work that must drain before a lifecycle transition completes.</summary>
public interface ILifecycleLeaseProvider
{
    int ActiveLeaseCount { get; }
    ValueTask<LifecycleLease> AcquireLeaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>A single in-flight work reservation. Dispose it exactly once when the work is complete.</summary>
public sealed class LifecycleLease : IDisposable, IAsyncDisposable
{
    private Action? _release;
    public LifecycleLease(Action release) => _release = release ?? throw new ArgumentNullException(nameof(release));
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

public sealed class LifecycleTransitionEventArgs : EventArgs
{
    public LifecycleTransitionEventArgs(LifecycleOperation operation, LifecycleState previousState, LifecycleState currentState, TimeSpan duration, Exception? exception = null)
        => (Operation, PreviousState, CurrentState, Duration, Exception, Timestamp) = (operation, previousState, currentState, duration, exception, DateTimeOffset.UtcNow);
    public LifecycleOperation Operation { get; }
    public LifecycleState PreviousState { get; }
    public LifecycleState CurrentState { get; }
    public TimeSpan Duration { get; }
    public Exception? Exception { get; }
    public DateTimeOffset Timestamp { get; }
}

public interface ILifecycleMiddleware
{
    Task InvokeAsync(LifecycleTransitionContext context, LifecycleTransitionDelegate next);
}
public delegate Task LifecycleTransitionDelegate(LifecycleTransitionContext context);

public sealed class LifecycleTransitionContext
{
    public LifecycleTransitionContext(ILifecycle lifecycle, LifecycleOperation operation, LifecycleState sourceState, CancellationToken cancellationToken)
        => (Lifecycle, Operation, SourceState, CancellationToken) = (lifecycle, operation, sourceState, cancellationToken);
    public ILifecycle Lifecycle { get; }
    public LifecycleOperation Operation { get; }
    public LifecycleState SourceState { get; }
    public CancellationToken CancellationToken { get; }
    public Exception? Exception { get; internal set; }
}

public sealed class LifecycleOptions
{
    public TimeSpan? InitializeTimeout { get; init; }
    public TimeSpan? StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan? StopTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan? DefaultTimeout { get; init; }
    /// <summary>Maximum time a draining transition waits for issued work leases.</summary>
    public TimeSpan? DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class RetryOptions
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    public Func<int, TimeSpan>? DelayFactory { get; init; }
}

public sealed class LifecycleSupervisorOptions
{
    public int MaxRecoveryAttempts { get; init; } = 3;
    public TimeSpan RecoveryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed class LifecycleTransitionException : InvalidOperationException
{
    public LifecycleTransitionException(LifecycleOperation operation, LifecycleState currentState) : base($"Cannot {operation.ToString().ToLowerInvariant()} a lifecycle object while it is {currentState}.") => (Operation, CurrentState) = (operation, currentState);
    public LifecycleOperation Operation { get; }
    public LifecycleState CurrentState { get; }
}
