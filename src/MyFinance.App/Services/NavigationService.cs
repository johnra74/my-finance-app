using Microsoft.Extensions.DependencyInjection;
using MyFinance.App.ViewModels;
using MyFinance.App.ViewModels.Pages;

namespace MyFinance.App.Services;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly Stack<PageViewModel> _back = new();

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public PageViewModel? CurrentPage { get; private set; }

    public AppSection CurrentSection => CurrentPage?.Section ?? AppSection.Home;

    public event EventHandler? Navigated;

    public bool CanGoBack => _back.Count > 0;

    public void GoToSection(AppSection section)
    {
        switch (section)
        {
            case AppSection.Home:
                GoTo<HomePageViewModel>();
                break;
            case AppSection.Banking:
                GoTo<AccountListPageViewModel>();
                break;
            case AppSection.Bills:
                GoTo<BillsPageViewModel>();
                break;
            case AppSection.Reports:
                GoTo<ReportsPageViewModel>();
                break;
            case AppSection.Budget:
                GoTo<BudgetPageViewModel>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section.");
        }
    }

    public void GoTo<TPage>()
        where TPage : PageViewModel =>
        GoTo(_services.GetRequiredService<TPage>());

    public void GoTo(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (CurrentPage is not null)
        {
            _back.Push(CurrentPage);
        }

        SetCurrent(page);
    }

    public void GoBack()
    {
        if (_back.Count == 0)
        {
            return;
        }

        SetCurrent(_back.Pop());
    }

    private void SetCurrent(PageViewModel page)
    {
        CurrentPage = page;
        Navigated?.Invoke(this, EventArgs.Empty);

        // Fire-and-forget by design: the page shows its own busy state while loading, and
        // navigation must not block the UI thread waiting on a database read.
        _ = page.OnNavigatedToAsync();
    }
}
