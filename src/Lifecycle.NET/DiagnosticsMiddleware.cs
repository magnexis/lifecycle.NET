using System.Diagnostics;

namespace Lifecycle;

public sealed class TimingMiddleware : ILifecycleMiddleware
{
    public event EventHandler<LifecycleTransitionEventArgs>? Measured;
    public async Task InvokeAsync(LifecycleTransitionContext context, LifecycleTransitionDelegate next)
    {
        var watch = Stopwatch.StartNew();
        try { await next(context).ConfigureAwait(false); Measured?.Invoke(this, new(context.Operation, context.SourceState, context.Lifecycle.State, watch.Elapsed)); }
        catch (Exception ex) { Measured?.Invoke(this, new(context.Operation, context.SourceState, LifecycleState.Failed, watch.Elapsed, ex)); throw; }
    }
}
