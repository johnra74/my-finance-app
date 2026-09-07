using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Printing;
using MyFinance.App.Services;
using MyFinance.Core.Help;
using MyFinance.Core.Entities;
using MyFinance.Core.Primitives;
using MyFinance.Core.Reporting;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Pages;

/// <summary>A report the user can pick.</summary>
/// <param name="Kind">Which report.</param>
/// <param name="Text">How it reads in the list.</param>
/// <param name="Group">The heading it sits under.</param>
public readonly record struct ReportChoice(ReportKind Kind, string Text, string Group);

/// <summary>A named date range for the report's picker.</summary>
/// <param name="Text">How it reads.</param>
/// <param name="Resolve">Turns today into a window.</param>
public sealed record DateRangeChoice(string Text, Func<DateOnly, (DateOnly From, DateOnly To)> Resolve);

/// <summary>One row of a report's table, under the chart.</summary>
public sealed class ReportRowViewModel
{
    public required int? Key { get; init; }

    public required string Label { get; init; }

    public required Money Amount { get; init; }

    public required int Count { get; init; }

    public required decimal Share { get; init; }

    /// <summary>Bar length as a share of the largest row, zero to one.</summary>
    public required double Fraction { get; init; }

    public string AmountText => Amount.Abs().ToString("C", CultureInfo.CurrentCulture);

    public string ShareText => Share.ToString("P1", CultureInfo.CurrentCulture);

    public string CountText => Count == 1 ? "1 transaction" : $"{Count} transactions";

    /// <summary>False for the folded "Other" row, which stands for many categories.</summary>
    public bool CanDrillDown => Key is not null;
}

/// <summary>One transaction in a drill-down list.</summary>
public sealed class ReportDetailViewModel
{
    public required ReportEntry Entry { get; init; }

    public string DateText => Entry.Date.ToString("d", CultureInfo.CurrentCulture);

    public string PayeeName => Entry.PayeeName ?? "(no payee)";

    public string CategoryPath => Entry.CategoryPath ?? ReportEngine.UncategorizedLabel;

    public string AccountName => Entry.AccountName;

    public Money Amount => Entry.Amount;

    public string AmountText => Amount.ToAccountingString(CultureInfo.CurrentCulture);
}

/// <summary>One gridline of the net-worth chart, already positioned.</summary>
/// <remarks>
/// The arithmetic that turns a tick's fraction into a y-coordinate lives here rather than in
/// a converter so the chart and its gridlines are laid out by the same numbers — a gridline
/// that disagrees with the line it sits behind is worse than no gridline at all.
/// </remarks>
/// <param name="Y">Distance down from the top of the plot.</param>
/// <param name="Width">How far the line runs.</param>
/// <param name="Label">The figure the line marks.</param>
public sealed record GridLineViewModel(double Y, double Width, string Label);

/// <summary>
/// The reports screen: pick a report and a window, see the chart, the table and the detail.
/// </summary>
/// <remarks>
/// The chart and the table are always both present. A chart shows the shape and a table
/// gives the figures, and one without the other leaves somebody unable to read the report —
/// which is also what makes it accessible without relying on colour.
/// </remarks>
public sealed partial class ReportsPageViewModel : PageViewModel
{
    private readonly ReportService _reports;
    private readonly AccountService _accounts;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IServiceProviderAccessor _services;

