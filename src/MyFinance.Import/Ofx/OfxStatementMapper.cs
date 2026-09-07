using MyFinance.Core.Enums;
using MyFinance.Import.Model;
using MyFinance.Import.Ofx.Model;

namespace MyFinance.Import.Ofx;

/// <summary>
/// Translates the OFX-shaped reading of a file into the format-neutral one everything
/// downstream works with.
/// </summary>
/// <remarks>
/// Kept as a separate step rather than having the reader produce the neutral shape directly,
/// so <see cref="OfxStatement"/> stays a faithful description of what OFX actually says. The
/// two models answer different questions: one documents the format, the other documents what
/// this application imports.
/// </remarks>
public static class OfxStatementMapper
{
    public static ImportedFile ToImported(OfxParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ImportedFile
        {
            Format = ImportFormat.Ofx,
            Statements = [.. result.Statements.Select(ToImported)],
            Diagnostics = result.Diagnostics,
        };
    }

    public static ImportedStatement ToImported(OfxStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        return new ImportedStatement
        {
            Format = ImportFormat.Ofx,
            Account = new ImportedAccountInfo(
                statement.Account.Kind switch
                {
                    OfxAccountKind.CreditCard => ImportedAccountKind.CreditCard,
                    OfxAccountKind.Investment => ImportedAccountKind.Investment,
                    _ => ImportedAccountKind.Bank,
                },
                statement.Account.BankId,
                statement.Account.AccountId,
                Name: null,
                statement.Account.MappedType),
            CurrencyCode = statement.CurrencyCode,
            PeriodStart = statement.PeriodStart,
            PeriodEnd = statement.PeriodEnd,
            Transactions = [.. statement.Transactions.Select(ToImported)],
            LedgerBalance = statement.LedgerBalance is OfxBalance ledger
                ? new ImportedBalance(ledger.Amount, ledger.AsOf)
                : null,
            AvailableBalance = statement.AvailableBalance is OfxBalance available
                ? new ImportedBalance(available.Amount, available.AsOf)
                : null,
            Institution = statement.Institution,
        };
    }

    private static ImportedTransaction ToImported(OfxTransaction transaction) =>
        new()
        {
            ExternalId = transaction.FitId,
            TransactionType = transaction.TransactionType,
            Posted = transaction.Posted,
            Amount = transaction.Amount,
            Name = transaction.Name,
            Memo = transaction.Memo,
            CheckNumber = transaction.CheckNumber,

            // OFX has no per-row cleared flag, and does not need one: a downloaded item has
            // reached the bank by definition, which is exactly what Cleared means here.
            Cleared = ClearedStatus.Cleared,
            CorrectsExternalId = transaction.CorrectsFitId,
            MerchantCode = transaction.Sic,
        };
}
