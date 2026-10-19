using System.Collections.Concurrent;
using Krypta.Client.Services;

namespace Krypta.Client.Tests.TestDoubles;

internal sealed class QueuedUiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _Actions = new();

    public int PendingCount => _Actions.Count;

    public void Post(Action action) => _Actions.Enqueue(action);

    public void Drain()
    {
        while (_Actions.TryDequeue(out Action? action))
        {
            action();
        }
    }
}
