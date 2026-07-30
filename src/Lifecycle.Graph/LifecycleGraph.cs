using System.Collections.ObjectModel;
using System.Diagnostics;
using Lifecycle;

namespace Lifecycle.Graph;

/// <summary>A strongly typed graph node identifier.</summary>
public readonly record struct LifecycleNodeId(string Value)
{
    public static LifecycleNodeId Parse(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Node identifiers cannot be empty.", nameof(value)) : new(value);
    public override string ToString() => Value;
}
public enum LifecycleSchedulingMode { Sequential, DependencyParallel, FullyParallelWhenSafe }
public enum LifecycleFailureMode { StopImmediately, ContinueOnFailure }
public enum LifecycleTransactionMode { Atomic, BestEffort, ContinueOnFailure, ManualCommit }
public enum LifecycleGraphValidationCode { DuplicateNode, MissingDependency, CycleDetected, InvalidNodeIdentifier }
public sealed record LifecycleGraphValidationIssue(LifecycleGraphValidationCode Code, string Message, IReadOnlyList<LifecycleNodeId> Nodes);
public sealed class LifecycleGraphValidationResult
{
    internal LifecycleGraphValidationResult(IReadOnlyList<LifecycleGraphValidationIssue> issues) => Issues = issues;
    public IReadOnlyList<LifecycleGraphValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}
public sealed class LifecycleGraphValidationException(LifecycleGraphValidationResult result) : InvalidOperationException("Lifecycle graph validation failed.") { public LifecycleGraphValidationResult Result { get; } = result; }
public sealed class LifecycleExecutionOptions
{
    public int MaximumParallelism { get; init; } = Environment.ProcessorCount;
    public LifecycleSchedulingMode SchedulingMode { get; init; } = LifecycleSchedulingMode.DependencyParallel;
    public LifecycleFailureMode FailureMode { get; init; } = LifecycleFailureMode.StopImmediately;
}
public sealed record LifecycleGraphNodeSnapshot(LifecycleNodeId Id, ILifecycle Lifecycle, IReadOnlyList<LifecycleNodeId> Dependencies);
public sealed class LifecycleGraphSnapshot
{
    internal LifecycleGraphSnapshot(long version, IReadOnlyList<LifecycleGraphNodeSnapshot> nodes, IReadOnlyList<IReadOnlyList<LifecycleNodeId>> waves) => (Version, Nodes, Waves) = (version, nodes, waves);
    public long Version { get; }
    public IReadOnlyList<LifecycleGraphNodeSnapshot> Nodes { get; }
    public IReadOnlyList<IReadOnlyList<LifecycleNodeId>> Waves { get; }
}
public sealed class LifecycleDryRunReport
{
    internal LifecycleDryRunReport(LifecycleGraphSnapshot snapshot, IReadOnlyList<LifecycleNodeId> skipped) => (Snapshot, SkippedNodes) = (snapshot, skipped);
    public LifecycleGraphSnapshot Snapshot { get; }
    public IReadOnlyList<LifecycleNodeId> SkippedNodes { get; }
    public IReadOnlyList<LifecycleNodeId> RollbackOrder => Snapshot.Waves.Reverse().SelectMany(wave => wave.Reverse()).ToArray();
    public string ToText() => $"Lifecycle Dry Run (graph v{Snapshot.Version}){Environment.NewLine}" + string.Join(Environment.NewLine, Snapshot.Waves.Select((wave, index) => $"Wave {index + 1}: {string.Join(", ", wave)}"));
}
public sealed class LifecycleTransactionResult
{
    internal LifecycleTransactionResult(string name, TimeSpan duration, IReadOnlyList<LifecycleNodeId> started, IReadOnlyList<LifecycleNodeId> rolledBack, IReadOnlyList<Exception> rollbackFailures, LifecycleNodeId? failedNode, Exception? failure, bool cancelled)
        => (Name, Duration, StartedNodes, RolledBackNodes, RollbackFailures, FailedNode, Failure, WasCancelled) = (name, duration, started, rolledBack, rollbackFailures, failedNode, failure, cancelled);
    public string Name { get; }
    public TimeSpan Duration { get; }
    public IReadOnlyList<LifecycleNodeId> StartedNodes { get; }
    public IReadOnlyList<LifecycleNodeId> RolledBackNodes { get; }
    public IReadOnlyList<Exception> RollbackFailures { get; }
    public LifecycleNodeId? FailedNode { get; }
    public Exception? Failure { get; }
    public bool WasCancelled { get; }
    public bool Succeeded => Failure is null && !WasCancelled;
    public bool RequiresManualIntervention => RollbackFailures.Count > 0;
}

/// <summary>Builds an immutable lifecycle dependency graph. Builders are not thread-safe.</summary>
public sealed class LifecycleGraphBuilder
{
    private readonly Dictionary<LifecycleNodeId, NodeDefinition> _nodes = [];
    public LifecycleGraphBuilder Add(string id, ILifecycle lifecycle, params string[] dependsOn) => Add(LifecycleNodeId.Parse(id), lifecycle, dependsOn.Select(LifecycleNodeId.Parse));
    public LifecycleGraphBuilder Add(LifecycleNodeId id, ILifecycle lifecycle, params LifecycleNodeId[] dependsOn) => Add(id, lifecycle, (IEnumerable<LifecycleNodeId>)dependsOn);
    public LifecycleGraphBuilder DependsOn(string node, params string[] dependencies)
    {
        var id = LifecycleNodeId.Parse(node); if (!_nodes.TryGetValue(id, out var definition)) throw new KeyNotFoundException($"Node '{id}' is not registered.");
        definition.Dependencies.AddRange(dependencies.Select(LifecycleNodeId.Parse)); return this;
    }
    public LifecycleGraph Build() => new(_nodes.Select(pair => new LifecycleGraphNodeSnapshot(pair.Key, pair.Value.Lifecycle, pair.Value.Dependencies.Distinct().ToArray())));
    private LifecycleGraphBuilder Add(LifecycleNodeId id, ILifecycle lifecycle, IEnumerable<LifecycleNodeId> dependencies)
    {
        ArgumentNullException.ThrowIfNull(lifecycle); if (!_nodes.TryAdd(id, new(lifecycle, dependencies.ToList()))) throw new ArgumentException($"A node named '{id}' is already registered.", nameof(id)); return this;
    }
    private sealed record NodeDefinition(ILifecycle Lifecycle, List<LifecycleNodeId> Dependencies);
}

/// <summary>Coordinates a validated dependency graph. Graph metadata is immutable after construction.</summary>
public sealed class LifecycleGraph
{
    private readonly List<LifecycleGraphNodeSnapshot> _nodes;
    private readonly Dictionary<LifecycleNodeId, LifecycleGraphNodeSnapshot> _byId;
    public LifecycleGraph() : this([]) { }
    internal LifecycleGraph(IEnumerable<LifecycleGraphNodeSnapshot> nodes)
    {
        _nodes = nodes.ToList(); _byId = _nodes.ToDictionary(node => node.Id);
    }
    public long Version => 1;
    [Obsolete("Use LifecycleGraphBuilder for immutable graph construction.")]
    public void Add(string name, ILifecycle lifecycle, params string[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(lifecycle); dependencies ??= [];
        var id = LifecycleNodeId.Parse(name); if (_byId.ContainsKey(id)) throw new ArgumentException($"A node named '{id}' is already registered.", nameof(name));
        var node = new LifecycleGraphNodeSnapshot(id, lifecycle, dependencies.Select(LifecycleNodeId.Parse).ToArray()); _nodes.Add(node); _byId.Add(id, node);
    }
    public LifecycleGraphValidationResult Validate()
    {
        var issues = new List<LifecycleGraphValidationIssue>();
        foreach (var node in _nodes) foreach (var dependency in node.Dependencies) if (!_byId.ContainsKey(dependency)) issues.Add(new(LifecycleGraphValidationCode.MissingDependency, $"Node '{node.Id}' depends on missing node '{dependency}'.", [node.Id, dependency]));
        if (issues.Count == 0 && TryBuildWaves(out _, out var cycle)) issues.Add(new(LifecycleGraphValidationCode.CycleDetected, "Lifecycle dependency graph contains a cycle.", cycle));
        return new(issues);
    }
    public LifecycleGraphSnapshot CreateSnapshot()
    {
        EnsureValid(); TryBuildWaves(out var waves, out _); return new(Version, _nodes, waves.Select(wave => (IReadOnlyList<LifecycleNodeId>)wave.Select(node => node.Id).ToArray()).ToArray());
    }
    public Task<LifecycleDryRunReport> DryRunStartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var snapshot = CreateSnapshot(); var skipped = snapshot.Nodes.Where(node => node.Lifecycle.State == LifecycleState.Running).Select(node => node.Id).ToArray(); return Task.FromResult(new LifecycleDryRunReport(snapshot, skipped));
    }
    public LifecycleTransaction BeginTransaction(string name, LifecycleTransactionMode mode = LifecycleTransactionMode.Atomic, LifecycleExecutionOptions? options = null) => new(this, name, mode, options ?? new());
    public async Task StartAsync(CancellationToken cancellationToken = default) { var result = await BeginTransaction("graph-start").StartAsync(cancellationToken).ConfigureAwait(false); if (!result.Succeeded) throw new InvalidOperationException("Graph startup failed.", result.Failure); }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CreateSnapshot();
        foreach (var wave in snapshot.Waves.Reverse()) foreach (var id in wave.Reverse()) { var lifecycle = _byId[id].Lifecycle; if (lifecycle.State is LifecycleState.Running or LifecycleState.Paused or LifecycleState.Failed) await lifecycle.StopAsync(cancellationToken).ConfigureAwait(false); }
    }
    public LifecycleHealth GetHealth()
    {
        if (_nodes.Count == 0) return LifecycleHealth.Unknown; var states = _nodes.Select(node => node.Lifecycle.Health).ToArray(); if (states.Contains(LifecycleHealth.Unhealthy)) return LifecycleHealth.Unhealthy; if (states.Contains(LifecycleHealth.Degraded)) return LifecycleHealth.Degraded; return states.All(state => state == LifecycleHealth.Healthy) ? LifecycleHealth.Healthy : LifecycleHealth.Unknown;
    }
    public string ExportMermaid() => "graph TD" + Environment.NewLine + string.Join(Environment.NewLine, _nodes.SelectMany(node => node.Dependencies.Select(dependency => $"  {Identifier(dependency.Value)} --> {Identifier(node.Id.Value)}")));
    internal LifecycleGraphNodeSnapshot GetNode(LifecycleNodeId id) => _byId[id];
    private void EnsureValid() { var validation = Validate(); if (!validation.IsValid) throw new LifecycleGraphValidationException(validation); }
    private bool TryBuildWaves(out IReadOnlyList<IReadOnlyList<LifecycleGraphNodeSnapshot>> waves, out IReadOnlyList<LifecycleNodeId> cycle)
    {
        var remaining = _nodes.ToDictionary(node => node.Id, node => new HashSet<LifecycleNodeId>(node.Dependencies)); var result = new List<IReadOnlyList<LifecycleGraphNodeSnapshot>>();
        while (remaining.Count > 0) { var ready = remaining.Where(pair => pair.Value.Count == 0).Select(pair => _byId[pair.Key]).OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToArray(); if (ready.Length == 0) { waves = []; cycle = remaining.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(); return true; } result.Add(ready); foreach (var node in ready) remaining.Remove(node.Id); foreach (var dependencies in remaining.Values) dependencies.ExceptWith(ready.Select(node => node.Id)); }
        waves = result; cycle = []; return false;
    }
    private static string Identifier(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>An orchestration transaction with deterministic compensating shutdown.</summary>
public sealed class LifecycleTransaction : IAsyncDisposable
{
    private readonly LifecycleGraph _graph; private readonly LifecycleExecutionOptions _options; private readonly List<LifecycleNodeId> _started = []; private bool _completed;
    internal LifecycleTransaction(LifecycleGraph graph, string name, LifecycleTransactionMode mode, LifecycleExecutionOptions options) { _graph = graph; Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A transaction name is required.", nameof(name)) : name; Mode = mode; _options = options.MaximumParallelism < 1 ? throw new ArgumentOutOfRangeException(nameof(options)) : options; }
    public string Name { get; } public LifecycleTransactionMode Mode { get; }
    public Task PrepareAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _ = _graph.CreateSnapshot(); return Task.CompletedTask; }
    public Task<LifecycleTransactionResult> CommitAsync(CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public async Task<LifecycleTransactionResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) throw new InvalidOperationException("This transaction has already completed."); _completed = true; var snapshot = _graph.CreateSnapshot(); var watch = Stopwatch.StartNew(); LifecycleNodeId? failed = null; Exception? failure = null;
        try
        {
            var waves = _options.SchedulingMode == LifecycleSchedulingMode.Sequential ? [snapshot.Waves.SelectMany(wave => wave).ToArray()] : snapshot.Waves;
            foreach (var wave in waves)
            {
                using var semaphore = new SemaphoreSlim(_options.MaximumParallelism);
                await Task.WhenAll(wave.Select(async id => { await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false); try { var node = _graph.GetNode(id); if (node.Lifecycle.State == LifecycleState.Created) await node.Lifecycle.InitializeAsync(cancellationToken).ConfigureAwait(false); if (node.Lifecycle.State is LifecycleState.Initialized or LifecycleState.Stopped) await node.Lifecycle.StartAsync(cancellationToken).ConfigureAwait(false); if (node.Lifecycle.State == LifecycleState.Running) lock (_started) _started.Add(id); } catch (Exception ex) { failed ??= id; failure ??= ex; if (_options.FailureMode == LifecycleFailureMode.StopImmediately) throw; } finally { semaphore.Release(); } })).ConfigureAwait(false);
                if (failure is not null && _options.FailureMode == LifecycleFailureMode.StopImmediately) break;
            }
        }
        catch (Exception ex) { failure ??= ex; }
        var cancelled = failure is OperationCanceledException;
        (IReadOnlyList<LifecycleNodeId> Nodes, IReadOnlyList<Exception> Failures) rolledBack = failure is not null && Mode == LifecycleTransactionMode.Atomic
            ? await RollbackAsync().ConfigureAwait(false)
            : ([], []);
        return new(Name, watch.Elapsed, _started.ToArray(), rolledBack.Nodes, rolledBack.Failures, failed, failure, cancelled);
    }
    public async Task<LifecycleTransactionResult> RollbackAsync(CancellationToken cancellationToken = default)
    {
        _completed = true; var watch = Stopwatch.StartNew(); var rollback = await RollbackAsync().ConfigureAwait(false); return new(Name, watch.Elapsed, _started.ToArray(), rollback.Nodes, rollback.Failures, null, null, false);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private async Task<(IReadOnlyList<LifecycleNodeId> Nodes, IReadOnlyList<Exception> Failures)> RollbackAsync()
    {
        var stopped = new List<LifecycleNodeId>(); var failures = new List<Exception>(); var ordered = _graph.CreateSnapshot().Waves.Reverse().SelectMany(wave => wave.Reverse()).Where(id => _started.Contains(id));
        foreach (var id in ordered) try { var lifecycle = _graph.GetNode(id).Lifecycle; if (lifecycle.State is LifecycleState.Running or LifecycleState.Paused or LifecycleState.Failed) await lifecycle.StopAsync(CancellationToken.None).ConfigureAwait(false); stopped.Add(id); } catch (Exception ex) { failures.Add(ex); }
        return (stopped, failures);
    }
}
