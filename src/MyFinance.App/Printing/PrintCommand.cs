using System.Windows;
using MyFinance.App.ViewModels.Pages;
using MyFinance.App.Views.Dialogs;
using MyFinance.Core.Printing;
using MyFinance.Core.Reporting;

namespace MyFinance.App.Printing;

/// <summary>
/// Opens the print preview for a register or a report.
/// </summary>
/// <remarks>
/// Nothing is sent to a printer from here. The preview states the page count, offers
/// orientation, and only its own Print button reaches a print queue — so a large job is
/// something the user sees before it happens rather than after.
/// </remarks>
internal static class PrintCommand
{
    /// <summary>
    /// The register, as it currently reads: the same rows, the same filter, the same figures.
    /// </summary>
    /// <param name="chosenColumns">
    /// Column keys the user picked, remembered between sessions. Null means "decide for me",
    /// and the fitter drops by priority until the page fits.
    /// </param>
    public static void ShowRegister(
        Window? owner,
        string accountName,
        IReadOnlyList<RegisterRowViewModel> rows,
        DateOnly? from,
        DateOnly? to,
        string? filterDescription,
        IReadOnlyCollection<string>? chosenColumns)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Show(owner, $"{accountName} register", geometry =>
        {
            ColumnFit fit = ColumnFitter.Fit(RegisterColumns.All, geometry.UsableWidth, chosenColumns);
            return RegisterDocument.Build(accountName, rows, fit, geometry, from, to, filterDescription);
        });
    }

    /// <summary>The report on screen: its chart and its table, never one without the other.</summary>
    public static void ShowReport(
        Window? owner,
        string title,
        string subtitle,
        IReadOnlyList<ReportRowViewModel> rows,
        IReadOnlyList<BarSlice> bars,
        string? warning)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(bars);

        Show(owner, title, geometry =>
            ReportDocument.Build(title, subtitle, rows, bars, geometry, warning));
    }

    private static void Show(Window? owner, string jobName, Func<PageGeometry, System.Windows.Documents.FixedDocument> build)
    {
        var preview = new PrintPreviewWindow(build, jobName)
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };

        preview.ShowDialog();
    }
}
