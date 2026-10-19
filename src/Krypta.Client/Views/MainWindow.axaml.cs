using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Krypta.Client.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
