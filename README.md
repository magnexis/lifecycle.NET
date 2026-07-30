# Lifecycle.NET

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Status: Alpha](https://img.shields.io/badge/status-alpha-1565c0)](#roadmap)
[![GitHub Pages](https://img.shields.io/badge/docs-GitHub%20Pages-222?logo=github)](docs/site)

<p align="center"><img src="assets/lifecycle-net-logo.png" alt="Lifecycle.NET logo" width="640" /></p>

Lifecycle.NET is an async-first, framework-independent lifecycle foundation for .NET services and components. It standardizes initialization, startup, pausing, stopping, restart, disposal, and dependency-aware orchestration.

## Included today

- Thread-safe `LifecycleObject` base class with validated states and serialized transitions.
- Cancellation tokens and configurable startup/shutdown timeouts.
- Transition events, `WaitForStateAsync`, health signals, and middleware.
- Dependency graph orchestration with structured validation, immutable execution snapshots, dry runs, bounded parallel waves, atomic rollback, reverse shutdown, and Mermaid export.
- Microsoft.Extensions.DependencyInjection registration extensions.
- .NET Generic Host integration with deterministic startup and reverse-order shutdown.
- Bounded diagnostics history, aggregate metrics, and retry middleware for transient transition failures.
- A supervisor that can observe failed objects, invoke recovery, and safely restart them.
- Work leases and drain barriers for graceful pauses, restarts, shutdowns, and disposal.
- A self-contained executable test suite requiring no third-party test runner.

## Quick start

```csharp
using Lifecycle;

public sealed class Worker : LifecycleObject
{
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        // Start resources here.
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        // Release active work here.
        return Task.CompletedTask;
    }
}

var worker = new Worker();
await worker.InitializeAsync();
await worker.StartAsync();
await worker.WaitForRunningAsync();
await worker.StopAsync();
```

## State model

`Created → Initializing → Initialized → Starting → Running → Pausing → Paused → Resuming → Running → Stopping → Stopped`

Failures enter `Failed`; `RecoverAsync` transitions through `Recovering` back to `Initialized`; disposal reaches `Disposed`. Invalid operations throw `LifecycleTransitionException` before a hook runs.

## Transactional orchestration

```csharp
var graph = new Lifecycle.Graph.LifecycleGraphBuilder()
    .Add("database", database)
    .Add("cache", cache, "database")
    .Add("api", api, "database", "cache")
    .Build();

var dryRun = await graph.DryRunStartAsync();
await using var transaction = graph.BeginTransaction("application-startup");
var result = await transaction.StartAsync();

if (!result.Succeeded)
    throw result.Failure!;
```

## Build and test

```powershell
dotnet build Lifecycle.NET.sln --configuration Release
dotnet run --project tests/Lifecycle.Tests -c Release
```

## Create NuGet packages

Lifecycle.NET uses SDK-style project metadata rather than separate `.nuspec` files, keeping package metadata aligned with each library project. To create local prerelease packages and symbol packages:

```powershell
dotnet pack Lifecycle.NET.sln --configuration Release --output ./artifacts/packages
```

Packages include the official logo and package README. Publishing uses [NuGet Trusted Publishing](docs/releasing.md) through GitHub Actions OIDC; no long-lived API key is stored in this repository.

## Documentation site

The static documentation site lives in [`docs/site`](docs/site) and is deployed by [the GitHub Pages workflow](.github/workflows/pages.yml). Before its first deployment, a repository administrator must enable GitHub Pages and select **Pages → Build and deployment → Source → GitHub Actions**. Subsequent pushes to `main` that change documentation or branding deploy it automatically.

Alternatively, add a `PAGES_ENABLEMENT_TOKEN` repository secret containing a fine-grained token with **Pages: write** and **Administration: write** for this repository. The workflow then enables Pages on its first run. Do not use a broad personal token or expose the token in workflow files.

## Architecture

| Project | Responsibility |
| --- | --- |
| `Lifecycle.Abstractions` | State, contracts, events, middleware contracts, options, exceptions. |
| `Lifecycle.NET` | Lifecycle engine, hook invocation, pipeline, timeout enforcement. |
| `Lifecycle.Graph` | Dependency graph validation and orchestration. |
| `Lifecycle.Diagnostics` | Bounded transition history and aggregate in-process metrics. |
| `Lifecycle.Hosting` | Standard .NET Generic Host bridge. |
| `Lifecycle.Extensions.DependencyInjection` | DI service registration. |

## Roadmap

The next slices are intentionally not placeholders: reusable lifecycle plans, optional/conditional dependencies, policy engine circuit breaking, checkpoints, safe replacement, health/readiness adapters, OpenTelemetry, platform adapters, testing helpers, and the Roslyn source generator will be added with their actual integration tests and package metadata.

## License

MIT. See [LICENSE](LICENSE).
