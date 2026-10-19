using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Krypta.Client.Services;
using Krypta.Client.ViewModels;
using Krypta.Client.Views;

namespace Krypta.Client;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            KryptaConnection connection = new();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(connection),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
