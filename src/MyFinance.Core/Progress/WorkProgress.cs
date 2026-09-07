using System.Globalization;

namespace MyFinance.Core.Progress;

/// <summary>
/// How far a long-running operation has got, in terms a person can read.
/// </summary>
/// <remarks>
/// <para>
/// One type for every slow operation in the application rather than one per service, so a
/// single piece of user interface can show all of them and no screen has to invent its own
/// way of saying "still going".
/// </para>
/// <para>
/// A total of zero means the work cannot be counted — reading a file, say, where the
/// interesting number is not known until it is finished. That is reported honestly as an
/// indeterminate bar rather than as a fabricated percentage.
/// </para>
/// </remarks>
/// <param name="Stage">What is happening, in the user's words: "Writing transactions".</param>
/// <param name="Done">How many units are finished.</param>
/// <param name="Total">How many there are, or zero when that is not knowable.</param>
public readonly record struct WorkProgress(string Stage, int Done = 0, int Total = 0)
{
    /// <summary>Work that is happening but cannot be counted.</summary>
    public static WorkProgress Starting(string stage) => new(stage);

    /// <summary>Whether a percentage can honestly be shown.</summary>
    public bool IsDeterminate => Total > 0;

    /// <summary>Between zero and one, or zero when the work cannot be counted.</summary>
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);

    /// <summary>The counts, or empty when there are none worth showing.</summary>
    public string CountText => IsDeterminate
        ? string.Format(CultureInfo.CurrentCulture, "{0:N0} of {1:N0}", Done, Total)
        : string.Empty;

    /// <summary>The whole thing on one line, for a status area with no room for two.</summary>
    public string Text => IsDeterminate ? $"{Stage} — {CountText}" : Stage;
}
