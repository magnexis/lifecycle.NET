namespace Lifecycle;

public sealed class LifecycleBuilder
{
    private readonly List<ILifecycleMiddleware> _middleware = [];
    private LifecycleOptions _options = new();
    private LifecycleBuilder() { }
    public static LifecycleBuilder Create() => new();
    public LifecycleBuilder Use(ILifecycleMiddleware middleware) { ArgumentNullException.ThrowIfNull(middleware); _middleware.Add(middleware); return this; }
    public LifecycleBuilder UseRetry(RetryOptions? options = null) => Use(new RetryMiddleware(options));
    public LifecycleBuilder Configure(LifecycleOptions options) { _options = options ?? throw new ArgumentNullException(nameof(options)); return this; }
    public T Build<T>(Func<LifecycleOptions, IEnumerable<ILifecycleMiddleware>, T> factory) where T : LifecycleObject => factory(_options, _middleware);
}
