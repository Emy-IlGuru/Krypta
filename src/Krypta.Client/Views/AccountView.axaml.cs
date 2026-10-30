using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Krypta.Client.Views;

public sealed partial class AccountView : UserControl
{
    public AccountView() => AvaloniaXamlLoader.Load(this);
}
