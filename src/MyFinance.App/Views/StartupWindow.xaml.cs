using System.Windows;
using MyFinance.App.ViewModels;
using MyFinance.Core.Security;

namespace MyFinance.App.Views;

/// <summary>
/// Welcome, unlock and create-book screens.
/// </summary>
/// <remarks>
/// The password handling is deliberately in code-behind. <see cref="PasswordBox"/> exposes
/// no bindable password property precisely so the secret does not get copied into the
/// binding system, and routing it through an observable string on the view model would
/// throw that protection away. Instead the value is read at the moment of use and handed
/// straight to the command.
/// </remarks>
public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
    }

    private StartupViewModel? ViewModel => DataContext as StartupViewModel;

    private void OnUnlockPasswordChanged(object sender, RoutedEventArgs e)
    {
        // Clearing a stale error as soon as the user starts retyping.
        if (ViewModel is { ErrorMessage: not null })
        {
            ViewModel.ErrorMessage = null;
        }
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        string password = UnlockPasswordBox.Password;

        if (ViewModel.UnlockCommand.CanExecute(password))
        {
            ViewModel.UnlockCommand.Execute(password);
        }

        UnlockPasswordBox.Clear();
    }

    private void OnCreatePasswordChanged(object sender, RoutedEventArgs e)
    {
        PasswordStrengthLevel level = PasswordStrength.Evaluate(CreatePasswordBox.Password);
        StrengthText.Text = CreatePasswordBox.Password.Length == 0
            ? string.Empty
            : PasswordStrength.Describe(level);
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var passwords = new PasswordPair(CreatePasswordBox.Password, ConfirmPasswordBox.Password);

        if (ViewModel.CreateCommand.CanExecute(passwords))
        {
            ViewModel.CreateCommand.Execute(passwords);
        }
    }
}
