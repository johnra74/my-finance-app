using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Export;

/// <summary>
/// How an amount is written: twice, side by side, on purpose.
/// </summary>
/// <remarks>
/// <para>
/// Every amount appears as a pair of sibling properties — <c>"amount": "-123.45"</c> beside
/// <c>"amountMinorUnits": -12345</c>. The string is what a person and a spreadsheet read; the
/// integer is what a careful program reads. Neither alone is sufficient.
/// </para>
/// <para>
/// Written instead as a bare JSON number, <c>-123.45</c> becomes a binary double in most
/// readers, and the exactness this whole application is built on would be lost at the very
/// last step. They are siblings rather than a nested object because a document is read by
/// people, and <c>"amount": { "amount": … }</c> is not a thing anyone should have to look at.
/// </para>
/// </remarks>
public static class ExportMoney
{
    public static string Text(Money money) =>
        money.ToDecimal().ToString("0.00", CultureInfo.InvariantCulture);

    public static string? Text(Money? money) => money is null ? null : Text(money.Value);

    public static long? Minor(Money? money) => money?.MinorUnits;

    public static Money Parse(long minorUnits) => Money.FromMinorUnits(minorUnits);
}

/// <summary>Who wrote the document, and when.</summary>
public sealed record ExportApplication(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version);

public sealed record ExportAccount
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("type")] public required string Type { get; init; }

    [JsonPropertyName("group")] public required string Group { get; init; }

    [JsonPropertyName("institution")] public string? Institution { get; init; }

    /// <summary>
    /// The display mask only, e.g. <c>XXXXXXXX6464</c>.
    /// </summary>
    /// <remarks>
    /// The book never holds a full account number, so the export cannot reintroduce one. The
    /// OFX account digest is not exported either: it is a keyed value with no meaning outside
    /// this book, so writing it out would export a secret to no purpose.
    /// </remarks>
    [JsonPropertyName("accountNumberMasked")] public string? AccountNumberMasked { get; init; }

    [JsonPropertyName("openingBalance")] public required string OpeningBalance { get; init; }

    [JsonPropertyName("openingBalanceMinorUnits")] public required long OpeningBalanceMinorUnits { get; init; }

    [JsonPropertyName("openedOn")] public string? OpenedOn { get; init; }

    [JsonPropertyName("currencyCode")] public required string CurrencyCode { get; init; }

    [JsonPropertyName("isClosed")] public bool IsClosed { get; init; }

    [JsonPropertyName("isFavorite")] public bool IsFavorite { get; init; }

    [JsonPropertyName("sortOrder")] public int SortOrder { get; init; }

    [JsonPropertyName("lastReconciledOn")] public string? LastReconciledOn { get; init; }

    [JsonPropertyName("lastReconciledBalance")] public string? LastReconciledBalance { get; init; }

    [JsonPropertyName("lastReconciledBalanceMinorUnits")] public long? LastReconciledBalanceMinorUnits { get; init; }

    [JsonPropertyName("notes")] public string? Notes { get; init; }
}

public sealed record ExportCategory
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    /// <summary>Leaf name only.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>Rendered path, for a reader who does not want to walk the tree.</summary>
    [JsonPropertyName("fullName")] public required string FullName { get; init; }

    [JsonPropertyName("parentId")] public int? ParentId { get; init; }

    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("isTaxRelated")] public bool IsTaxRelated { get; init; }

    /// <summary>Archived categories are exported. A file that omits history is not an export.</summary>
    [JsonPropertyName("isArchived")] public bool IsArchived { get; init; }

    [JsonPropertyName("sortOrder")] public int SortOrder { get; init; }
}

public sealed record ExportPayee
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("normalizedName")] public required string NormalizedName { get; init; }

    /// <summary>
    /// What this payee was last filed under — decades of decisions, and one of the things in
    /// this document worth most.
    /// </summary>
    [JsonPropertyName("lastCategoryId")] public int? LastCategoryId { get; init; }

    [JsonPropertyName("lastAmount")] public string? LastAmount { get; init; }

    [JsonPropertyName("lastAmountMinorUnits")] public long? LastAmountMinorUnits { get; init; }

    [JsonPropertyName("isActive")] public bool IsActive { get; init; }

    [JsonPropertyName("notes")] public string? Notes { get; init; }

    /// <summary>Descriptors that should resolve to this payee on a future import.</summary>
    [JsonPropertyName("aliases")] public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed record ExportSplit
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("categoryId")] public int? CategoryId { get; init; }

    [JsonPropertyName("amount")] public required string Amount { get; init; }

    [JsonPropertyName("amountMinorUnits")] public required long AmountMinorUnits { get; init; }

    [JsonPropertyName("memo")] public string? Memo { get; init; }

    [JsonPropertyName("sortOrder")] public int SortOrder { get; init; }
}

