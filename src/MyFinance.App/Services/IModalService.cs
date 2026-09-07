namespace MyFinance.App.Services;

/// <summary>
/// Shows a modal editor for a view model and reports whether it was accepted.
/// </summary>
/// <remarks>
/// View models raise dialogs through this rather than constructing windows, so the flow of
/// an edit — open editor, collect values, save, refresh the list — stays testable and free
/// of WPF types. The mapping from view model to window lives in one table in the
/// composition root.
/// </remarks>
public interface IModalService
{
    /// <summary>Shows the editor registered for this view model's type.</summary>
    /// <returns>True when the user accepted, false when they cancelled or closed it.</returns>
    bool Show(object viewModel);
}
