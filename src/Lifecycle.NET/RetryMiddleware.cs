namespace Lifecycle;

/// <summary>Retries failed transition hooks with cancellation-aware backoff.</summary>
public sealed class RetryMiddleware : ILifecycleMiddleware
{
    private readonly RetryOptions _options;
    public RetryMiddleware(RetryOptions? options = null)
    {
        _options = options ?? new RetryOptions();
        if (_options.MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be at least one.");
        if (_options.InitialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }
    public async Task InvokeAsync(LifecycleTransitionContext context, LifecycleTransitionDelegate next)
    {
        Exception? finalException = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try { await next(context).ConfigureAwait(false); return; }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (attempt < _options.MaxAttempts)
            {
                finalException = exception;
                var delay = _options.DelayFactory?.Invoke(attempt) ?? TimeSpan.FromTicks(_options.InitialDelay.Ticks * (1L << Math.Min(attempt - 1, 20)));
                await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) { finalException = exception; break; }
        }
        throw finalException ?? new InvalidOperationException("Retry middleware completed without a result.");
    }
}
