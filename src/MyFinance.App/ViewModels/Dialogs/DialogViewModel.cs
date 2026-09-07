using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.ViewModels;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>
/// Base for anything shown in a modal editor.
/// </summary>
/// <remarks>
/// Closing is an event rather than a call onto a window: the view model decides that the
/// edit is finished, and the host window is what knows how to disappear.
/// </remarks>
public abstract partial class DialogViewModel : BusyViewModel
{
    /// <summary>Raised with true when the edit was accepted, false when it was abandoned.</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>Title shown in the window chrome.</summary>
    public abstract string Title { get; }

    /// <summary>Validation failure to show in the editor, or null when there is none.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [RelayCommand]
    protected virtual void Cancel() => Close(false);

    protected void Close(bool accepted) => RequestClose?.Invoke(this, accepted);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
