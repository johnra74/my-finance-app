using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Investments;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Record a buy, sell, dividend, or fee against an investment account.</summary>
public sealed partial class InvestmentEditorViewModel : DialogViewModel
{
    private readonly InvestmentService _investments;
    private int _accountId;

    private InvestmentEditorViewModel(InvestmentService investments)
    {
        _investments = investments;
        Date = DateTime.Today;
        Activity = InvestmentActivity.Buy;
    }

    public static InvestmentEditorViewModel For(
        InvestmentService investments,
        int accountId,
        IReadOnlyList<Security> securities_list)
    {
        var vm = new InvestmentEditorViewModel(investments);
        vm._accountId = accountId;
        vm.SecurityOptions = [.. securities_list];
        return vm;
    }

    public override string Title => "Record Investment Activity";

    public ObservableCollection<Security> SecurityOptions { get; private set; } = [];

    [ObservableProperty]
    private Security? _selectedSecurity;

    [ObservableProperty]
    private string? _securityName;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private InvestmentActivity _activity;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _pricePerUnitText = string.Empty;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _feesText = string.Empty;

    [ObservableProperty]
    private string? _memo;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        Security? security = SelectedSecurity;
        if (security is null)
        {
            ErrorMessage = "Select or enter a security.";
            return;
        }

        if (!Money.TryParse(AmountText, CultureInfo.CurrentCulture, out Money amount))
        {
            ErrorMessage = "The amount is not a valid number.";
            return;
        }

        Quantity quantity = Quantity.Zero;
        Money pricePerUnit = Money.Zero;

        if (Activity == InvestmentActivity.Buy || Activity == InvestmentActivity.Sell)
        {
            if (!Quantity.TryParse(QuantityText, out quantity) || quantity.IsZero)
            {
                ErrorMessage = "Enter the quantity.";
                return;
            }

            if (!Money.TryParse(PricePerUnitText, CultureInfo.CurrentCulture, out pricePerUnit))
            {
                ErrorMessage = "The price per unit is not a valid number.";
                return;
            }
        }

        if (!Money.TryParse(FeesText, CultureInfo.CurrentCulture, out Money fees))
        {
            ErrorMessage = "The fees are not a valid number.";
            return;
        }

        try
        {
            var draft = new InvestmentDraft
            {
                AccountId = _accountId,
                SecurityId = security.Id,
                Date = DateOnly.FromDateTime(Date),
                Activity = Activity,
                Quantity = quantity,
                PricePerUnit = pricePerUnit,
                Amount = amount,
                Fees = fees,
                Memo = Memo,
            };

            await _investments.RecordAsync(draft).ConfigureAwait(true);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    protected override void Cancel() => Close(false);
}
