using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Budgeting;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>One budgeted category, with how it is tracking.</summary>
public sealed partial class BudgetRowViewModel : ObservableObject
{
    public required BudgetLineResult Line { get; init; }

    public int CategoryId => Line.CategoryId;

    public string CategoryPath => Line.CategoryPath;

    public bool IsIncome => Line.Kind == CategoryKind.Income;

    public Money Budgeted => Line.Available;

    public Money Actual => Line.Actual;

    public Money Remaining => Line.Remaining;

    public decimal Progress => Line.Progress;

    /// <summary>Capped, so an overspent bar cannot run past the end of its track.</summary>
    public decimal ProgressCapped => Line.ProgressCapped;

    public bool IsOverBudget => Line.IsOverBudget;

    public bool RollsOver => Line.RollsOver;

    public string BudgetedText => Budgeted.ToString("C", CultureInfo.CurrentCulture);

    public string ActualText => Actual.ToString("C", CultureInfo.CurrentCulture);

    public string ProgressText => Line.ProgressText;

    /// <summary>
    /// What the bar means, in words.
    /// </summary>
    /// <remarks>
    /// The bar is coloured by status, and a status colour must never carry meaning on its
    /// own — so the same fact is always written out beside it.
    /// </remarks>
    public string StatusText => IsOverBudget
        ? $"Over by {Remaining.Abs().ToString("C", CultureInfo.CurrentCulture)}"
        : $"{Remaining.ToString("C", CultureInfo.CurrentCulture)} left";

    public string RolloverText => Line.RolloverIn.IsZero
        ? string.Empty
        : $"includes {Line.RolloverIn.ToString("C", CultureInfo.CurrentCulture)} carried forward";
}

/// <summary>
/// The budget screen: what was planned against what happened.
/// </summary>
public sealed partial class BudgetPageViewModel : PageViewModel
{
    private readonly BudgetService _budgets;
    private readonly CategoryService _categories;
    private readonly IModalService _modals;
    private readonly IDialogService _dialogs;

    public BudgetPageViewModel(
        BudgetService budgets,
        CategoryService categories,
        IModalService modals,
        IDialogService dialogs)
    {
        _budgets = budgets;
        _categories = categories;
        _modals = modals;
        _dialogs = dialogs;

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        _month = new DateTime(today.Year, today.Month, 1);
    }

    public override string Title => ShowYear ? $"Budget for {Month.Year}" : "Monthly budget";

    public override AppSection Section => AppSection.Budget;

    public override HelpTopic HelpTopic => HelpTopic.Budgets;

    public ObservableCollection<BudgetRowViewModel> Spending { get; } = [];

    public ObservableCollection<BudgetRowViewModel> Income { get; } = [];

    [ObservableProperty]
    private DateTime _month;

    [ObservableProperty]
    private bool _showYear;

    [ObservableProperty]
    private BudgetRowViewModel? _selected;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _periodLabel = string.Empty;

    [ObservableProperty]
    private Money _budgetedSpending;

    [ObservableProperty]
    private Money _actualSpending;

    [ObservableProperty]
    private Money _remainingToSpend;

    [ObservableProperty]
    private Money _plannedNet;

    [ObservableProperty]
    private Money _actualNet;

    [ObservableProperty]
    private int _overBudgetCount;

    public string BudgetedSpendingText => BudgetedSpending.ToString("C", CultureInfo.CurrentCulture);

    public string ActualSpendingText => ActualSpending.ToString("C", CultureInfo.CurrentCulture);

    public string RemainingToSpendText => RemainingToSpend.ToString("C", CultureInfo.CurrentCulture);

    public string ActualNetText => ActualNet.ToAccountingString(CultureInfo.CurrentCulture);

    public string OverBudgetText => OverBudgetCount switch
    {
        0 => "Nothing is over budget.",
        1 => "1 category is over budget.",
        _ => $"{OverBudgetCount} categories are over budget.",
    };

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "Set a budget…", Execute = () => EditCommand.Execute(null) },
                new TaskLink { Text = "Copy this month across the year", Execute = () => CopyYearCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DateOnly period = DateOnly.FromDateTime(Month);
        bool wholeYear = ShowYear;

        BudgetPeriodResult? result = await RunBusyAsync(
            "Working out the budget",
            (_, token) => wholeYear
                ? _budgets.GetYearAsync(period.Year, token)
                : _budgets.GetMonthAsync(period, token)).ConfigureAwait(true);

        if (result is null)
        {
            return;
        }

        int? previous = Selected?.CategoryId;

        Spending.Clear();
        Income.Clear();

        foreach (BudgetLineResult line in result.Lines)
        {
            var row = new BudgetRowViewModel { Line = line };

            if (row.IsIncome)
            {
                Income.Add(row);
            }
            else
            {
                Spending.Add(row);
            }
        }

        PeriodLabel = result.Label;
        BudgetedSpending = result.BudgetedSpending;
        ActualSpending = result.ActualSpending;
        RemainingToSpend = result.RemainingToSpend;
        PlannedNet = result.PlannedNet;
        ActualNet = result.ActualNet;
        OverBudgetCount = result.OverBudgetCount;
        IsEmpty = result.IsEmpty;

        Selected = Spending.Concat(Income).FirstOrDefault(r => r.CategoryId == previous);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BudgetedSpendingText));
        OnPropertyChanged(nameof(ActualSpendingText));
        OnPropertyChanged(nameof(RemainingToSpendText));
        OnPropertyChanged(nameof(ActualNetText));
        OnPropertyChanged(nameof(OverBudgetText));
    }

    [RelayCommand]
    private void PreviousMonth() => Month = Month.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => Month = Month.AddMonths(1);

    [RelayCommand]
    private async Task EditAsync(BudgetRowViewModel? row)
    {
        BudgetEditorViewModel editor = BudgetEditorViewModel.For(
            _budgets,
            await _categories.GetAllAsync().ConfigureAwait(true),
            DateOnly.FromDateTime(Month),
            row?.Line);

        if (_modals.Show(editor))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearAsync(BudgetRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
            "Remove budget",
            $"Remove the budget for \"{row.CategoryPath}\" in {PeriodLabel}?\n\nTransactions are untouched."))
        {
            return;
        }

        await _budgets.ClearAsync(row.CategoryId, DateOnly.FromDateTime(Month)).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Copies this month's figures across the rest of the year.
    /// </summary>
    /// <remarks>
    /// A budget is mostly the same number repeated, and typing twelve of everything is how
    /// people give up on budgeting.
    /// </remarks>
    [RelayCommand]
    private async Task CopyYearAsync()
    {
        DateOnly source = DateOnly.FromDateTime(Month);
        int remaining = 12 - source.Month;

        if (remaining <= 0)
        {
            _dialogs.ShowInformation("Copy budget", "December is the last month of the year.");
            return;
        }

        if (!_dialogs.Confirm(
            "Copy budget",
            $"Copy {PeriodLabel}'s budget onto the remaining {remaining} month{(remaining == 1 ? string.Empty : "s")} of {source.Year}?\n\nMonths that already have a figure are left alone."))
        {
            return;
        }

        int written = await _budgets
            .CopyMonthAsync(source, source.AddMonths(1), remaining)
            .ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);

        _dialogs.ShowInformation("Copy budget", $"{written} monthly figures were set.");
    }

    partial void OnMonthChanged(DateTime value) => _ = RefreshAsync();

    partial void OnShowYearChanged(bool value) => _ = RefreshAsync();
}
