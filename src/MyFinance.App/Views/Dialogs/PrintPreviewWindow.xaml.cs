using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Documents.Serialization;
using System.Windows.Xps;
using MyFinance.Core.Printing;

namespace MyFinance.App.Views.Dialogs;

/// <summary>
/// Shows what will print, before anything is sent to a printer.
/// </summary>
/// <remarks>
/// <para>
/// The page count is on screen from the moment the window opens. A twenty-five-year register
/// is thousands of pages, somebody will ask for one by accident, and the moment to find that
/// out is before the paper starts moving rather than after.
/// </para>
/// <para>
/// Spooling goes through <see cref="XpsDocumentWriter.WriteAsync(DocumentPaginator)"/> rather
/// than <c>PrintDialog.PrintDocument</c>, which blocks until the job is queued. Composing the
/// pages still happens on this thread — WPF elements can only be built where they live — so
/// what this buys is that a large job does not freeze the window while it spools.
/// </para>
/// </remarks>
public partial class PrintPreviewWindow : Window
{
    private readonly Func<PageGeometry, FixedDocument> _build;
    private readonly string _jobName;
    private XpsDocumentWriter? _writer;

    public PrintPreviewWindow(Func<PageGeometry, FixedDocument> build, string jobName)
    {
        ArgumentNullException.ThrowIfNull(build);

        _build = build;
        _jobName = jobName;

        InitializeComponent();
        Render(PageGeometry.LetterPortrait);
    }

    private PageGeometry Current =>
        Orientation?.SelectedIndex == 1
            ? PageGeometry.LetterLandscape
            : PageGeometry.LetterPortrait;

    private void Render(PageGeometry geometry)
    {
        FixedDocument document = _build(geometry);
        Viewer.Document = document;

        int pages = document.Pages.Count;
        Summary.Text = pages == 1
            ? "1 page"
            : string.Format(CultureInfo.CurrentCulture, "{0:N0} pages", pages);

        // Said plainly rather than left for the printer to discover. The threshold is a
        // judgement about what somebody would regret, not a technical limit.
        bool large = pages > 50;
        Warning.Visibility = large ? Visibility.Visible : Visibility.Collapsed;
        Warning.Text = large
            ? "That is a lot of paper — check the date range before printing."
            : string.Empty;
    }

    private void OnOrientationChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: fires once while the window is still being constructed.
        if (IsInitialized)
        {
            Render(Current);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // The printer's own printable area wins over the assumed Letter geometry: the
        // document is rebuilt for it rather than scaled, so a row is never half-clipped.
        var geometry = new PageGeometry(
            dialog.PrintableAreaWidth,
            dialog.PrintableAreaHeight,
            PageGeometry.LetterPortrait.Margin);

        FixedDocument document = _build(geometry);

        try
        {
            PrintButton.IsEnabled = false;

            _writer = PrintQueue.CreateXpsDocumentWriter(dialog.PrintQueue);
            _writer.WritingCompleted += OnWritingCompleted;

            var ticket = dialog.PrintTicket;
            ticket.PageOrientation = Current == PageGeometry.LetterLandscape
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;

            _writer.WriteAsync(document.DocumentPaginator, ticket, _jobName);
        }
        catch (PrintingCanceledException)
        {
            PrintButton.IsEnabled = true;
        }
    }

    private void OnWritingCompleted(object sender, WritingCompletedEventArgs e)
    {
        PrintButton.IsEnabled = true;

        if (e.Error is null && !e.Cancelled)
        {
            DialogResult = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // A job still spooling when the window closes is cancelled rather than left running
        // against a document nobody can see any more.
        if (_writer is not null)
        {
            _writer.WritingCompleted -= OnWritingCompleted;

            try
            {
                _writer.CancelAsync();
            }
            catch (PrintingCanceledException)
            {
            }
        }

        base.OnClosed(e);
    }
}
