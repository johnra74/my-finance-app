using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>Record a price for a security as of a date.</summary>
public sealed partial class PriceEditorViewModel : DialogViewModel
{
    private readonly InvestmentService _investments;
    private int _securityId;

    private PriceEditorViewModel(InvestmentService investments)
    {
        _investments = investments;
        AsOfDate = DateTime.Today;
    }

    public static PriceEditorViewModel For(InvestmentService investments, int securityId)
    {
        var vm = new PriceEditorViewModel(investments);
        vm._securityId = securityId;
        return vm;
    }

    public override string Title => "Set Price";

    [ObservableProperty]
    private DateTime _asOfDate;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (!Money.TryParse(PriceText, CultureInfo.CurrentCulture, out Money price))
        {
            ErrorMessage = "The price is not a valid amount.";
            return;
        }

        try
        {
            DateOnly asOf = DateOnly.FromDateTime(AsOfDate);
            await _investments.SetPriceAsync(_securityId, asOf, price).ConfigureAwait(true);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    protected override void Cancel() => Close(false);
}