    public ReportsPageViewModel(
        ReportService reports,
        AccountService accounts,
        IDialogService dialogs,
        INavigationService navigation,
        IServiceProviderAccessor services)
    {
        _reports = reports;
        _accounts = accounts;
        _dialogs = dialogs;
        _navigation = navigation;
        _services = services;

        Reports =
        [
            new ReportChoice(ReportKind.SpendingByCategory, "Spending by category", "Income and expenses"),
            new ReportChoice(ReportKind.SpendingByPayee, "Spending by payee", "Income and expenses"),
            new ReportChoice(ReportKind.IncomeByCategory, "Income by category", "Income and expenses"),
            new ReportChoice(ReportKind.IncomeAndSpendingOverTime, "Income and spending over time", "Income and expenses"),
            new ReportChoice(ReportKind.SpendingComparison, "Spending compared with last period", "Income and expenses"),
            new ReportChoice(ReportKind.AccountBalances, "Account balances", "Assets and liabilities"),
            new ReportChoice(ReportKind.NetWorthOverTime, "Net worth over time", "Assets and liabilities"),
            new ReportChoice(ReportKind.TransactionDetail, "Every transaction", "Detail"),
        ];

        Ranges =
        [
            new DateRangeChoice("This month", today =>
                (new DateOnly(today.Year, today.Month, 1), today)),
            new DateRangeChoice("Last month", today =>
            {
                DateOnly start = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                return (start, start.AddMonths(1).AddDays(-1));
            }),
            new DateRangeChoice("Last 3 months", today => (today.AddMonths(-3), today)),
            new DateRangeChoice("Last 12 months", today => (today.AddMonths(-12), today)),
            new DateRangeChoice("This year", today => (new DateOnly(today.Year, 1, 1), today)),
            new DateRangeChoice("Last year", today =>
                (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31))),
            new DateRangeChoice("Everything", _ => (DateOnly.MinValue, DateOnly.MaxValue)),
        ];

