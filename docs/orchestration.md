# Lifecycle graph orchestration

`LifecycleGraphBuilder` produces a graph whose execution snapshots are immutable. A snapshot records the graph version, nodes, dependencies, and topologically ordered execution waves. Independent nodes in a wave can start concurrently; `MaximumParallelism` bounds that concurrency.

## Validation and dry runs

`Validate()` returns structured issues instead of throwing generic exceptions. `CreateSnapshot()`, `DryRunStartAsync()`, and transaction execution throw `LifecycleGraphValidationException` only when an invalid graph is used operationally.

```csharp
var validation = graph.Validate();
if (!validation.IsValid)
    foreach (var issue in validation.Issues)
        Console.Error.WriteLine(issue.Message);

var report = await graph.DryRunStartAsync();
Console.WriteLine(report.ToText());
```

## Atomic transactions

`BeginTransaction` defaults to `Atomic`. If startup fails, successfully running nodes are stopped in reverse snapshot dependency order. The original startup exception is retained in `LifecycleTransactionResult.Failure`; compensation failures are collected separately in `RollbackFailures` and never overwrite it.

Cancellation is passed to startup operations. Rollback uses a non-cancelable token so the transaction can restore a predictable state after a caller abandons startup.

## Migration from the first graph API

The direct `LifecycleGraph.Add` API remains for source compatibility but is obsolete. Use `LifecycleGraphBuilder` for production use. It separates mutable declaration from an immutable runtime graph and exposes validation before operations run.