public sealed record ExportTransaction
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("accountId")] public required int AccountId { get; init; }

    [JsonPropertyName("date")] public required string Date { get; init; }

    [JsonPropertyName("sequenceInDay")] public int SequenceInDay { get; init; }

    [JsonPropertyName("amount")] public required string Amount { get; init; }

    [JsonPropertyName("amountMinorUnits")] public required long AmountMinorUnits { get; init; }

    [JsonPropertyName("payeeId")] public int? PayeeId { get; init; }

    [JsonPropertyName("memo")] public string? Memo { get; init; }

    [JsonPropertyName("number")] public string? Number { get; init; }

    [JsonPropertyName("clearedStatus")] public required string ClearedStatus { get; init; }

    [JsonPropertyName("isVoid")] public bool IsVoid { get; init; }

    /// <summary>
    /// The other leg of a transfer, named rather than inferred.
    /// </summary>
    /// <remarks>
    /// This is the field that ruled out QIF as an export format: QIF describes one account at
    /// a time and cannot express a link across a book, so a reader would have to guess which
    /// rows pair up from their amounts and dates.
    /// </remarks>
    [JsonPropertyName("transferPeerId")] public int? TransferPeerId { get; init; }

    [JsonPropertyName("importBatchId")] public int? ImportBatchId { get; init; }

    [JsonPropertyName("scheduledTransactionId")] public int? ScheduledTransactionId { get; init; }

    /// <summary>Nested, because a split has no meaning apart from its transaction.</summary>
    [JsonPropertyName("splits")] public IReadOnlyList<ExportSplit> Splits { get; init; } = [];
}

public sealed record ExportScheduleOccurrence
{
    [JsonPropertyName("dueDate")] public required string DueDate { get; init; }

    [JsonPropertyName("state")] public required string State { get; init; }

    [JsonPropertyName("transactionId")] public int? TransactionId { get; init; }

    [JsonPropertyName("actualAmount")] public string? ActualAmount { get; init; }

    [JsonPropertyName("actualAmountMinorUnits")] public long? ActualAmountMinorUnits { get; init; }
}

public sealed record ExportScheduled
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("accountId")] public required int AccountId { get; init; }

    [JsonPropertyName("payeeId")] public int? PayeeId { get; init; }

    [JsonPropertyName("memo")] public string? Memo { get; init; }

    [JsonPropertyName("amount")] public required string Amount { get; init; }

    [JsonPropertyName("amountMinorUnits")] public required long AmountMinorUnits { get; init; }

    [JsonPropertyName("isEstimate")] public bool IsEstimate { get; init; }

    [JsonPropertyName("paymentMethod")] public required string PaymentMethod { get; init; }

    [JsonPropertyName("frequency")] public required string Frequency { get; init; }

    [JsonPropertyName("interval")] public int Interval { get; init; }

    [JsonPropertyName("startDate")] public required string StartDate { get; init; }

    [JsonPropertyName("endKind")] public required string EndKind { get; init; }

    [JsonPropertyName("endDate")] public string? EndDate { get; init; }

    [JsonPropertyName("occurrenceCount")] public int? OccurrenceCount { get; init; }

    [JsonPropertyName("secondDayOfMonth")] public int? SecondDayOfMonth { get; init; }

    [JsonPropertyName("weekendShift")] public required string WeekendShift { get; init; }

    [JsonPropertyName("autoEnter")] public bool AutoEnter { get; init; }

    [JsonPropertyName("daysAheadToEnter")] public int DaysAheadToEnter { get; init; }

    [JsonPropertyName("isActive")] public bool IsActive { get; init; }

    [JsonPropertyName("splits")] public IReadOnlyList<ExportSplit> Splits { get; init; } = [];

    /// <summary>
    /// Which due dates were entered or skipped.
    /// </summary>
    /// <remarks>
    /// Not decoration: without it, a reconstructed book would not know which occurrences had
    /// already been dealt with, and would enter every backdated bill a second time.
    /// </remarks>
    [JsonPropertyName("occurrences")] public IReadOnlyList<ExportScheduleOccurrence> Occurrences { get; init; } = [];
}

public sealed record ExportBudgetLine
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("categoryId")] public required int CategoryId { get; init; }

    [JsonPropertyName("periodStart")] public required string PeriodStart { get; init; }

    [JsonPropertyName("periodType")] public required string PeriodType { get; init; }

    [JsonPropertyName("amount")] public required string Amount { get; init; }

    [JsonPropertyName("amountMinorUnits")] public required long AmountMinorUnits { get; init; }

    [JsonPropertyName("rollsOver")] public bool RollsOver { get; init; }

    [JsonPropertyName("notes")] public string? Notes { get; init; }
}

