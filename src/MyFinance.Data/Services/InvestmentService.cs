using Microsoft.EntityFrameworkCore;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Investments;
using MyFinance.Core.Primitives;
using SecurityEntity = MyFinance.Core.Entities.Security;

namespace MyFinance.Data.Services;

/// <summary>What is being recorded against an investment account.</summary>
public sealed class InvestmentDraft
{
    public required int AccountId { get; set; }

    public required int SecurityId { get; set; }

    public DateOnly Date { get; set; }

    public InvestmentActivity Activity { get; set; }

    /// <summary>Units bought or sold. Zero for a cash dividend or a fee.</summary>
    public Quantity Quantity { get; set; }

    public Money PricePerUnit { get; set; }

    /// <summary>Cash moved, as a positive figure. The direction comes from the activity.</summary>
    public Money Amount { get; set; }

    public Money Fees { get; set; }

    public string? Memo { get; set; }
}

/// <summary>One holding, with what it is worth and on what evidence.</summary>
public sealed record HoldingSummary(
    int SecurityId,
    string SecurityName,
    string? Symbol,
    Quantity Quantity,
    Money CostBasis,
    HoldingValue Value)
{
    public Money AverageCost => Quantity.IsZero
        ? Money.Zero
        : CostBasis * (1m / Quantity.ToDecimal());
}

/// <summary>
/// Buying, selling and valuing what an investment account holds.
/// </summary>
/// <remarks>
/// <para>
/// The cash side of every activity is written as an <b>ordinary register transaction</b>
/// through <see cref="RegisterService"/>, so it inherits sequencing, the running balance and
/// the split invariant without any of them being restated here. Only the holding side is new.
/// That is what stops this becoming a second ledger whose rules drift from the first.
/// </para>
/// <para>
/// Cost is tracked as an average per holding, not per lot — see
/// `specs/013-investment-accounts`.
/// </para>
/// </remarks>
public sealed class InvestmentService
{
    private readonly IBookContextFactory _factory;
    private readonly RegisterService _register;

    public InvestmentService(IBookContextFactory factory, RegisterService register)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(register);

