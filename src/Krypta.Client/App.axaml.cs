using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Krypta.Client.Configuration;
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
            ConfigService configService = new();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(connection, configService),
            };

            _ = ApplyConfiguredThemeAsync(configService);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Il tema è una preferenza del file di configurazione ([06 §3](../../docs/06-configurazione.md))
    /// e finché non esiste il selettore questa è l'unica cosa che lo applica. Senza,
    /// l'anteprima colorata della schermata [2] calcolerebbe il colore su un tema mentre
    /// la finestra ne disegna un altro, e sul fondo sbagliato quel colore non è leggibile.
    /// </summary>
    private async Task ApplyConfiguredThemeAsync(ConfigService configService)
    {
        try
        {
            ConfigLoadResult loaded = await configService.LoadAsync();
            RequestedThemeVariant = loaded.Config.Ui.Theme is ClientTheme.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Config illeggibile: resta il tema dichiarato in App.axaml, e l'avviso lo
            // mostra già la schermata [1].
        }
    }
}
