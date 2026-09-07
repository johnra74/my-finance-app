namespace MyFinance.Core.Printing;

/// <summary>What will actually print, and whether it fits.</summary>
/// <param name="Columns">The columns to print, in printing order.</param>
/// <param name="Dropped">Columns left out to make room. Empty when everything fitted.</param>
/// <param name="IsCramped">
/// True when the chosen columns are wider than the page. They still all print — the user asked
/// for them — but the caller should say so rather than let the page silently overflow.
/// </param>
public sealed record ColumnFit(
    IReadOnlyList<PrintColumn> Columns,
    IReadOnlyList<PrintColumn> Dropped,
    bool IsCramped)
{
    public double TotalWidth => Columns.Sum(c => c.Width);
}

/// <summary>
/// Decides which columns a printed page can carry.
/// </summary>
/// <remarks>
/// <para>
/// A register has more columns than a portrait page holds, so something has to give. The rule
/// is that it may never be given silently: a column the user explicitly asked for prints even
/// if the result is cramped, and anything dropped is reported so the caller can say what is
/// missing.
/// </para>
/// <para>
/// A printout that quietly disagrees with the screen is the failure this whole feature has to
/// avoid — it is the copy somebody takes to their accountant, and the memo column that went
/// missing on its own is not something they can notice.
/// </para>
/// </remarks>
public static class ColumnFitter
{
    /// <summary>
    /// Chooses columns for a page of the given usable width.
    /// </summary>
    /// <param name="candidates">Every column that could print, in printing order.</param>
    /// <param name="availableWidth">Usable width in device-independent pixels.</param>
    /// <param name="chosen">
    /// Keys the user explicitly asked for. Null or empty means "decide for me", and the fitter
    /// drops by priority until the page fits. Anything named here is kept whatever happens.
    /// </param>
    public static ColumnFit Fit(
        IReadOnlyList<PrintColumn> candidates,
        double availableWidth,
        IReadOnlyCollection<string>? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return new ColumnFit([], [], IsCramped: false);
        }

        bool userChose = chosen is { Count: > 0 };

        if (userChose)
        {
            // Exactly what was asked for, in printing order. Never trimmed to fit: the page
            // may be cramped, but it may not disagree with the request.
            List<PrintColumn> picked = [.. candidates.Where(c => chosen!.Contains(c.Key))];

            return new ColumnFit(
                picked,
                [.. candidates.Where(c => !chosen!.Contains(c.Key))],
                IsCramped: picked.Sum(c => c.Width) > availableWidth);
        }

        var keeping = candidates.ToList();
        var dropped = new List<PrintColumn>();

        // Drop the least essential first, and stop the moment it fits — never one more than
        // necessary.
        while (keeping.Sum(c => c.Width) > availableWidth && keeping.Count > 1)
        {
            PrintColumn worst = keeping.MaxBy(c => c.Priority)!;
            keeping.Remove(worst);
            dropped.Add(worst);
        }

        return new ColumnFit(
            [.. candidates.Where(keeping.Contains)],
            [.. candidates.Where(dropped.Contains)],
            IsCramped: keeping.Sum(c => c.Width) > availableWidth);
    }
}
