# Generic Host integration

Register lifecycle services through the normal DI extension, then add the host bridge. Lifecycle.NET initializes and starts them in registration order, and stops successfully started services in reverse order during host shutdown.

```csharp
services.AddLifecycle<MyWorker>();
services.AddLifecycleHosting();
```

If startup fails, every service started before the failure is stopped in reverse order. The original exception is preserved for the Generic Host to report.
