using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Krypta.Client.ViewModels;

namespace Krypta.Client;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? parameter)
    {
        if (parameter is null)
        {
            return new TextBlock { Text = "ViewModel assente." };
        }

        string? viewName = parameter.GetType().FullName?.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? viewType = viewName is null ? null : parameter.GetType().Assembly.GetType(viewName);

        return viewType is not null && Activator.CreateInstance(viewType) is Control control
            ? control
            : new TextBlock { Text = $"View non trovata per {parameter.GetType().Name}." };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
