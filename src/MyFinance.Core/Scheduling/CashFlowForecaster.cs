using MyFinance.Core.Primitives;

namespace MyFinance.Core.Scheduling;

/// <summary>One scheduled item as the forecast sees it.</summary>
/// <param name="ScheduleId">Which series it came from, so a point can be traced back.</param>
/// <param name="Description">Payee or memo, for the tooltip on a forecast point.</param>
/// <param name="Amount">Signed: a bill is negative, a deposit positive.</param>
/// <param name="Rule">When it falls due.</param>
/// <param name="IsEstimate">True when the amount is a guess rather than a known figure.</param>
public sealed record ForecastItem(
    int ScheduleId,
    string Description,
    Money Amount,
    RecurrenceRule Rule,
    bool IsEstimate);

/// <summary>One dated movement in the projection.</summary>
/// <param name="Date">When it falls.</param>
/// <param name="ScheduleId">The series responsible.</param>
/// <param name="Description">What it is.</param>
/// <param name="Amount">Signed amount.</param>
/// <param name="IsEstimate">Whether the amount is a guess.</param>
/// <param name="IsAlreadyRecorded">
/// True when this is a transaction that has already happened rather than one still expected.
/// A window that starts in the past contains both, and a projection that showed only the
/// second half would misstate every balance after the first recorded movement.
/// </param>
public sealed record ForecastEvent(
    DateOnly Date,
    int ScheduleId,
    string Description,
    Money Amount,
    bool IsEstimate,
    bool IsAlreadyRecorded = false);

/// <summary>The balance on one day of the projection.</summary>
/// <param name="Date">The day.</param>
/// <param name="Balance">Balance at the end of it.</param>
/// <param name="Change">Net movement during it.</param>
/// <param name="Events">What moved.</param>
public sealed record ForecastPoint(
    DateOnly Date,
    Money Balance,
    Money Change,
    IReadOnlyList<ForecastEvent> Events)
{
    public bool HasEvents => Events.Count > 0;
}

/// <summary>A projected balance over a window, and what it implies.</summary>
public sealed record CashFlowProjection
{
    public required Money OpeningBalance { get; init; }

    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>One point per day that something happens, plus the opening day.</summary>
    public required IReadOnlyList<ForecastPoint> Points { get; init; }

    public required IReadOnlyList<ForecastEvent> Events { get; init; }

    public Money ClosingBalance => Points.Count == 0 ? OpeningBalance : Points[^1].Balance;

    /// <summary>Total of everything expected to arrive.</summary>
    public Money TotalIn => Money.Sum(Events.Where(e => e.Amount.IsPositive).Select(e => e.Amount));

    /// <summary>Total of everything expected to leave, as a negative figure.</summary>
    public Money TotalOut => Money.Sum(Events.Where(e => e.Amount.IsNegative).Select(e => e.Amount));

    /// <summary>The worst point of the projection — the number that actually matters.</summary>
    public Money LowestBalance =>
        Points.Count == 0 ? OpeningBalance : Points.Min(p => p.Balance);

    public DateOnly? LowestBalanceDate =>
        Points.Count == 0 ? null : Points.OrderBy(p => p.Balance).ThenBy(p => p.Date).First().Date;

    /// <summary>True when the projection dips below zero at any point.</summary>
    public bool GoesNegative => LowestBalance.IsNegative;

    /// <summary>The first day the balance is projected to be negative, if any.</summary>
    public DateOnly? FirstNegativeDate =>
        Points.FirstOrDefault(p => p.Balance.IsNegative)?.Date;

    /// <summary>True when any figure feeding the projection was an estimate.</summary>
    public bool IncludesEstimates => Events.Any(e => e.IsEstimate);

    /// <summary>Movements that have already happened, as opposed to expected ones.</summary>
    public IEnumerable<ForecastEvent> Recorded => Events.Where(e => e.IsAlreadyRecorded);

    /// <summary>Movements still expected.</summary>
    public IEnumerable<ForecastEvent> Expected => Events.Where(e => !e.IsAlreadyRecorded);
}

/// <summary>
/// Projects an account's balance forward over its scheduled bills and deposits.
/// </summary>
/// <remarks>
/// <para>
/// Answers the question the bills screen exists for: not "what do I owe" but "will there be
/// enough in the account when it is taken". The lowest point of the projection is the
/// number that matters, and it is rarely the closing balance — a large bill early in the
/// month followed by a salary can end comfortably while dipping below zero in between.
/// </para>
/// <para>
/// Pure, and takes an opening balance rather than reading one, so the whole projection is
/// testable without a book.
/// </para>
/// </remarks>
public static class CashFlowForecaster
{
    public static CashFlowProjection Project(
        Money openingBalance,
        DateOnly from,
        DateOnly to,
        IEnumerable<ForecastItem> items,
        IEnumerable<ForecastEvent>? alreadyRecorded = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var events = new List<ForecastEvent>(alreadyRecorded ?? []);

        foreach (ForecastItem item in items)
        {
            foreach (DateOnly due in RecurrenceCalculator.Occurrences(item.Rule, from, to))
            {
                events.Add(new ForecastEvent(
                    due,
                    item.ScheduleId,
                    item.Description,
                    item.Amount,
                    item.IsEstimate));
            }
        }

        // Ordered by date, then by amount so that on a day carrying both a bill and a
        // deposit the money goes out first. That is the pessimistic reading, and the point
        // of a forecast is to find the trouble rather than to look reassuring.
        events.Sort((left, right) =>
        {
            int byDate = left.Date.CompareTo(right.Date);
            return byDate != 0 ? byDate : left.Amount.CompareTo(right.Amount);
        });

        var points = new List<ForecastPoint>();
        Money running = openingBalance;

        // The opening point, so a projection with nothing scheduled still draws a line.
        points.Add(new ForecastPoint(from, running, Money.Zero, []));

        foreach (IGrouping<DateOnly, ForecastEvent> day in events.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            Money change = Money.Sum(day.Select(e => e.Amount));
            running += change;

            points.Add(new ForecastPoint(day.Key, running, change, [.. day]));
        }

        return new CashFlowProjection
        {
            OpeningBalance = openingBalance,
            From = from,
            To = to,
            Points = points,
            Events = events,
        };
    }
}
