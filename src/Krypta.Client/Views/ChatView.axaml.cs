using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Krypta.Client.ViewModels;

namespace Krypta.Client.Views;

public sealed partial class ChatView : UserControl
{
    public ChatView() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// `Invio` invia, `Shift+Invio` va a capo (RF-C-18). Sta qui e non in un binding
    /// perché è una questione di tastiera, non di stato.
    /// </summary>
    private void OnDraftKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not Key.Enter || args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        args.Handled = true;
        if (DataContext is ChatViewModel viewModel && viewModel.CanSend)
        {
            viewModel.SendCommand.Execute().Subscribe();
        }
    }
}
