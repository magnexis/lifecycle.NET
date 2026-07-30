# Architecture

`LifecycleObject` owns one asynchronous transition gate. A transition selects a permitted source-to-intermediate-to-target state map, runs middleware around exactly one hook invocation, then publishes the new state through a replaceable completion signal. This makes concurrent callers queue rather than race state mutation.

Middleware is ordered in registration order. Each middleware must call its supplied `next` delegate exactly once to continue the pipeline.

`LifecycleGraph` computes topological layers at operation time. Nodes in a layer start concurrently only after every dependency layer has succeeded. It stops layers in reverse order. A failed startup stops all already-running nodes.

The orchestration layer now uses `LifecycleGraphBuilder` to create runtime graphs, each of which produces an immutable execution snapshot. `LifecycleTransaction` executes that snapshot and performs compensating shutdown in reverse dependency order if atomic startup fails. This keeps an operation independent of later graph-declaration changes.
