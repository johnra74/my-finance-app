using MyFinance.App.ViewModels;

namespace MyFinance.App.Services;

/// <summary>The top-level sections of the application, mirroring the tab bar.</summary>
public enum AppSection
{
    Home,
    Banking,
    Bills,
    Reports,
    Budget,
}

/// <summary>
/// Moves the shell between pages.
/// </summary>
/// <remarks>
/// Pages are resolved from the DI container by view-model type, so a later phase adds a
/// screen by registering its view model and a DataTemplate — nothing here changes.
/// </remarks>
public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    AppSection CurrentSection { get; }

    event EventHandler? Navigated;

    /// <summary>Navigates to the default page of a section.</summary>
    void GoToSection(AppSection section);

    /// <summary>Navigates to a specific page, resolving it from the container.</summary>
    void GoTo<TPage>()
        where TPage : PageViewModel;

    /// <summary>Navigates to an already-constructed page.</summary>
    void GoTo(PageViewModel page);

    bool CanGoBack { get; }

    void GoBack();
}
