using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Core.Registers;
using MyFinance.Core.Scheduling;
using MyFinance.Core.Validation;

namespace MyFinance.Data.Services;

/// <summary>
/// Manages recurring bills and deposits, and writes them into the register when they fall due.
/// </summary>
/// <remarks>
/// <para>
/// Due dates are computed from the recurrence rule, but each one is also recorded as a
/// <see cref="ScheduleOccurrence"/> once it has been entered or skipped. That is what makes
/// "nine occurrences past due" a fact rather than a recomputation — and, more importantly,
/// what stops a bill being entered into the register twice.
/// </para>
/// <para>
/// Occurrences are materialized lazily and only up to a horizon. Recording every future date
/// of an open-ended series is impossible, and recording every past one for a schedule
/// carried over from a decade-old file would write thousands of rows nobody asked for.
/// </para>
/// </remarks>
public sealed class ScheduleService
{
    public const string NotFound = "schedule.not_found";
    public const string AccountNotFound = "schedule.account_not_found";
    public const string AccountReadOnly = "schedule.account_read_only";
    public const string AmountRequired = "schedule.amount_required";
    public const string SplitsDoNotSum = "schedule.splits_mismatch";
    public const string OccurrenceNotFound = "schedule.occurrence_not_found";
    public const string OccurrenceResolved = "schedule.occurrence_resolved";
    public const string SeriesFinished = "schedule.series_finished";

    /// <summary>
    /// How many past occurrences of one series will be materialized.
    /// </summary>
    /// <remarks>
    /// A safety valve for a schedule that starts years before the book does — the kind that
    /// arrives from an imported file. Beyond this the bills list still shows the series as
    /// overdue; it simply stops counting individually.
    /// </remarks>
    public const int MaximumBacklog = 240;

    private readonly IBookContextFactory _factory;

