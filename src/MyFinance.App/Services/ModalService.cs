using System.Windows;
using MyFinance.App.ViewModels.Dialogs;

namespace MyFinance.App.Services;

/// <inheritdoc cref="IModalService" />
public sealed class ModalService : IModalService
{
    private readonly Dictionary<Type, Func<Window>> _windows = [];

    /// <summary>Maps a view-model type onto the window that edits it.</summary>
    public void Register<TViewModel>(Func<Window> factory)
        where TViewModel : DialogViewModel
    {
        ArgumentNullException.ThrowIfNull(factory);
        _windows[typeof(TViewModel)] = factory;
    }

    public bool Show(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (!_windows.TryGetValue(viewModel.GetType(), out Func<Window>? factory))
        {
            throw new InvalidOperationException(
                $"No editor window is registered for {viewModel.GetType().Name}.");
        }

        Window window = factory();
        window.DataContext = viewModel;

        // Owning the dialog keeps it above the shell and centres it there, and means closing
        // the shell cannot leave an orphaned editor behind.
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);

        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        bool accepted = false;

        if (viewModel is DialogViewModel dialog)
        {
            void OnRequestClose(object? sender, bool result)
            {
                accepted = result;

                // DialogResult throws unless the window was shown modally, and setting it is
                // what closes the window, so guard rather than assume.
                if (window.IsLoaded)
                {
                    window.DialogResult = result;
                }
            }

            dialog.RequestClose += OnRequestClose;

            try
            {
                window.ShowDialog();
            }
            finally
            {
                dialog.RequestClose -= OnRequestClose;
            }

            return accepted;
        }

        return window.ShowDialog() == true;
    }
}
