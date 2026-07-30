using Lifecycle;
using Microsoft.Extensions.Hosting;

namespace Lifecycle.Hosting;

/// <summary>Bridges registered lifecycle instances into the .NET Generic Host lifecycle.</summary>
public sealed class LifecycleHostedService(IEnumerable<ILifecycle> lifecycles) : IHostedService
{
    private readonly ILifecycle[] _lifecycles = lifecycles?.ToArray() ?? throw new ArgumentNullException(nameof(lifecycles));
    private readonly List<ILifecycle> _started = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started.Count != 0) return;
            try
            {
                foreach (var lifecycle in _lifecycles)
                {
                    if (lifecycle.State == LifecycleState.Failed) throw new InvalidOperationException("A failed lifecycle cannot be started by the host. Recover it before host startup or attach a LifecycleSupervisor.");
                    if (lifecycle.State == LifecycleState.Created) await lifecycle.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    if (lifecycle.State is LifecycleState.Initialized or LifecycleState.Stopped) await lifecycle.StartAsync(cancellationToken).ConfigureAwait(false);
                    if (lifecycle.State is LifecycleState.Running or LifecycleState.Paused) _started.Add(lifecycle);
                }
            }
            catch
            {
                await StopStartedAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopStartedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task StopStartedAsync(CancellationToken cancellationToken)
    {
        for (var index = _started.Count - 1; index >= 0; index--)
        {
            var lifecycle = _started[index];
            if (lifecycle.State is LifecycleState.Running or LifecycleState.Paused or LifecycleState.Failed) await lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        _started.Clear();
    }
}
