using Avalonia.Threading;

namespace Krypta.Client.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action);
    }
}
