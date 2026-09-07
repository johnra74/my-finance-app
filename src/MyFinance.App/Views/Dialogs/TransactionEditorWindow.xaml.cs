using System.Windows;
using MyFinance.App.ViewModels.Dialogs;

namespace MyFinance.App.Views.Dialogs;

public partial class TransactionEditorWindow : Window
{
    public TransactionEditorWindow() => InitializeComponent();

    /// <summary>
    /// Recommends a category once the user has finished typing the payee's name.
    /// </summary>
    /// <remarks>
    /// Driven from LostFocus rather than from a property setter: the editable combo pushes
    /// its text on lost focus, so reacting any earlier would look up half a name.
    /// </remarks>
    private void OnPayeeLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionEditorViewModel model)
        {
            model.SuggestCategoryCommand.Execute(null);
        }
    }

    /// <summary>Clears the transfer target, turning the entry back into ordinary spending.</summary>
    private void OnClearTransfer(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionEditorViewModel model)
        {
            model.TransferAccount = null;
        }
    }
}
