using System.Reflection;
using MyFinance.Core.Diagnostics;

namespace MyFinance.Core.Tests.Diagnostics;

/// <summary>
/// The writer: what it records, what it refuses to record, and what it does when it cannot.
/// </summary>
public sealed class DiagnosticLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "myfinance-log-tests", Guid.NewGuid().ToString("N"));

    private DiagnosticLog Open(long cap = 1024 * 1024, bool verbose = false) =>
        new(new DiagnosticOptions(_directory, cap, verbose), startWriter: false);

    private string Read() =>
        File.Exists(Path.Combine(_directory, DiagnosticOptions.FileName))
            ? File.ReadAllText(Path.Combine(_directory, DiagnosticOptions.FileName))
            : string.Empty;

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;   // so it carries a real stack trace
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    // -- The API is the redaction policy ------------------------------------------------

    [Fact]
    public void The_api_offers_no_way_to_pass_free_text()
    {
        // The whole design. With 59 catch blocks in this solution, a rule each of them must
        // remember is a rule that will be broken — so the writer offers no parameter that
        // could carry a payee, an amount or a path.
        MethodInfo[] recording =
        [
            typeof(DiagnosticLog).GetMethod(nameof(DiagnosticLog.Failure))!,
            typeof(DiagnosticLog).GetMethod(nameof(DiagnosticLog.Breadcrumb))!,
        ];

        foreach (MethodInfo method in recording)
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                parameter.ParameterType.ShouldNotBe(
                    typeof(string),
                    $"{method.Name} would let a caller write anything into the log");
            }
        }
    }

    // -- Writing ------------------------------------------------------------------------

    [Fact]
    public void An_entry_reaches_the_file()
    {
        using DiagnosticLog log = Open();
        log.Describe("1.2.3");

        log.Failure(Operation.SaveTransaction, Thrown(new InvalidOperationException("nope")));
        log.Flush();

        string text = Read();
        text.ShouldContain("SaveTransaction");
        text.ShouldContain("System.InvalidOperationException");
        text.ShouldContain("v1.2.3");
    }

    [Fact]
    public void An_entry_is_on_disk_without_a_clean_shutdown()
    {
        // Flushed per entry rather than at exit: the last entry before a crash is the one
        // worth having.
        DiagnosticLog log = Open();
        log.Failure(Operation.OpenBook, Thrown(new IOException("disk")));
        log.Flush();

        // Deliberately not disposed — stands in for the process dying here.
        Read().ShouldContain("OpenBook");
    }

    [Fact]
    public void A_null_exception_is_ignored_rather_than_throwing()
    {
        using DiagnosticLog log = Open();
        Should.NotThrow(() => log.Failure(Operation.RunReport, null!));
    }

    // -- Never becoming the failure it exists to record ---------------------------------

    [Fact]
    public void An_unwritable_directory_is_survived_silently()
    {
        // A read-only folder, a full disk, a disconnected drive. The application carries on.
        var log = new DiagnosticLog(
            new DiagnosticOptions(Path.Combine(_directory, "\0invalid")), startWriter: false);

        Should.NotThrow(() =>
        {
            log.Failure(Operation.Migrate, Thrown(new InvalidOperationException("x")));
            log.Flush();
        });

        log.Dispose();
    }

    [Fact]
    public void A_full_queue_drops_the_oldest_rather_than_blocking()
    {
        // No writer thread, so nothing drains: the queue fills and must not block.
        using DiagnosticLog log = Open();

        for (int i = 0; i < DiagnosticLog.QueueLimit + 50; i++)
        {
            log.Failure(Operation.LoadRegister, Thrown(new InvalidOperationException("x")));
        }

        log.Dropped.ShouldBeGreaterThan(0, "the queue is bounded and nothing was draining it");
    }

    [Fact]
    public void Two_writers_append_without_corrupting_each_other()
    {
        // The executable is a single file people copy around; two copies can run at once.
        using DiagnosticLog first = Open();
        using DiagnosticLog second = Open();

        first.Failure(Operation.OpenBook, Thrown(new InvalidOperationException("a")));
        second.Failure(Operation.CloseBook, Thrown(new InvalidOperationException("b")));
        first.Flush();
        second.Flush();

        string text = Read();
        text.ShouldContain("OpenBook");
        text.ShouldContain("CloseBook");
    }

    // -- Bounded ------------------------------------------------------------------------

    [Fact]
    public void The_log_rolls_over_at_the_size_cap()
    {
        // Breadcrumbs rather than failures: failures from one call site share a stack and are
        // collapsed on purpose, so they cannot grow a file.
        using DiagnosticLog log = Open(cap: 2000, verbose: true);

        for (int i = 0; i < 200; i++)
        {
            log.Breadcrumb(Operation.LoadRegister);
            log.Flush();
        }

        File.Exists(Path.Combine(_directory, DiagnosticOptions.PreviousFileName)).ShouldBeTrue();
    }

    [Fact]
    public void Only_two_files_are_ever_kept()
    {
        using DiagnosticLog log = Open(cap: 500, verbose: true);

        for (int i = 0; i < 400; i++)
        {
            log.Breadcrumb(Operation.LoadRegister);
            log.Flush();
        }

        Directory.GetFiles(_directory).Length.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void A_thousand_identical_failures_produce_one_counted_entry()
    {
        // A fault in a filter handler fires on every keystroke. Without collapsing, the entry
        // that explains it has already rolled off the end.
        using DiagnosticLog log = Open();

        Exception same = Thrown(new InvalidOperationException("same"));

        for (int i = 0; i < 1000; i++)
        {
            log.Failure(Operation.LoadRegister, same);
        }

        log.Flush();

        int lines = Read().Split('\n').Count(l => l.Contains("FAIL", StringComparison.Ordinal));
        lines.ShouldBeLessThan(20, "identical failures should be counted, not repeated");
        Read().ShouldContain("×");
    }

    [Fact]
    public void A_different_stack_is_not_collapsed_into_the_same_entry()
    {
        using DiagnosticLog log = Open();

        log.Failure(Operation.LoadRegister, Thrown(new InvalidOperationException("a")));
        log.Failure(Operation.SaveTransaction, Thrown(new IOException("b")));
        log.Flush();

        string text = Read();
        text.ShouldContain("LoadRegister");
        text.ShouldContain("SaveTransaction");
    }

    // -- Verbose ------------------------------------------------------------------------

    [Fact]
    public void Breadcrumbs_are_silent_until_verbose_is_on()
    {
        using (DiagnosticLog quiet = Open())
        {
            quiet.Breadcrumb(Operation.LoadRegister);
            quiet.Flush();
        }

        Read().ShouldBeEmpty();

        using DiagnosticLog loud = Open(verbose: true);
        loud.Breadcrumb(Operation.LoadRegister);
        loud.Flush();

        Read().ShouldContain("LoadRegister");
    }

    [Fact]
    public void Verbose_adds_operations_and_no_arguments()
    {
        // The redaction rules are absolute at every level: verbose changes how much is said
        // about what the application did, never about what the book holds.
        using DiagnosticLog log = Open(verbose: true);

        log.Breadcrumb(Operation.SuggestCategory);
        log.Flush();

        string text = Read();
        text.ShouldContain("SuggestCategory");

        // A breadcrumb is one line of fixed fields: no exception section, and nothing
        // indented beneath it where a payload would go.
        text.Trim().Split('\n').Length.ShouldBe(1);
        text.ShouldNotContain("    ");
    }

    // -- The book, without saying which book --------------------------------------------

    [Fact]
    public void An_entry_names_the_book_only_by_its_tag()
    {
        using DiagnosticLog log = Open();
        log.BookOpened(@"C:\Users\Jane Smith\Divorce settlement.mfdb", schemaVersion: 4);

        log.Failure(Operation.OpenBook, Thrown(new InvalidOperationException("x")));
        log.Flush();

        string text = Read();
        text.ShouldNotContain("Jane Smith");
        text.ShouldNotContain("Divorce");
        text.ShouldContain("schema:4");
        text.ShouldContain(BookTag.For(@"C:\Users\Jane Smith\Divorce settlement.mfdb"));
    }

    [Fact]
    public void Closing_the_book_stops_it_being_named_at_all()
    {
        using DiagnosticLog log = Open();
        log.BookOpened(@"C:\books\a.mfdb", 4);
        log.BookClosed();

        log.Failure(Operation.CloseBook, Thrown(new InvalidOperationException("x")));
        log.Flush();

        Read().ShouldContain(BookTag.None);
    }
}
