# Work leases and graceful draining

While a `LifecycleObject` is `Running`, callers can acquire a `LifecycleLease` before handling work. Pause, stop, restart, and disposal transitions first enter their transitional state, reject new leases, then wait for existing leases to be disposed.

```csharp
await using var lease = await worker.AcquireLeaseAsync(cancellationToken);
await ProcessMessageAsync(cancellationToken);
```

Set `LifecycleOptions.DrainTimeout` to control the maximum drain period. The operation cancellation token still takes precedence.
