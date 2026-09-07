using System.Globalization;
using MyFinance.Core.Diagnostics;

namespace MyFinance.Core.Tests.Diagnostics;

/// <summary>
/// What may be written down about an exception, and what may not.
/// </summary>
public class SafeMessageTests
{
    /// <summary>Stands in for this solution's own exceptions, whose messages name payees.</summary>
    private sealed class BookValidationException(string message) : Exception(message);

    [Fact]
    public void A_validation_message_is_withheld_because_it_can_name_a_payee()
    {
        // Not hypothetical: ScheduleService throws "\"{payee}\" has nothing outstanding to
        // enter." and import diagnostics quote bank descriptors.
        var exception = new BookValidationException("\"Blue Bottle Coffee\" has nothing outstanding.");

        SafeMessages.For(exception).ShouldBe(SafeMessages.Withheld);
        SafeMessages.For(exception).ShouldNotContain("Blue Bottle");
    }

    [Fact]
    public void A_framework_exception_message_is_kept()
    {
        // These say things like "Value cannot be null. (Parameter 'draft')" — a type and a
        // parameter name, nothing of the user's.
        SafeMessages.For(new ArgumentNullException("draft")).ShouldContain("draft");
        SafeMessages.For(new InvalidOperationException("Sequence contains no elements"))
            .ShouldBe("Sequence contains no elements");
    }

    [Fact]
    public void A_withheld_message_still_records_the_exception_type()
    {
        LogEntry entry = LogEntry.FromException(
            Operation.EnterScheduled,
            new BookValidationException("\"Blue Bottle\" is overdue"),
            "1.0.0",
            BookTag.None,
            null);

        entry.Message.ShouldBe(SafeMessages.Withheld);

        // The type and the stack are what locate a fault; the message is a convenience.
        entry.ExceptionType.ShouldEndWith("BookValidationException");
    }

    [Fact]
    public void An_unknown_type_is_withheld_by_default()
    {
        // An allow-list, so a new exception type added anywhere is withheld until somebody
        // decides otherwise. The mistake falls in the safe direction.
        SafeMessages.IsSafe(typeof(BookValidationException)).ShouldBeFalse();
        SafeMessages.IsSafe(typeof(ArgumentException)).ShouldBeTrue();
    }

    [Fact]
    public void A_path_is_removed_even_from_a_safe_message()
    {
        // IOException names the file it failed on, and that file is frequently the book.
        string message = SafeMessages.For(
            new IOException(@"The process cannot access C:\Users\Jane\Divorce.mfdb"));

        message.ShouldNotContain("Jane");
        message.ShouldNotContain("Divorce");
        message.ShouldContain(PathScrubber.Replacement);
    }
}

/// <summary>Stripping paths out of anything before it is written down.</summary>
public class PathScrubberTests
{
    [Theory]
    [InlineData(@"cannot open C:\Users\Jane Smith\book.mfdb", "Jane Smith")]
    [InlineData(@"failed at \\server\share\books\x.mfdb", "server")]
    [InlineData("no such file /home/jack/finance/book.mfdb", "jack")]
    public void A_path_is_replaced_wherever_it_appears(string text, string secret)
    {
        string scrubbed = PathScrubber.Scrub(text);

        scrubbed.ShouldNotContain(secret);
        scrubbed.ShouldContain(PathScrubber.Replacement);
    }

    [Fact]
    public void Ordinary_prose_survives_untouched()
    {
        // "and/or" must not be mistaken for a path, or every message becomes unreadable.
        PathScrubber.Scrub("the value was null and/or empty")
            .ShouldBe("the value was null and/or empty");
    }

    [Fact]
    public void Nothing_is_answered_with_nothing()
    {
        PathScrubber.Scrub(null).ShouldBe(string.Empty);
        PathScrubber.Scrub(string.Empty).ShouldBe(string.Empty);
    }
}

/// <summary>Naming a book without saying which book it is.</summary>
public class BookTagTests
{
    [Fact]
    public void The_same_book_tags_the_same_way_twice()
    {
        BookTag.For(@"C:\books\mine.mfdb").ShouldBe(BookTag.For(@"C:\books\mine.mfdb"));
    }

