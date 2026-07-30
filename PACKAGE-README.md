# Lifecycle.NET

Lifecycle.NET is an async-first lifecycle framework for .NET components, services, workers, plugins, and application subsystems.

It provides validated lifecycle transitions, cancellation-aware hooks, retries, recovery supervision, graceful work draining, diagnostics, dependency graphs, transactional startup, and Generic Host integration.

## Quick start

```csharp
public sealed class Worker : LifecycleObject
{
    protected override Task OnStartAsync(CancellationToken cancellationToken)
        => StartWorkerAsync(cancellationToken);
}

var worker = new Worker();
await worker.InitializeAsync();
await worker.StartAsync();
```

See the repository README and documentation for graph orchestration, transactions, recovery, and hosting integration.
