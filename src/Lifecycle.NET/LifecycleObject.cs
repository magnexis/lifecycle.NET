using System.Diagnostics;

namespace Lifecycle;

/// <summary>Provides serialized, cancellation-aware lifecycle transitions for derived types.</summary>
public abstract class LifecycleObject : ILifecycleObservable, ILifecycleLeaseProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly ILifecycleMiddleware[] _middleware;
    private readonly LifecycleOptions _options;
    private TaskCompletionSource<LifecycleState> _stateChanged = NewStateSignal();
    private readonly object _leaseSync = new();
    private TaskCompletionSource _leasesDrained = CompletedSignal();
    private int _activeLeases;
    private int _state = (int)LifecycleState.Created;

    protected LifecycleObject(LifecycleOptions? options = null, IEnumerable<ILifecycleMiddleware>? middleware = null)
    {
        _options = options ?? new LifecycleOptions();
        _middleware = middleware?.ToArray() ?? [];
    }

    public LifecycleState State => (LifecycleState)Volatile.Read(ref _state);
    public virtual LifecycleHealth Health => State is LifecycleState.Running or LifecycleState.Paused ? LifecycleHealth.Healthy : State == LifecycleState.Failed ? LifecycleHealth.Unhealthy : LifecycleHealth.Unknown;
    public int ActiveLeaseCount => Volatile.Read(ref _activeLeases);
    public event EventHandler<LifecycleTransitionEventArgs>? Transitioning;
    public event EventHandler<LifecycleTransitionEventArgs>? Transitioned;
    public event EventHandler<LifecycleTransitionEventArgs>? Failed;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Initialize, cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Start, cancellationToken);
    public Task PauseAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Pause, cancellationToken);
    public Task ResumeAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Resume, cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Stop, cancellationToken);
    public Task RestartAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Restart, cancellationToken);
    public Task RecoverAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Recover, cancellationToken);
    public Task DisposeAsync(CancellationToken cancellationToken = default) => ExecuteAsync(LifecycleOperation.Dispose, cancellationToken);
    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync().ConfigureAwait(false);

    public async Task WaitForStateAsync(LifecycleState state, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (State == state) return;
            var signal = Volatile.Read(ref _stateChanged).Task;
            // A state change can occur between the first check and loading its signal.
            // Recheck so we never wait for a later transition after the target was reached.
            if (State == state) return;
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    public Task WaitForRunningAsync(CancellationToken cancellationToken = default) => WaitForStateAsync(LifecycleState.Running, cancellationToken);
    public Task WaitForStoppedAsync(CancellationToken cancellationToken = default) => WaitForStateAsync(LifecycleState.Stopped, cancellationToken);

    /// <summary>Reserves an in-flight work slot while this object is running.</summary>
    public ValueTask<LifecycleLease> AcquireLeaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_leaseSync)
        {
            if (State != LifecycleState.Running) throw new InvalidOperationException("Work leases can only be acquired while the lifecycle object is running.");
            if (_activeLeases++ == 0) _leasesDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return ValueTask.FromResult(new LifecycleLease(ReleaseLease));
        }
    }

    protected virtual Task OnInitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnPauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnRecoverAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnDisposeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ExecuteAsync(LifecycleOperation operation, CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = State;
            var (intermediate, target) = GetTransition(operation, source);
            var stopwatch = Stopwatch.StartNew();
            Transitioning?.Invoke(this, new LifecycleTransitionEventArgs(operation, source, intermediate, TimeSpan.Zero));
            SetState(intermediate);
            using var timeout = CreateTimeout(operation, cancellationToken);
            var effectiveToken = timeout?.Token ?? cancellationToken;
            var context = new LifecycleTransitionContext(this, operation, source, effectiveToken);
            try
            {
                await InvokePipelineAsync(context, () => InvokeHookAsync(operation, effectiveToken)).ConfigureAwait(false);
                SetState(target);
                Transitioned?.Invoke(this, new LifecycleTransitionEventArgs(operation, source, target, stopwatch.Elapsed));
            }
            catch (Exception exception)
            {
                SetState(LifecycleState.Failed);
                Failed?.Invoke(this, new LifecycleTransitionEventArgs(operation, source, LifecycleState.Failed, stopwatch.Elapsed, exception));
                throw;
            }
        }
        finally { _transitionGate.Release(); }
    }

    private Task InvokePipelineAsync(LifecycleTransitionContext context, Func<Task> hook)
    {
        LifecycleTransitionDelegate next = _ => hook();
        for (var i = _middleware.Length - 1; i >= 0; i--)
        {
            var current = _middleware[i]; var following = next;
            next = ctx => current.InvokeAsync(ctx, following);
        }
        return next(context);
    }

    private async Task InvokeHookAsync(LifecycleOperation operation, CancellationToken cancellationToken)
    {
        if (operation is LifecycleOperation.Pause or LifecycleOperation.Stop or LifecycleOperation.Restart or LifecycleOperation.Dispose) await WaitForLeasesAsync(cancellationToken).ConfigureAwait(false);
        switch (operation)
        {
            case LifecycleOperation.Initialize: await OnInitializeAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Start: await OnStartAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Pause: await OnPauseAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Resume: await OnResumeAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Stop: await OnStopAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Restart: await OnStopAsync(cancellationToken).ConfigureAwait(false); await OnStartAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Recover: await OnRecoverAsync(cancellationToken).ConfigureAwait(false); break;
            case LifecycleOperation.Dispose: await OnDisposeAsync(cancellationToken).ConfigureAwait(false); break;
        }
    }
    private CancellationTokenSource? CreateTimeout(LifecycleOperation operation, CancellationToken cancellationToken)
    {
        var duration = operation switch { LifecycleOperation.Initialize => _options.InitializeTimeout, LifecycleOperation.Start or LifecycleOperation.Restart => _options.StartTimeout, LifecycleOperation.Stop or LifecycleOperation.Dispose => _options.StopTimeout, _ => _options.DefaultTimeout };
        duration ??= operation is LifecycleOperation.Pause or LifecycleOperation.Stop or LifecycleOperation.Restart or LifecycleOperation.Dispose ? _options.DrainTimeout : null;
        if (duration is null) return null;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source.CancelAfter(duration.Value); return source;
    }
    private (LifecycleState Intermediate, LifecycleState Target) GetTransition(LifecycleOperation operation, LifecycleState source) => (operation, source) switch
    {
        (LifecycleOperation.Initialize, LifecycleState.Created) => (LifecycleState.Initializing, LifecycleState.Initialized),
        (LifecycleOperation.Start, LifecycleState.Initialized) or (LifecycleOperation.Start, LifecycleState.Stopped) => (LifecycleState.Starting, LifecycleState.Running),
        (LifecycleOperation.Pause, LifecycleState.Running) => (LifecycleState.Pausing, LifecycleState.Paused),
        (LifecycleOperation.Resume, LifecycleState.Paused) => (LifecycleState.Resuming, LifecycleState.Running),
        (LifecycleOperation.Stop, LifecycleState.Running) or (LifecycleOperation.Stop, LifecycleState.Paused) or (LifecycleOperation.Stop, LifecycleState.Failed) => (LifecycleState.Stopping, LifecycleState.Stopped),
        (LifecycleOperation.Restart, LifecycleState.Running) or (LifecycleOperation.Restart, LifecycleState.Paused) => (LifecycleState.Restarting, LifecycleState.Running),
        (LifecycleOperation.Recover, LifecycleState.Failed) => (LifecycleState.Recovering, LifecycleState.Initialized),
        (LifecycleOperation.Dispose, not LifecycleState.Disposed) => (LifecycleState.Stopping, LifecycleState.Disposed),
        _ => throw new LifecycleTransitionException(operation, source)
    };
    private void SetState(LifecycleState next)
    {
        lock (_leaseSync)
        {
            Interlocked.Exchange(ref _state, (int)next);
            var previous = Interlocked.Exchange(ref _stateChanged, NewStateSignal()); previous.TrySetResult(next);
        }
    }
    private async Task WaitForLeasesAsync(CancellationToken cancellationToken)
    {
        Task signal;
        lock (_leaseSync) signal = _leasesDrained.Task;
        await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private void ReleaseLease()
    {
        lock (_leaseSync)
        {
            if (_activeLeases == 0) return;
            if (--_activeLeases == 0) _leasesDrained.TrySetResult();
        }
    }
    private static TaskCompletionSource<LifecycleState> NewStateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSignal() { var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); signal.SetResult(); return signal; }
}
