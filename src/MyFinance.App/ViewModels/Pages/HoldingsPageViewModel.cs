using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Help;
using MyFinance.Core.Enums;
using MyFinance.Core.Investments;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One security holding, with quantity, cost, and value.</summary>
public sealed partial class HoldingRowViewModel : ObservableObject
{
    public required HoldingSummary Holding { get; init; }

    public string SecurityName => Holding.SecurityName;

    public string Symbol => Holding.Symbol ?? "—";

    public string QuantityText => Holding.Quantity.ToString();

    public string AverageCostText => Holding.AverageCost.ToString("C", CultureInfo.CurrentCulture);

    public string CostBasisText => Holding.CostBasis.ToString("C", CultureInfo.CurrentCulture);

    public string ValueText => Holding.Value.Amount.ToString("C", CultureInfo.CurrentCulture);

    public string PriceDateText => Holding.Value.PriceDate?.ToString("d", CultureInfo.CurrentCulture) ?? "—";

    public string AtCostText => Holding.Value.IsAtCost
        ? "at cost — no price recorded"
        : string.Empty;
}

/// <summary>
/// Investment account holdings: what is held and what it is worth.
/// </summary>
public sealed partial class HoldingsPageViewModel : PageViewModel
{
    private readonly InvestmentService _investments;
    private readonly IModalService _modals;
    private int _accountId;

    public HoldingsPageViewModel(
        InvestmentService investments,
        IModalService modals)
    {
        _investments = investments;
        _modals = modals;
        _asOfDate = DateTime.Today;
    }

    public override string Title => "Holdings";

    public override AppSection Section => AppSection.Banking;

    public override HelpTopic HelpTopic => HelpTopic.Investments;

    public ObservableCollection<HoldingRowViewModel> Holdings { get; } = [];

    [ObservableProperty]
    private HoldingRowViewModel? _selectedRow;

    [ObservableProperty]
    private DateTime _asOfDate;

    public string PortfolioValueText
    {
        get
        {
            if (Holdings.Count == 0)
                return "No holdings";

            Money total = Money.Sum(Holdings.Select(h => h.Holding.Value.Amount));
            return $"Total value: {total.ToString("C", CultureInfo.CurrentCulture)}";
        }
    }

    public void SetAccount(int accountId)
    {
        _accountId = accountId;
    }

    public override async Task OnNavigatedToAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        DateOnly asOf = DateOnly.FromDateTime(AsOfDate);

        _ = await RunBusyAsync(
            "Loading holdings",
            (_, token) => _investments.GetHoldingsAsync(_accountId, asOf, token)).ConfigureAwait(true);

        IReadOnlyList<HoldingSummary> results =
            await _investments.GetHoldingsAsync(_accountId, asOf).ConfigureAwait(true);

        Holdings.Clear();

        foreach (var holding in results)
        {
            Holdings.Add(new HoldingRowViewModel { Holding = holding });
        }

        OnPropertyChanged(nameof(PortfolioValueText));
    }

    [RelayCommand]
    private void RecordActivity()
    {
        // For now, start with an empty security list; user can type to find/create
        InvestmentEditorViewModel editor = InvestmentEditorViewModel.For(
            _investments,
            _accountId,
            []);

        if (_modals.Show(editor))
        {
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    private void SetPrice(HoldingRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        PriceEditorViewModel editor = PriceEditorViewModel.For(_investments, row.Holding.SecurityId);

        if (_modals.Show(editor))
        {
            _ = RefreshAsync();
        }
    }

    partial void OnAsOfDateChanged(DateTime value)
    {
        _ = RefreshAsync();
    }
}