    [Fact]
    public void The_same_book_tags_the_same_way_whatever_the_casing()
    {
        // Windows would otherwise tag one book two ways depending on how it was opened.
        BookTag.For(@"C:\Books\Mine.mfdb").ShouldBe(BookTag.For(@"c:\books\mine.mfdb"));
    }

    [Fact]
    public void Two_books_tag_differently()
    {
        BookTag.For(@"C:\books\a.mfdb").ShouldNotBe(BookTag.For(@"C:\books\b.mfdb"));
    }

    [Fact]
    public void The_same_name_in_two_folders_tags_differently()
    {
        BookTag.For(@"C:\work\book.mfdb").ShouldNotBe(BookTag.For(@"C:\home\book.mfdb"));
    }

    [Fact]
    public void The_tag_reveals_neither_the_name_nor_the_path()
    {
        string tag = BookTag.For(@"C:\Users\Jane Smith\Divorce settlement.mfdb");

        tag.ShouldNotContain("Jane");
        tag.ShouldNotContain("Divorce");
        tag.ShouldNotContain("settlement");
        tag.ShouldNotContain("mfdb");
        tag.Length.ShouldBe(8);
    }

    [Fact]
    public void No_book_is_said_so_rather_than_left_blank()
    {
        BookTag.For(null).ShouldBe(BookTag.None);
        BookTag.For("  ").ShouldBe(BookTag.None);
    }
}

/// <summary>What one line of the log carries.</summary>
public class LogEntryTests
{
    private static Exception Nested()
    {
        try
        {
            try
            {
                throw new IOException("inner failure");
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException("outer failure", inner);
            }
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void An_entry_carries_the_type_and_stack_not_just_the_message()
    {
        LogEntry entry = LogEntry.FromException(
            Operation.SaveTransaction, Nested(), "1.0.0", "abcd1234", 4);

        entry.ExceptionType.ShouldBe("System.InvalidOperationException");
        entry.Stack.ShouldNotBeNullOrWhiteSpace();
        entry.Render().ShouldContain("System.InvalidOperationException");
    }

    [Fact]
    public void Inner_exceptions_are_recorded_to_the_root()
    {
        LogEntry entry = LogEntry.FromException(
            Operation.SaveTransaction, Nested(), "1.0.0", "abcd1234", 4);

        entry.Inner.Count.ShouldBe(1);
        entry.Inner[0].ShouldContain("System.IO.IOException");
        entry.Render().ShouldContain("--->");
    }

    [Fact]
    public void An_entry_names_the_application_version()
    {
        // The first question about any fault report, answered without having to ask.
        LogEntry.FromException(Operation.OpenBook, new IOException("x"), "2.5.1", "abcd1234", 4)
            .Render()
            .ShouldContain("v2.5.1");
    }

    [Fact]
    public void The_rendered_line_is_culture_independent()
    {
        // A log read on a machine set to another locale must say the same thing, and a
        // timestamp whose format follows the reader is one nobody can sort.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            string rendered = new LogEntry
            {
                At = new DateTimeOffset(2026, 3, 1, 14, 30, 0, TimeSpan.Zero),
                Level = LogLevel.Failure,
                Operation = Operation.RunReport,
                AppVersion = "1.0.0",
                SchemaVersion = 4,
            }.Render();

            rendered.ShouldContain("2026-03-01 14:30:00");
            rendered.ShouldContain("schema:4");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Identical_failures_share_a_signature_and_different_ones_do_not()
    {
        LogEntry first = LogEntry.FromException(Operation.LoadRegister, Nested(), "1", "b", 4);
        LogEntry second = LogEntry.FromException(Operation.LoadRegister, Nested(), "1", "b", 4);
        LogEntry other = LogEntry.FromException(Operation.RunReport, Nested(), "1", "b", 4);

        first.Signature.ShouldBe(second.Signature);
        first.Signature.ShouldNotBe(other.Signature);
    }
}