        _selectedReport = Reports[0];
        _selectedRange = Ranges[3];
    }

    public override string Title => SelectedReport.Text;

    public override AppSection Section => AppSection.Reports;

    public override HelpTopic HelpTopic => HelpTopic.Reports;

    public IReadOnlyList<ReportChoice> Reports { get; }

    public IReadOnlyList<DateRangeChoice> Ranges { get; }

    public ObservableCollection<Account> Accounts { get; } = [];

    /// <summary>The table under the chart, for the grouped reports.</summary>
    public ObservableCollection<ReportRowViewModel> Rows { get; } = [];

    /// <summary>The bars of a magnitude chart.</summary>
    public ObservableCollection<BarSlice> Bars { get; } = [];

    /// <summary>The columns of an income-against-spending chart.</summary>
    public ObservableCollection<ColumnPair> Columns { get; } = [];

    /// <summary>Gridlines of whichever chart is showing.</summary>
    public ObservableCollection<AxisTick> Ticks { get; } = [];

    /// <summary>The same gridlines, placed in the net-worth chart's viewport.</summary>
    public ObservableCollection<GridLineViewModel> Gridlines { get; } = [];

    /// <summary>The drill-down list, or the whole detail report.</summary>
    public ObservableCollection<ReportDetailViewModel> Detail { get; } = [];

    public ObservableCollection<AccountBalanceRow> Balances { get; } = [];

    public ObservableCollection<ComparisonRow> Comparison { get; } = [];

    [ObservableProperty]
    private ReportChoice _selectedReport;

    [ObservableProperty]
    private DateRangeChoice _selectedRange;

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private bool _rollUpToParent;

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private Money _total;

    [ObservableProperty]
    private string _uncategorizedWarning = string.Empty;

    [ObservableProperty]
    private bool _hasUncategorized;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _detailCaption = string.Empty;

    /// <summary>The row the table has selected, which is what a double-click drills into.</summary>
    [ObservableProperty]
    private ReportRowViewModel? _selectedRow;

    /// <summary>The transaction the drill-down list has selected.</summary>
    [ObservableProperty]
    private ReportDetailViewModel? _selectedDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLineLabels))]
    private LineChart _line = LineChart.Empty;

    /// <summary>
    /// Whether every point on the net-worth chart gets its date written under it.
    /// </summary>
    /// <remarks>
    /// Past a dozen or so the labels collide into a grey smear, at which point the tooltip
    /// and the axis figures carry the reading instead.
    /// </remarks>
    public bool ShowsLineLabels => Line.Points.Count <= 16;

    // Which chart shape is showing. Only ever one at a time.
    [ObservableProperty]
    private bool _showsBars;

    [ObservableProperty]
    private bool _showsColumns;

    [ObservableProperty]
    private bool _showsLine;

    [ObservableProperty]
    private bool _showsBalances;

    [ObservableProperty]
    private bool _showsComparison;

    [ObservableProperty]
    private bool _showsDetail;

    [ObservableProperty]
    private string _firstPeriodLabel = string.Empty;

    [ObservableProperty]
    private string _secondPeriodLabel = string.Empty;

    public override IReadOnlyList<TaskGroup> TaskGroups =>
    [
        new TaskGroup
        {
            Header = "Common tasks",
            Links =
            [
                new TaskLink { Text = "Assign missing categories", Execute = () => FixUncategorizedCommand.Execute(null) },
                new TaskLink { Text = "Export to CSV…", Execute = () => ExportCommand.Execute(null) },
                new TaskLink { Text = "Refresh", Execute = () => RefreshCommand.Execute(null) },
            ],
        },
    ];

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Accounts.Count == 0)
        {
            foreach (Account account in await _accounts.GetAllAsync().ConfigureAwait(true))
            {
                Accounts.Add(account);
            }
        }

        ReportFilter filter = BuildFilter();
        ReportKind kind = SelectedReport.Kind;

        // Only the query goes to the background. Everything after the await is back on the UI
        // thread, which is what the observable collections below require.
        ReportOutput? output = await RunBusyAsync(
            "Running the report",
            (_, token) => _reports.RunAsync(kind, filter, token)).ConfigureAwait(true);

        if (output is null)
        {
            return;
        }

        ClearAll();
        Apply(output);

        UncategorizedWarning = output.UncategorizedWarning;
        HasUncategorized = output.HasUncategorized;

        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Lists the transactions behind one row.
    /// </summary>
    /// <remarks>
    /// The point of a report is usually the question it raises — "why was that so high" —
    /// so every row leads to the transactions that make it up.
    /// </remarks>
    [RelayCommand]
    private async Task DrillDownAsync(ReportRowViewModel? row)
    {
        // A double-click carries no parameter, so the table's own selection stands in.
        row ??= SelectedRow;

        if (row is null || !row.CanDrillDown)
        {
            return;
        }

        ReportFilter filter = BuildFilter();

        bool byPayee = SelectedReport.Kind == ReportKind.SpendingByPayee;

        IReadOnlyList<ReportEntry> entries = await _reports.DrillDownAsync(
            filter,
            categoryId: byPayee ? null : row.Key,
            payeeId: byPayee ? row.Key : null).ConfigureAwait(true);

        ShowDetail(entries, $"{row.Label} — {row.CountText}");
    }

    /// <summary>Lists the transactions that still need a category.</summary>
    [RelayCommand]
    private async Task FixUncategorizedAsync()
    {
        IReadOnlyList<ReportEntry> entries = await _reports
            .DrillDownAsync(BuildFilter(), uncategorizedOnly: true)
            .ConfigureAwait(true);

        if (entries.Count == 0)
        {
            _dialogs.ShowInformation("Categories", "Everything in this period has a category.");
            return;
        }

        ShowDetail(entries, $"{entries.Count} transactions with no category");
    }

    /// <summary>
    /// Opens the register at the account a listed transaction belongs to.
    /// </summary>
    /// <remarks>
    /// Without this the drill-down is a dead end: you find the charge you do not recognise
    /// and then have to go and look for it by hand.
    /// </remarks>
    [RelayCommand]
    private void OpenInRegister(ReportDetailViewModel? row)
    {
        row ??= SelectedDetail;

        if (row is null)
        {
            return;
        }

        RegisterPageViewModel page = _services.GetRequired<RegisterPageViewModel>();
        page.SetAccount(row.Entry.AccountId);
        _navigation.GoTo(page);
    }

    [RelayCommand]
    private void CloseDetail()
    {
        ShowsDetail = false;
        SelectedDetail = null;
        Detail.Clear();
        DetailCaption = string.Empty;
    }

    /// <summary>
    /// Writes the report to a comma-separated file.
    /// </summary>
    /// <remarks>
    /// The escape hatch this application exists to provide. Money's own file format is why
    /// the user is here; nothing should be readable only from inside this program.
    /// </remarks>
    [RelayCommand]
    private void PrintReport()
    {
        // The chart and the table together. Neither on its own is the report — which is also
        // what makes it readable without relying on colour, and on paper that matters more
        // than it does on screen.
        PrintCommand.ShowReport(
            owner: null,
            Title,
            RangeText,
            [.. Rows],
            [.. Bars],
            UncategorizedWarning);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string? path = _dialogs.PickExportLocation($"{SelectedReport.Text}.csv");

        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, BuildCsv()).ConfigureAwait(true);

            _dialogs.ShowInformation(
                "Export",
                $"The report was written to {Path.GetFileName(path)}.");
        }
        catch (IOException ex)
        {
            _dialogs.ShowError("Export", $"That file could not be written: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogs.ShowError("Export", $"That file could not be written: {ex.Message}");
        }
    }

    private string BuildCsv()
    {
        var builder = new System.Text.StringBuilder();

        if (ShowsDetail || SelectedReport.Kind == ReportKind.TransactionDetail)
        {
            builder.AppendLine("Date,Payee,Category,Account,Amount");

            foreach (ReportDetailViewModel row in Detail)
            {
                builder.AppendLine(string.Join(',',
                    Escape(row.DateText),
                    Escape(row.PayeeName),
                    Escape(row.CategoryPath),
                    Escape(row.AccountName),
                    row.Amount.ToDecimal().ToString(CultureInfo.InvariantCulture)));
            }

            return builder.ToString();
        }

        if (ShowsColumns)
        {
            builder.AppendLine("Period,Income,Spending,Net");

            foreach (ColumnPair column in Columns)
            {
                builder.AppendLine(string.Join(',',
                    Escape(column.Label),
                    column.Income.ToDecimal().ToString(CultureInfo.InvariantCulture),
                    column.Spending.ToDecimal().ToString(CultureInfo.InvariantCulture),
                    column.Net.ToDecimal().ToString(CultureInfo.InvariantCulture)));
            }

            return builder.ToString();
        }

        builder.AppendLine("Label,Amount,Transactions,Share");

        foreach (ReportRowViewModel row in Rows)
        {
            builder.AppendLine(string.Join(',',
                Escape(row.Label),
                row.Amount.ToDecimal().ToString(CultureInfo.InvariantCulture),
                row.Count.ToString(CultureInfo.InvariantCulture),
                row.Share.ToString(CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }

    /// <summary>Quotes a field that would otherwise break the column structure.</summary>
    private static string Escape(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private ReportFilter BuildFilter()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        (DateOnly from, DateOnly to) = SelectedRange.Resolve(today);

        RangeText = from == DateOnly.MinValue
            ? "Everything"
            : $"{from:d MMMM yyyy} through {to:d MMMM yyyy}";

        return ReportFilter.Everything with
        {
            From = from == DateOnly.MinValue ? null : from,
            To = to == DateOnly.MaxValue ? null : to,
            AccountIds = SelectedAccount is null ? [] : [SelectedAccount.Id],
            RollUpToParent = RollUpToParent,
            Period = ReportPeriod.Monthly,
        };
    }

    private void Apply(ReportOutput output)
    {
        if (output.Grouped is GroupedReport grouped)
        {
            BarChart chart = ChartGeometry.Bars(grouped);

            foreach (BarSlice bar in chart.Bars)
            {
                Bars.Add(bar);

                Rows.Add(new ReportRowViewModel
                {
                    Key = bar.Key,
                    Label = bar.Label,
                    Amount = bar.Amount,
                    Count = grouped.Rows.FirstOrDefault(r => r.Key == bar.Key)?.Count ?? 0,
                    Share = bar.Share,
                    Fraction = bar.Fraction,
                });
            }

            Total = grouped.Total;
            TotalText = grouped.Total.Abs().ToString("C", CultureInfo.CurrentCulture);
            ShowsBars = !chart.IsEmpty;
            IsEmpty = chart.IsEmpty;
            return;
        }

        if (output.Series is TimeSeriesReport series)
        {
            ColumnChart chart = ChartGeometry.Columns(series);

            foreach (ColumnPair column in chart.Columns)
            {
                Columns.Add(column);
            }

            foreach (AxisTick tick in chart.Ticks)
            {
                Ticks.Add(tick);
            }

            Total = series.Net;
            TotalText = series.Net.ToAccountingString(CultureInfo.CurrentCulture);
            ShowsColumns = !chart.IsEmpty;
            IsEmpty = chart.IsEmpty;
            return;
        }

        if (output.NetWorth is IReadOnlyList<NetWorthPoint> points)
        {
            Line = ChartGeometry.Line(
                output.Title,
                [.. points.Select(p => (p.Label, p.NetWorth))]);

            foreach (AxisTick tick in Line.Ticks)
            {
                Ticks.Add(tick);

                // Fraction is measured up from the baseline; the screen measures down.
                Gridlines.Add(new GridLineViewModel(
                    Line.Height * (1 - tick.Fraction),
                    Line.Width,
                    tick.Label));
            }

            Total = points.Count == 0 ? Money.Zero : points[^1].NetWorth;
            TotalText = Total.ToAccountingString(CultureInfo.CurrentCulture);
            ShowsLine = !Line.IsEmpty;
            IsEmpty = Line.IsEmpty;
            return;
        }

        if (output.Balances is IReadOnlyList<AccountBalanceRow> balances)
        {
            foreach (AccountBalanceRow row in balances)
            {
                Balances.Add(row);
            }

            Total = Money.Sum(balances.Select(b => b.Balance));
            TotalText = Total.ToAccountingString(CultureInfo.CurrentCulture);
            ShowsBalances = balances.Count > 0;
            IsEmpty = balances.Count == 0;
            return;
        }

        if (output.Comparison is ComparisonReport comparison)
        {
            foreach (ComparisonRow row in comparison.Rows)
            {
                Comparison.Add(row);
            }

            FirstPeriodLabel = comparison.FirstLabel;
            SecondPeriodLabel = comparison.SecondLabel;
            Total = comparison.Change;
            TotalText = comparison.Change.ToAccountingString(CultureInfo.CurrentCulture);
            ShowsComparison = !comparison.IsEmpty;
            IsEmpty = comparison.IsEmpty;
            return;
        }

        if (output.Detail is IReadOnlyList<ReportEntry> detail)
        {
            ShowDetail(detail, $"{detail.Count} transactions");
            Total = Money.Sum(detail.Select(d => d.Amount));
            TotalText = Total.ToAccountingString(CultureInfo.CurrentCulture);
            IsEmpty = detail.Count == 0;
        }
    }

    private void ShowDetail(IReadOnlyList<ReportEntry> entries, string caption)
    {
        Detail.Clear();

        foreach (ReportEntry entry in entries)
        {
            Detail.Add(new ReportDetailViewModel { Entry = entry });
        }

        DetailCaption = caption;
        ShowsDetail = true;
    }

    private void ClearAll()
    {
        SelectedRow = null;
        SelectedDetail = null;
        Rows.Clear();
        Bars.Clear();
        Columns.Clear();
        Ticks.Clear();
        Gridlines.Clear();
        Detail.Clear();
        Balances.Clear();
        Comparison.Clear();

        Line = LineChart.Empty;
        ShowsBars = ShowsColumns = ShowsLine = ShowsBalances = ShowsComparison = ShowsDetail = false;
        DetailCaption = string.Empty;
    }

    partial void OnSelectedReportChanged(ReportChoice value) => _ = RefreshAsync();

    partial void OnSelectedRangeChanged(DateRangeChoice value) => _ = RefreshAsync();

    partial void OnSelectedAccountChanged(Account? value) => _ = RefreshAsync();

    partial void OnRollUpToParentChanged(bool value) => _ = RefreshAsync();
}
