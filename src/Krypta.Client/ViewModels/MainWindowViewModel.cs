using System.Reactive.Linq;
using Krypta.Client.Services;
using ReactiveUI;

namespace Krypta.Client.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IScreen
{
    private readonly ObservableAsPropertyHelper<IRoutableViewModel?> _CurrentViewModel;

    public MainWindowViewModel(KryptaConnection connection)
    {
        Connection = connection;

        // `Router.CurrentViewModel` è un IObservable, non una proprietà: senza questo
        // ponte il ContentControl non avrebbe niente a cui agganciarsi. Va costruito
        // prima della prima navigazione, altrimenti il valore iniziale si perde.
        _CurrentViewModel = Router.CurrentViewModel
            .ToProperty(this, viewModel => viewModel.CurrentViewModel);

        Router.Navigate.Execute(new ConnectionViewModel(this)).Subscribe();
    }

    public RoutingState Router { get; } = new();

    public IRoutableViewModel? CurrentViewModel => _CurrentViewModel.Value;

    public KryptaConnection Connection { get; }
}
