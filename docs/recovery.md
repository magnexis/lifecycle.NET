# Recovery and supervision

Override `OnRecoverAsync` to repair resources after a failed lifecycle operation. Calling `RecoverAsync` is valid only from `Failed` and returns the object to `Initialized`; it does not automatically start work.

`LifecycleSupervisor` is an opt-in operational component. It observes failures asynchronously, runs recovery, and starts the object again. It prevents concurrent recovery loops for the same object and limits attempts through `LifecycleSupervisorOptions`.

```csharp
using var supervisor = new Lifecycle.Diagnostics.LifecycleSupervisor();
supervisor.Supervise(worker);
```