    public ScheduleService(IBookContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// The bills summary: every active series, when it is next due, and how far behind it is.
    /// </summary>
    public async Task<BillsSummary> GetBillsAsync(
        DateOnly today,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<ScheduledTransaction> schedules = await db.ScheduledTransactions
            .AsNoTracking()
            .Include(s => s.Payee)
            .Include(s => s.Account)
            .Include(s => s.Splits)
            .ThenInclude(s => s.Category)
            .ThenInclude(c => c!.Parent)
            .Where(s => includeInactive || s.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (schedules.Count == 0)
        {
            return BillsSummary.Empty;
        }

        List<int> ids = [.. schedules.Select(s => s.Id)];

        // Only the resolved ones matter here: a pending occurrence is recomputed from the
        // rule anyway, whereas an entered or skipped one is a fact that overrides it.
        List<ScheduleOccurrence> resolved = await db.ScheduleOccurrences
            .AsNoTracking()
            .Where(o => ids.Contains(o.ScheduledTransactionId)
                && o.State != ScheduleOccurrenceState.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ILookup<int, ScheduleOccurrence> resolvedBySchedule =
            resolved.ToLookup(o => o.ScheduledTransactionId);

        var bills = new List<BillListItem>(schedules.Count);

        foreach (ScheduledTransaction schedule in schedules)
        {
            var settled = resolvedBySchedule[schedule.Id].Select(o => o.DueDate).ToHashSet();

            List<DateOnly> outstanding = OutstandingDueDates(schedule, settled, today);

            bills.Add(new BillListItem
            {
                Schedule = schedule,
                PayeeName = schedule.Payee?.Name ?? schedule.Memo ?? "(no payee)",
                AccountName = schedule.Account?.Name ?? "(account deleted)",
                CategoryName = schedule.Splits.Count == 1
                    ? schedule.Splits.First().Category?.FullName
                    : schedule.Splits.Count > 1 ? "— Split —" : null,
                NextDue = outstanding.Count > 0
                    ? outstanding[0]
                    : RecurrenceCalculator.NextOnOrAfter(ToRule(schedule), today),
                OverdueCount = outstanding.Count(d => d < today),
                DaysOverdue = outstanding.Count > 0 && outstanding[0] < today
                    ? today.DayNumber - outstanding[0].DayNumber
                    : 0,
            });
        }

        // Overdue first, then by due date — the order the reference books use, and the one
        // that puts what needs attention at the top.
        bills.Sort((left, right) =>
        {
            int byOverdue = right.IsOverdue.CompareTo(left.IsOverdue);
            if (byOverdue != 0)
            {
                return byOverdue;
            }

            return (left.NextDue ?? DateOnly.MaxValue).CompareTo(right.NextDue ?? DateOnly.MaxValue);
        });

        return new BillsSummary
        {
            Bills = bills,
            AccountBalances = await LoadBalancesAsync(db, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<ScheduledTransaction?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        return await db.ScheduledTransactions
            .AsNoTracking()
            .Include(s => s.Payee)
            .Include(s => s.Splits)
            .ThenInclude(s => s.Category)
            .ThenInclude(c => c!.Parent)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(ScheduleDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Guard(draft);

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await GuardAccountAsync(db, draft.AccountId, cancellationToken).ConfigureAwait(false);

        Payee? payee = await PayeeService
            .ResolveAsync(db, draft.PayeeName, cancellationToken)
            .ConfigureAwait(false);

        var schedule = new ScheduledTransaction
        {
            AccountId = draft.AccountId,
            Memo = Trim(draft.Memo),
            Amount = draft.Amount,
            IsEstimate = draft.IsEstimate,
            PaymentMethod = draft.PaymentMethod,
            Frequency = draft.Frequency,
            Interval = Math.Max(1, draft.Interval),
            StartDate = draft.StartDate,
            SecondDayOfMonth = draft.SecondDayOfMonth,
            WeekendShift = draft.WeekendShift,
            EndKind = draft.EndKind,
            EndDate = draft.EndDate,
            OccurrenceCount = draft.OccurrenceCount,
            AutoEnter = draft.AutoEnter,
            DaysAheadToEnter = Math.Max(0, draft.DaysAheadToEnter),
            IsActive = draft.IsActive,
        };

        if (payee is not null)
        {
            schedule.Payee = payee;
        }

        ApplySplits(schedule, draft);

        db.ScheduledTransactions.Add(schedule);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return schedule.Id;
    }

    public async Task UpdateAsync(ScheduleDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Guard(draft);

        if (draft.Id is not int id)
        {
            throw new BookValidationException(NotFound, "That bill has not been saved yet.");
        }

        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        ScheduledTransaction schedule = await db.ScheduledTransactions
            .Include(s => s.Splits)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That bill no longer exists.");

        await GuardAccountAsync(db, draft.AccountId, cancellationToken).ConfigureAwait(false);

        Payee? payee = await PayeeService
            .ResolveAsync(db, draft.PayeeName, cancellationToken)
            .ConfigureAwait(false);

        bool patternMoved = schedule.StartDate != draft.StartDate
            || schedule.Frequency != draft.Frequency
            || schedule.Interval != draft.Interval
            || schedule.SecondDayOfMonth != draft.SecondDayOfMonth;

        schedule.AccountId = draft.AccountId;
        schedule.Memo = Trim(draft.Memo);
        schedule.Amount = draft.Amount;
        schedule.IsEstimate = draft.IsEstimate;
        schedule.PaymentMethod = draft.PaymentMethod;
        schedule.Frequency = draft.Frequency;
        schedule.Interval = Math.Max(1, draft.Interval);
        schedule.StartDate = draft.StartDate;
        schedule.SecondDayOfMonth = draft.SecondDayOfMonth;
        schedule.WeekendShift = draft.WeekendShift;
        schedule.EndKind = draft.EndKind;
        schedule.EndDate = draft.EndDate;
        schedule.OccurrenceCount = draft.OccurrenceCount;
        schedule.AutoEnter = draft.AutoEnter;
        schedule.DaysAheadToEnter = Math.Max(0, draft.DaysAheadToEnter);
        schedule.IsActive = draft.IsActive;

        schedule.PayeeId = payee?.Id;
        if (payee is not null)
        {
            schedule.Payee = payee;
        }

        ApplySplits(schedule, draft);

        if (patternMoved)
        {
            // The dates the old pattern produced no longer exist, so the pending markers
            // pointing at them are meaningless. Entered and skipped ones stay: those record
            // something that actually happened.
            List<ScheduleOccurrence> stale = await db.ScheduleOccurrences
                .Where(o => o.ScheduledTransactionId == id
                    && o.State == ScheduleOccurrenceState.Pending)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            db.ScheduleOccurrences.RemoveRange(stale);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        ScheduledTransaction schedule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);
        schedule.IsActive = isActive;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a series. Transactions it already produced stay in the register.
    /// </summary>
    /// <remarks>
    /// Removing them would rewrite history: the money genuinely left the account, whatever
    /// happens to the schedule that predicted it.
    /// </remarks>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        ScheduledTransaction schedule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        db.ScheduledTransactions.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the next outstanding occurrence of a series into the register.
    /// </summary>
    /// <param name="id">The series.</param>
    /// <param name="today">The date to measure "outstanding" against.</param>
    /// <param name="postOn">
    /// The date to record the transaction under. Defaults to the due date, so entering a
    /// backlog keeps each payment on the day it was actually owed.
    /// </param>
    /// <param name="actualAmount">Overrides the series amount for this one instance.</param>
    public async Task<int> EnterNextAsync(
        int id,
        DateOnly today,
        DateOnly? postOn = null,
        Money? actualAmount = null,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        ScheduledTransaction schedule = await LoadForEntryAsync(db, id, cancellationToken)
            .ConfigureAwait(false);

        DateOnly due = await NextOutstandingAsync(db, schedule, today, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(
                SeriesFinished,
                $"\"{schedule.Payee?.Name ?? "That bill"}\" has nothing outstanding to enter.");

        int transactionId = await WriteOccurrenceAsync(
            db, schedule, due, postOn ?? due, actualAmount, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return transactionId;
    }

    /// <summary>
    /// Marks the next outstanding occurrence as skipped without writing anything.
    /// </summary>
    /// <remarks>
    /// The way to clear a bill that did not actually happen this month — a holiday from a
    /// subscription, say — without either paying it or destroying the series.
    /// </remarks>
    public async Task<DateOnly> SkipNextAsync(
        int id,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        ScheduledTransaction schedule = await RequireAsync(db, id, cancellationToken).ConfigureAwait(false);

        DateOnly due = await NextOutstandingAsync(db, schedule, today, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(
                SeriesFinished,
                "That bill has nothing outstanding to skip.");

        db.ScheduleOccurrences.Add(new ScheduleOccurrence
        {
            ScheduledTransactionId = schedule.Id,
            DueDate = due,
            State = ScheduleOccurrenceState.Skipped,
            ResolvedUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return due;
    }

    /// <summary>
    /// Enters every occurrence of a series that is already due, oldest first.
    /// </summary>
    /// <remarks>
    /// What the "enter in register" button does for a bill nine occurrences behind: each is
    /// posted on the day it was owed rather than all of them landing today, so the register
    /// and the running balance stay truthful about when the money left.
    /// </remarks>
    public async Task<EnterResult> EnterAllDueAsync(
        int id,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        ScheduledTransaction schedule = await LoadForEntryAsync(db, id, cancellationToken)
            .ConfigureAwait(false);

        HashSet<DateOnly> settled = await SettledDatesAsync(db, id, cancellationToken)
            .ConfigureAwait(false);

        List<DateOnly> due =
        [
            .. OutstandingDueDates(schedule, settled, today).Where(d => d <= today)
        ];

        var written = new List<int>();

        foreach (DateOnly date in due)
        {
            written.Add(await WriteOccurrenceAsync(
                db, schedule, date, date, null, cancellationToken).ConfigureAwait(false));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new EnterResult
        {
            Entered = written.Count,
            TransactionIds = written,
            Total = schedule.Amount * written.Count,
        };
    }

    /// <summary>
    /// Enters everything that has fallen due on series marked to do so automatically.
    /// </summary>
    /// <remarks>
    /// Run when a book is opened. Only touches series the user explicitly set to auto-enter,
    /// and only up to their own days-ahead setting — writing to the register unasked is the
    /// sort of thing that has to be opted into, once, deliberately.
    /// </remarks>
    public async Task<EnterResult> AutoEnterDueAsync(
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();
        await using var scope = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ScheduledTransaction> schedules = await db.ScheduledTransactions
            .Include(s => s.Splits)
            .Include(s => s.Account)
            .Where(s => s.IsActive && s.AutoEnter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var written = new List<int>();
        Money total = Money.Zero;

        foreach (ScheduledTransaction schedule in schedules)
        {
            if (schedule.Account?.IsReadOnly ?? true)
            {
                continue;
            }

            HashSet<DateOnly> settled = await SettledDatesAsync(db, schedule.Id, cancellationToken)
                .ConfigureAwait(false);

            DateOnly horizon = today.AddDays(schedule.DaysAheadToEnter);

            foreach (DateOnly date in OutstandingDueDates(schedule, settled, horizon)
                .Where(d => d <= horizon))
            {
                written.Add(await WriteOccurrenceAsync(
                    db, schedule, date, date, null, cancellationToken).ConfigureAwait(false));

                total += schedule.Amount;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new EnterResult { Entered = written.Count, TransactionIds = written, Total = total };
    }

    /// <summary>Past occurrences of a series, most recent first.</summary>
    public async Task<IReadOnlyList<OccurrenceListItem>> GetHistoryAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<ScheduleOccurrence> occurrences = await db.ScheduleOccurrences
            .AsNoTracking()
            .Include(o => o.ScheduledTransaction)
            .ThenInclude(s => s!.Payee)
            .Where(o => o.ScheduledTransactionId == id && o.State != ScheduleOccurrenceState.Pending)
            .OrderByDescending(o => o.DueDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. occurrences.Select(o => new OccurrenceListItem
            {
                Occurrence = o,
                PayeeName = o.ScheduledTransaction?.Payee?.Name ?? "(no payee)",
            })
        ];
    }

    /// <summary>
    /// Projects an account's balance forward over everything scheduled against it.
    /// </summary>
    /// <param name="accountId">The account, or null to project across all of them.</param>
    /// <param name="from">Start of the window; the opening balance is measured here.</param>
    /// <param name="to">End of the window.</param>
    public async Task<CashFlowProjection> ForecastAsync(
        int? accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => !a.IsClosed && (accountId == null || a.Id == accountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<int> accountIds = [.. accounts.Select(a => a.Id)];

        List<Transaction> transactions = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Payee)
            .Where(t => accountIds.Contains(t.AccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The opening balance is the position as at the start of the window, not today's —
        // otherwise a forecast starting in the past would double-count what has since
        // already been paid.
        Money opening = Money.Sum(accounts.Select(a =>
            BalanceCalculator.BalanceAsOf(
                a.OpeningBalance,
                transactions.Where(t => t.AccountId == a.Id),
                from.AddDays(-1))));

        List<ScheduledTransaction> schedules = await db.ScheduledTransactions
            .AsNoTracking()
            .Include(s => s.Payee)
            .Where(s => s.IsActive && accountIds.Contains(s.AccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var settled = (await db.ScheduleOccurrences
            .AsNoTracking()
            .Where(o => o.State != ScheduleOccurrenceState.Pending)
            .Select(o => new { o.ScheduledTransactionId, o.DueDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToLookup(o => o.ScheduledTransactionId, o => o.DueDate);

        var items = new List<ForecastItem>(schedules.Count);

        foreach (ScheduledTransaction schedule in schedules)
        {
            items.Add(new ForecastItem(
                schedule.Id,
                schedule.Payee?.Name ?? schedule.Memo ?? "Scheduled",
                schedule.Amount,
                ToRule(schedule),
                schedule.IsEstimate));
        }

        // Transactions already in the register that fall inside the window. They come after
        // the opening balance was taken, so without them the line would ignore money that
        // has demonstrably already moved.
        List<ForecastEvent> recorded =
        [
            .. transactions
                .Where(t => !t.IsVoid && t.Date >= from && t.Date <= to)
                .Select(t => new ForecastEvent(
                    t.Date,
                    ScheduleId: 0,
                    t.Payee?.Name ?? t.Memo ?? "Recorded",
                    t.Amount,
                    IsEstimate: false,
                    IsAlreadyRecorded: true))
        ];

        CashFlowProjection projection =
            CashFlowForecaster.Project(opening, from, to, items, recorded);

        // An occurrence already entered is in the register and therefore in the opening
        // balance; projecting it again would count the same payment twice.
        var alreadyHandled = settled
            .SelectMany(group => group.Select(date => (Schedule: group.Key, Date: date)))
            .ToHashSet();

        if (alreadyHandled.Count == 0)
        {
            return projection;
        }

        List<ForecastEvent> kept =
        [
            .. projection.Events.Where(e => !alreadyHandled.Contains((e.ScheduleId, e.Date)))
        ];

        return Rebuild(projection, kept);
    }

    /// <summary>
    /// Recomputes a projection from a filtered event list.
    /// </summary>
    private static CashFlowProjection Rebuild(
        CashFlowProjection original,
        IReadOnlyList<ForecastEvent> events)
    {
        var points = new List<ForecastPoint>
        {
            new(original.From, original.OpeningBalance, Money.Zero, []),
        };

        Money running = original.OpeningBalance;

        foreach (IGrouping<DateOnly, ForecastEvent> day in events.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            Money change = Money.Sum(day.Select(e => e.Amount));
            running += change;
            points.Add(new ForecastPoint(day.Key, running, change, [.. day]));
        }

        return original with { Points = points, Events = events };
    }

    /// <summary>
    /// The due dates of a series that are still outstanding, oldest first.
    /// </summary>
    /// <remarks>
    /// Recomputed from the rule and filtered by what has been entered or skipped, rather
    /// than read from stored pending rows. The rule is the source of truth about when
    /// something falls due; the stored rows record only what was done about it.
    /// </remarks>
    private static List<DateOnly> OutstandingDueDates(
        ScheduledTransaction schedule,
        HashSet<DateOnly> settled,
        DateOnly through)
    {
        RecurrenceRule rule = ToRule(schedule);

        List<DateOnly> dates =
        [
            .. RecurrenceCalculator
                .Occurrences(rule, null, through)
                .Where(d => !settled.Contains(d))
                .Take(MaximumBacklog)
        ];

        return dates;
    }

    private static async Task<HashSet<DateOnly>> SettledDatesAsync(
        MyFinanceDbContext db,
        int scheduleId,
        CancellationToken cancellationToken)
    {
        List<DateOnly> dates = await db.ScheduleOccurrences
            .AsNoTracking()
            .Where(o => o.ScheduledTransactionId == scheduleId
                && o.State != ScheduleOccurrenceState.Pending)
            .Select(o => o.DueDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. dates];
    }

    private static async Task<DateOnly?> NextOutstandingAsync(
        MyFinanceDbContext db,
        ScheduledTransaction schedule,
        DateOnly through,
        CancellationToken cancellationToken)
    {
        HashSet<DateOnly> settled = await SettledDatesAsync(db, schedule.Id, cancellationToken)
            .ConfigureAwait(false);

        List<DateOnly> outstanding = OutstandingDueDates(schedule, settled, through);

        if (outstanding.Count > 0)
        {
            return outstanding[0];
        }

        // Nothing overdue, so offer the next one the pattern produces.
        RecurrenceRule rule = ToRule(schedule);

        for (DateOnly? candidate = RecurrenceCalculator.NextOnOrAfter(rule, through);
            candidate is DateOnly date;
            candidate = RecurrenceCalculator.NextAfter(rule, date))
        {
            if (!settled.Contains(date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes one occurrence into the register and records that it was entered.
    /// </summary>
    private static async Task<int> WriteOccurrenceAsync(
        MyFinanceDbContext db,
        ScheduledTransaction schedule,
        DateOnly dueDate,
        DateOnly postOn,
        Money? actualAmount,
        CancellationToken cancellationToken)
    {
        Money amount = actualAmount ?? schedule.Amount;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        int sequence = await NextSequenceAsync(db, schedule.AccountId, postOn, cancellationToken)
            .ConfigureAwait(false);

        var transaction = new Transaction
        {
            AccountId = schedule.AccountId,
            Date = postOn,
            PayeeId = schedule.PayeeId,
            Memo = schedule.Memo,
            Amount = amount,

            // Entered from a schedule, not seen on a statement — the bank has not
            // acknowledged it yet, which is exactly what Uncleared means.
            ClearedStatus = ClearedStatus.Uncleared,
            ScheduledTransactionId = schedule.Id,
            SequenceInDay = sequence,
            CreatedUtc = now,
            ModifiedUtc = now,
        };

        CopySplits(schedule, transaction, amount);

        ValidationResult validation = TransactionValidator.Validate(transaction);
        if (!validation.IsValid)
        {
            throw new BookValidationException(validation);
        }

        db.Transactions.Add(transaction);

        // Saved here rather than at the end so the occurrence can reference the row's id,
        // and so the intra-day sequence of the next one in a backlog sees this one.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.ScheduleOccurrences.Add(new ScheduleOccurrence
        {
            ScheduledTransactionId = schedule.Id,
            DueDate = dueDate,
            State = ScheduleOccurrenceState.Entered,
            TransactionId = transaction.Id,
            ActualAmount = actualAmount,
            ResolvedUtc = now,
        });

        return transaction.Id;
    }

    /// <summary>
    /// Copies the schedule's category template onto a generated transaction.
    /// </summary>
    /// <remarks>
    /// When the actual amount differs from the scheduled one, a single-category template
    /// simply takes the new figure. A split template is left alone and the difference put on
    /// the first line, because there is no way to know which of several categories the
    /// variation belongs to and silently rescaling them all would be a guess.
    /// </remarks>
    private static void CopySplits(ScheduledTransaction schedule, Transaction transaction, Money amount)
    {
        if (schedule.Splits.Count == 0)
        {
            transaction.Splits.Add(new TransactionSplit { CategoryId = null, Amount = amount, SortOrder = 0 });
            return;
        }

        int order = 0;

        foreach (ScheduledTransactionSplit split in schedule.Splits.OrderBy(s => s.SortOrder))
        {
            transaction.Splits.Add(new TransactionSplit
            {
                CategoryId = split.CategoryId,
                Amount = split.Amount,
                Memo = split.Memo,
                SortOrder = order++,
            });
        }

        Money assigned = Money.Sum(transaction.Splits.Select(s => s.Amount));

        if (assigned == amount)
        {
            return;
        }

        TransactionSplit first = transaction.Splits.First();

        if (transaction.Splits.Count == 1)
        {
            first.Amount = amount;
            return;
        }

        first.Amount += amount - assigned;
    }

    private static async Task<int> NextSequenceAsync(
        MyFinanceDbContext db,
        int accountId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        int highest = await db.Transactions
            .Where(t => t.AccountId == accountId && t.Date == date)
            .Select(t => (int?)t.SequenceInDay)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        return highest + 1;
    }

    private static async Task<IReadOnlyDictionary<int, Money>> LoadBalancesAsync(
        MyFinanceDbContext db,
        CancellationToken cancellationToken)
    {
        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Transaction> transactions = await db.Transactions
            .AsNoTracking()
            .Where(t => !t.IsVoid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accounts.ToDictionary(
            a => a.Id,
            a => BalanceCalculator.CurrentBalance(
                a.OpeningBalance,
                transactions.Where(t => t.AccountId == a.Id)));
    }

    internal static RecurrenceRule ToRule(ScheduledTransaction schedule) => new()
    {
        Frequency = schedule.Frequency,
        Interval = Math.Max(1, schedule.Interval),
        StartDate = schedule.StartDate,
        SecondDayOfMonth = schedule.SecondDayOfMonth,
        WeekendShift = schedule.WeekendShift,
        EndKind = schedule.EndKind,
        EndDate = schedule.EndDate,
        OccurrenceCount = schedule.OccurrenceCount,
    };

    private static void ApplySplits(ScheduledTransaction schedule, ScheduleDraft draft)
    {
        schedule.Splits.Clear();

        int order = 0;

        foreach (SplitDraft split in draft.Splits)
        {
            schedule.Splits.Add(new ScheduledTransactionSplit
            {
                CategoryId = split.CategoryId,
                Amount = split.Amount,
                Memo = Trim(split.Memo),
                SortOrder = order++,
            });
        }
    }

    private static void Guard(ScheduleDraft draft)
    {
        if (draft.Amount.IsZero && !draft.IsEstimate)
        {
            throw new BookValidationException(
                AmountRequired,
                "A scheduled bill needs an amount. Mark it as an estimate if the figure varies.");
        }

        if (draft.Splits.Count > 0)
        {
            Money assigned = Money.Sum(draft.Splits.Select(s => s.Amount));

            if (assigned != draft.Amount)
            {
                throw new BookValidationException(
                    SplitsDoNotSum,
                    $"The categories total {assigned.ToAccountingString()} but the bill is {draft.Amount.ToAccountingString()}.");
            }
        }
    }

    private static async Task<ScheduledTransaction> LoadForEntryAsync(
        MyFinanceDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        ScheduledTransaction schedule = await db.ScheduledTransactions
            .Include(s => s.Splits)
            .Include(s => s.Payee)
            .Include(s => s.Account)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BookValidationException(NotFound, "That bill no longer exists.");

        if (schedule.Account?.IsReadOnly ?? false)
        {
            throw new BookValidationException(
                AccountReadOnly,
                $"\"{schedule.Account.Name}\" cannot be written to.");
        }

        return schedule;
    }

    private static async Task<ScheduledTransaction> RequireAsync(
        MyFinanceDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        ScheduledTransaction? schedule = await db.ScheduledTransactions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return schedule ?? throw new BookValidationException(NotFound, "That bill no longer exists.");
    }

    private static async Task GuardAccountAsync(
        MyFinanceDbContext db,
        int accountId,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            throw new BookValidationException(AccountNotFound, "That account no longer exists.");
        }

        if (account.IsReadOnly)
        {
            throw new BookValidationException(
                AccountReadOnly,
                $"\"{account.Name}\" cannot have bills scheduled against it.");
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
