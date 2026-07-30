# Contributing to Lifecycle.NET

Thank you for helping improve Lifecycle.NET. The project aims to provide reliable, framework-independent lifecycle infrastructure for .NET, so correctness and predictable behavior matter more than feature count.

## Before opening a pull request

1. Open an issue first for substantial API or architecture changes.
2. Keep pull requests focused on one behavior change.
3. Preserve public API compatibility unless the change is explicitly breaking and documented.
4. Add or update tests for successful, failure, cancellation, and concurrency behavior where applicable.
5. Update documentation whenever user-visible behavior changes.

## Local verification

```powershell
dotnet restore Lifecycle.NET.sln --ignore-failed-sources
dotnet build Lifecycle.NET.sln --configuration Release --no-restore
dotnet run --project tests/Lifecycle.Tests --configuration Release --no-restore
dotnet pack Lifecycle.NET.sln --configuration Release --no-restore --output artifacts/packages
```

## Design expectations

- Keep core packages framework-independent and NativeAOT-conscious.
- Never expose arbitrary code execution or hidden background actions.
- Validate public input and preserve original exceptions.
- Avoid sync-over-async and avoid global mutable runtime state.
- Use clear XML documentation on public APIs.

By contributing, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