public sealed record ExportBudget
{
    [JsonPropertyName("lines")] public IReadOnlyList<ExportBudgetLine> Lines { get; init; } = [];

    [JsonPropertyName("watchedCategoryIds")] public IReadOnlyList<int> WatchedCategoryIds { get; init; } = [];
}

public sealed record ExportRule
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("priority")] public int Priority { get; init; }

    [JsonPropertyName("matchField")] public required string MatchField { get; init; }

    [JsonPropertyName("matchKind")] public required string MatchKind { get; init; }

    [JsonPropertyName("pattern")] public required string Pattern { get; init; }

    [JsonPropertyName("isCaseSensitive")] public bool IsCaseSensitive { get; init; }

    [JsonPropertyName("accountId")] public int? AccountId { get; init; }

    [JsonPropertyName("targetCategoryId")] public int? TargetCategoryId { get; init; }

    [JsonPropertyName("targetPayeeId")] public int? TargetPayeeId { get; init; }

    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; init; }
}

public sealed record ExportMerchantCode
{
    [JsonPropertyName("code")] public required string Code { get; init; }

    [JsonPropertyName("categoryId")] public required int CategoryId { get; init; }
}

public sealed record ExportImportBatch
{
    [JsonPropertyName("id")] public required int Id { get; init; }

    [JsonPropertyName("sourceFileName")] public string? SourceFileName { get; init; }

    [JsonPropertyName("format")] public required string Format { get; init; }

    [JsonPropertyName("accountId")] public int? AccountId { get; init; }

    [JsonPropertyName("importedUtc")] public required string ImportedUtc { get; init; }

    [JsonPropertyName("transactionsAdded")] public int TransactionsAdded { get; init; }

    [JsonPropertyName("isReverted")] public bool IsReverted { get; init; }
}

/// <summary>
/// A whole book, in one file that can be read without this application.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Money's file format is the reason this application exists; nothing produced here
/// may be readable only from inside it. Report-level CSV export keeps half of that promise —
/// this document keeps the other half.
/// </para>
/// <para>
/// <b>The shape is an interface from the day it ships.</b> It carries
/// <see cref="FormatVersion"/> from version 1 so a reader can refuse what it does not
/// understand, the same rule the key sidecar already applies. It lives in
/// <c>MyFinance.Core</c> rather than in the data layer so it is ours to version deliberately,
/// rather than being a reflection of the database schema.
/// </para>
/// <para>
/// Every id referenced anywhere resolves to an entity present in the document. A published
/// format may not carry a dangling reference — which is why import batches are here at all,
/// rather than being dismissed as internal undo machinery.
/// </para>
/// </remarks>
public sealed record BookDocument
{
    public const string FormatName = "myfinance-book";

    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("format")] public string Format { get; init; } = FormatName;

    [JsonPropertyName("formatVersion")] public int FormatVersion { get; init; } = CurrentFormatVersion;

    [JsonPropertyName("exportedUtc")] public required string ExportedUtc { get; init; }

    [JsonPropertyName("application")] public required ExportApplication Application { get; init; }

    [JsonPropertyName("accounts")] public IReadOnlyList<ExportAccount> Accounts { get; init; } = [];

    [JsonPropertyName("categories")] public IReadOnlyList<ExportCategory> Categories { get; init; } = [];

    [JsonPropertyName("payees")] public IReadOnlyList<ExportPayee> Payees { get; init; } = [];

    [JsonPropertyName("transactions")] public IReadOnlyList<ExportTransaction> Transactions { get; init; } = [];

    [JsonPropertyName("scheduled")] public IReadOnlyList<ExportScheduled> Scheduled { get; init; } = [];

    [JsonPropertyName("budgets")] public ExportBudget Budgets { get; init; } = new();

    [JsonPropertyName("rules")] public IReadOnlyList<ExportRule> Rules { get; init; } = [];

    [JsonPropertyName("merchantCodes")] public IReadOnlyList<ExportMerchantCode> MerchantCodes { get; init; } = [];

    [JsonPropertyName("importBatches")] public IReadOnlyList<ExportImportBatch> ImportBatches { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, BookDocumentJson.Default.BookDocument);

    public static BookDocument FromJson(string json)
    {
        BookDocument? parsed = JsonSerializer.Deserialize(json, BookDocumentJson.Default.BookDocument);

        if (parsed is null)
        {
            throw new InvalidDataException("The export file is empty or malformed.");
        }

        if (!string.Equals(parsed.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"This is not a MyFinance book export (format '{parsed.Format}').");
        }

        if (parsed.FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"This export is version {parsed.FormatVersion}; this build reads up to {CurrentFormatVersion}.");
        }

        return parsed;
    }
}

/// <summary>
/// Source-generated serialization, so the single-file build resolves nothing by reflection.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BookDocument))]
public partial class BookDocumentJson : JsonSerializerContext;
