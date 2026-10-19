using ReactiveUI;

namespace Krypta.Client.ViewModels;

public sealed class ConnectionViewModel(IScreen hostScreen) : ViewModelBase, IRoutableViewModel
{
    public string UrlPathSegment => "connection";

    public IScreen HostScreen { get; } = hostScreen;
}
