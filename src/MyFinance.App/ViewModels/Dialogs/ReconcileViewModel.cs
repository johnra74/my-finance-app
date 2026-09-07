using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>One item on the reconcile list, with the tick that says it is on the statement.</summary>
public sealed partial class ReconcileRowViewModel : ObservableObject
{
    public ReconcileRowViewModel(Transaction transaction, bool isTicked)
    {
        Transaction = transaction;
        _isTicked = isTicked;
    }

    public Transaction Transaction { get; }

    public int Id => Transaction.Id;

    public DateOnly Date => Transaction.Date;

    public string? Number => Transaction.Number;

    public string PayeeName => Transaction.Payee?.Name ?? Transaction.Memo ?? "(no payee)";

    public Money Amount => Transaction.Amount;

    public string AmountText => Amount.ToAccountingString(CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool _isTicked;

    partial void OnIsTickedChanged(bool value) => Ticked?.Invoke(this, EventArgs.Empty);

    public event EventHandler? Ticked;
}

/// <summary>
/// Agrees a statement against the register, then locks the agreed items down.
/// </summary>
/// <remarks>
/// The difference indicator is the whole point of the screen: it has to reach zero before
/// the session can be finished, because a reconciliation that does not balance has
/// established nothing and would leave the next one starting from a figure the bank never
/// agreed to.
/// </remarks>
public sealed partial class ReconcileViewModel : DialogViewModel
{
    private readonly ReconcileService _reconcile;
    private readonly ReconcileSession _session;

    public ReconcileViewModel(ReconcileService reconcile, ReconcileSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _reconcile = reconcile;
        _session = session;

        var ticked = session.InitiallyCleared.ToHashSet();
        Rows = [];

        foreach (Transaction transaction in session.Outstanding)
        {
            var row = new ReconcileRowViewModel(transaction, ticked.Contains(transaction.Id));
            row.Ticked += (_, _) => Retally();
            Rows.Add(row);
        }

        _statementDate = DateTime.Today;
        _statementBalanceText = session.StartingBalance.ToString("N", CultureInfo.CurrentCulture);

        Retally();
    }

    public override string Title => $"Balance {_session.Account.Name}";

    public ObservableCollection<ReconcileRowViewModel> Rows { get; }

    public string AccountName => _session.Account.Name;

    public Money StartingBalance => _session.StartingBalance;

    public string StartingBalanceText =>
        StartingBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string LastReconciledText => _session.LastReconciledOn is DateOnly on
        ? $"Last balanced {on:d MMMM yyyy}"
        : "This account has never been balanced";

    [ObservableProperty]
    private DateTime _statementDate;

    /// <summary>The ending balance printed on the statement, as typed.</summary>
    [ObservableProperty]
    private string _statementBalanceText;

    [ObservableProperty]
    private Money _clearedTotal;

    [ObservableProperty]
    private Money _clearedBalance;

    [ObservableProperty]
    private Money _difference;

    [ObservableProperty]
    private bool _isBalanced;

    public string ClearedBalanceText => ClearedBalance.ToAccountingString(CultureInfo.CurrentCulture);

    public string DifferenceText => Difference.ToAccountingString(CultureInfo.CurrentCulture);

    public int TickedCount => Rows.Count(r => r.IsTicked);

    public string ProgressText =>
        $"{TickedCount} of {Rows.Count} item{(Rows.Count == 1 ? string.Empty : "s")} ticked";

    [RelayCommand]
    private void TickAll()
    {
        foreach (ReconcileRowViewModel row in Rows)
        {
            row.IsTicked = true;
        }
    }

    [RelayCommand]
    private void UntickAll()
    {
        foreach (ReconcileRowViewModel row in Rows)
        {
            row.IsTicked = false;
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        ErrorMessage = null;

        if (!Money.TryParse(StatementBalanceText, CultureInfo.CurrentCulture, out Money statement))
        {
            ErrorMessage = "The statement balance is not an amount.";
            return;
        }

        IsBusy = true;

        try
        {
            await _reconcile.CompleteAsync(
                _session.Account.Id,
                DateOnly.FromDateTime(StatementDate),
                statement,
                [.. Rows.Where(r => r.IsTicked).Select(r => r.Id)]).ConfigureAwait(true);

            Close(true);
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnStatementBalanceTextChanged(string value) => Retally();

    private void Retally()
    {
        ClearedTotal = Money.Sum(Rows.Where(r => r.IsTicked).Select(r => r.Amount));

        Money statement =
            Money.TryParse(StatementBalanceText, CultureInfo.CurrentCulture, out Money parsed)
                ? parsed
                : StartingBalance;

        var tally = new ReconcileTally(StartingBalance, ClearedTotal, statement);

        ClearedBalance = tally.ClearedBalance;
        Difference = tally.Difference;
        IsBalanced = tally.IsBalanced;

        OnPropertyChanged(nameof(ClearedBalanceText));
        OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(ProgressText));
    }
}