        _factory = factory;
        _register = register;
    }

    /// <summary>Finds a security by name, creating it on first use.</summary>
    public async Task<int> FindOrCreateSecurityAsync(
        string name,
        string? symbol = null,
        SecurityType type = SecurityType.Share,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using MyFinanceDbContext db = _factory.CreateContext();

        string trimmed = name.Trim();

        SecurityEntity? existing = await db.Securities
            .FirstOrDefaultAsync(s => s.Name == trimmed, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var created = new SecurityEntity { Name = trimmed, Symbol = symbol, Type = type };
        db.Securities.Add(created);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    /// <summary>Records a price for a security on a day, replacing any already there.</summary>
    public async Task SetPriceAsync(
        int securityId,
        DateOnly asOf,
        Money price,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        SecurityPrice? existing = await db.SecurityPrices
            .FirstOrDefaultAsync(p => p.SecurityId == securityId && p.AsOf == asOf, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.SecurityPrices.Add(new SecurityPrice { SecurityId = securityId, AsOf = asOf, Price = price });
        }
        else
        {
            existing.Price = price;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records an activity: updates the holding, and writes the cash leg into the register.
    /// </summary>
    public async Task<int> RecordAsync(InvestmentDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Account account;
        SecurityEntity security;

        await using (MyFinanceDbContext db = _factory.CreateContext())
        {
            account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.AccountId, cancellationToken)
                          .ConfigureAwait(false)
                      ?? throw new BookValidationException("investment.account_missing", "That account no longer exists.");

            security = await db.Securities.FirstOrDefaultAsync(s => s.Id == draft.SecurityId, cancellationToken)
                           .ConfigureAwait(false)
                       ?? throw new BookValidationException("investment.security_missing", "That security no longer exists.");
        }

        if (account.Type != AccountType.Brokerage)
        {
            throw new BookValidationException(
                "investment.not_an_investment_account",
                $"\"{account.Name}\" is not an investment account, so it cannot hold securities.");
        }

        // The cash leg first, through the ordinary register path: it is a real transaction and
        // must be sequenced, balanced and split like any other.
        Money cash = CashMovement(draft);

        int cashId = await _register.SaveAsync(
            new TransactionDraft
            {
                AccountId = draft.AccountId,
                Date = draft.Date,
                Amount = cash,
                PayeeName = security.Name,
                Memo = draft.Memo ?? Describe(draft, security),
            },
            cancellationToken).ConfigureAwait(false);

        await using MyFinanceDbContext write = _factory.CreateContext();
        await using var scope = await write.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Holding? holding = await write.Holdings
            .FirstOrDefaultAsync(h => h.AccountId == draft.AccountId && h.SecurityId == draft.SecurityId, cancellationToken)
            .ConfigureAwait(false);

        if (holding is null)
        {
            holding = new Holding
            {
                AccountId = draft.AccountId,
                SecurityId = draft.SecurityId,
                Quantity = Quantity.Zero,
                CostBasis = Money.Zero,
            };

            write.Holdings.Add(holding);
        }

        var state = new HoldingState(holding.Quantity, holding.CostBasis);

        state = draft.Activity switch
        {
            InvestmentActivity.Buy or InvestmentActivity.Reinvestment =>
                HoldingCalculator.Buy(state, draft.Quantity, draft.Amount.Abs() + draft.Fees),

            InvestmentActivity.Sell =>
                Sell(state, draft),

            // A cash dividend or a fee moves money without moving units.
            _ => state,
        };

        holding.Quantity = state.Quantity;
        holding.CostBasis = state.CostBasis;

        var record = new InvestmentTransaction
        {
            AccountId = draft.AccountId,
            SecurityId = draft.SecurityId,
            Date = draft.Date,
            Activity = draft.Activity,
            Quantity = draft.Quantity,
            PricePerUnit = draft.PricePerUnit,
            Amount = cash,
            Fees = draft.Fees,
            Memo = draft.Memo,
            CashTransactionId = cashId,
        };

        write.InvestmentTransactions.Add(record);

        await write.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(cancellationToken).ConfigureAwait(false);

        return record.Id;
    }

    private static HoldingState Sell(HoldingState state, InvestmentDraft draft)
    {
        if (draft.Quantity > state.Quantity)
        {
            throw new BookValidationException(
                "investment.oversold",
                $"Cannot sell {draft.Quantity} units — only {state.Quantity} are held.");
        }

        return HoldingCalculator.Sell(state, draft.Quantity).Remaining;
    }

    /// <summary>
    /// Which way the cash moves. Buying and paying a fee take money out; selling and a
    /// dividend bring it in. A reinvestment moves no cash at all — the dividend buys units
    /// instead of arriving.
    /// </summary>
    private static Money CashMovement(InvestmentDraft draft) => draft.Activity switch
    {
        InvestmentActivity.Buy => (draft.Amount.Abs() + draft.Fees).Negated(),
        InvestmentActivity.Fee => draft.Amount.Abs().Negated(),
        InvestmentActivity.Sell => draft.Amount.Abs() - draft.Fees,
        InvestmentActivity.Dividend => draft.Amount.Abs(),
        InvestmentActivity.Reinvestment => Money.Zero,
        _ => Money.Zero,
    };

    private static string Describe(InvestmentDraft draft, SecurityEntity security) => draft.Activity switch
    {
        InvestmentActivity.Buy => $"Bought {draft.Quantity} {security.Name}",
        InvestmentActivity.Sell => $"Sold {draft.Quantity} {security.Name}",
        InvestmentActivity.Dividend => $"Dividend from {security.Name}",
        InvestmentActivity.Reinvestment => $"Reinvested {draft.Quantity} {security.Name}",
        InvestmentActivity.Fee => $"Fee on {security.Name}",
        _ => security.Name,
    };

    /// <summary>
    /// What an account holds, valued as at a date.
    /// </summary>
    /// <remarks>
    /// Every value carries the date of the price behind it, and one with no price at all is
    /// carried at cost and says so. A figure whose basis the reader cannot see is one they
    /// have to check from scratch.
    /// </remarks>
    public async Task<IReadOnlyList<HoldingSummary>> GetHoldingsAsync(
        int accountId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<Holding> holdings = await db.Holdings
            .AsNoTracking()
            .Include(h => h.Security)
            .Where(h => h.AccountId == accountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var securityIds = holdings.Select(h => h.SecurityId).ToList();

        List<SecurityPrice> prices = await db.SecurityPrices
            .AsNoTracking()
            .Where(p => securityIds.Contains(p.SecurityId) && p.AsOf <= asOf)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. holdings
                .Select(h => new HoldingSummary(
                    h.SecurityId,
                    h.Security?.Name ?? "(unknown)",
                    h.Security?.Symbol,
                    h.Quantity,
                    h.CostBasis,
                    HoldingCalculator.Value(
                        new HoldingState(h.Quantity, h.CostBasis),
                        prices.Where(p => p.SecurityId == h.SecurityId),
                        asOf)))
                .OrderBy(h => h.SecurityName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// What every holding in the book is worth, for the net-worth figure.
    /// </summary>
    public async Task<IReadOnlyList<HoldingSummary>> GetAllHoldingsAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        await using MyFinanceDbContext db = _factory.CreateContext();

        List<int> accounts = await db.Accounts
            .Where(a => a.Type == AccountType.Brokerage)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var all = new List<HoldingSummary>();

        foreach (int accountId in accounts)
        {
            all.AddRange(await GetHoldingsAsync(accountId, asOf, cancellationToken).ConfigureAwait(false));
        }

        return all;
    }
}
