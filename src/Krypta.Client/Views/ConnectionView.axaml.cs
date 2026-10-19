using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Krypta.Client.Views;

public sealed partial class ConnectionView : UserControl
{
    public ConnectionView() => AvaloniaXamlLoader.Load(this);
}
