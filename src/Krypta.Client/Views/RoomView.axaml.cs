using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Krypta.Client.Views;

public sealed partial class RoomView : UserControl
{
    public RoomView() => AvaloniaXamlLoader.Load(this);
}
