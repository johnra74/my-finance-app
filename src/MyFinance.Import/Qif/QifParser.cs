using System.Globalization;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;

namespace MyFinance.Import.Qif;

/// <summary>Codes the QIF reader attaches to its diagnostics.</summary>
public static class QifDiagnostic
{
    public const string NoRecords = "qif.no_records";
    public const string UnreadableDate = "qif.bad_date";
    public const string UnreadableAmount = "qif.bad_amount";
    public const string AmbiguousDates = "qif.ambiguous_dates";
    public const string ConflictingDates = "qif.conflicting_dates";
    public const string InvestmentUnsupported = "qif.investment_unsupported";
    public const string SplitMismatch = "qif.split_mismatch";
    public const string TransferKept = "qif.transfer_kept";
    public const string UnknownSection = "qif.unknown_section";
}

/// <summary>The file is not QIF at all.</summary>
public sealed class QifParseException : Exception
{
    public QifParseException(string message)
        : base(message)
    {
    }
}

/// <summary>A category the file's own category list defined.</summary>
/// <param name="Path">Full path as written, e.g. "Food:Coffee".</param>
/// <param name="Description">Free-text description, when given.</param>
/// <param name="IsIncome">True when the file marked it as income.</param>
/// <param name="IsTaxRelated">True when the file marked it tax related.</param>
public sealed record QifCategoryDefinition(
    string Path,
    string? Description,
    bool IsIncome,
    bool IsTaxRelated);

/// <summary>
/// Reads Quicken Interchange Format files.
/// </summary>
/// <remarks>
/// <para>
/// QIF is a line-oriented format in which each line begins with a single-letter code and a
/// record ends with <c>^</c>. It carries no version, no encoding declaration and no locale,
/// so the file is read in two passes: the first collects every raw date and amount to settle
/// what the file's conventions are, and the second builds the transactions using them.
/// </para>
/// <para>
/// What QIF has that OFX does not is the user's own categorization — which is the reason to
/// support it at all, since it is how a ledger leaves an older program. What it lacks is any
/// unique transaction id, so duplicate detection against a QIF file can only ever be
/// approximate.
/// </para>
/// </remarks>
public static class QifParser
{
    /// <summary>Section headers that introduce ordinary transaction records.</summary>
    private static readonly Dictionary<string, AccountType?> TransactionSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["!Type:Bank"] = AccountType.Checking,
            ["!Type:Cash"] = AccountType.Cash,
            ["!Type:CCard"] = AccountType.CreditCard,
            ["!Type:Oth A"] = null,
            ["!Type:Oth L"] = null,
        };

    public static ImportedFile ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path, DetectEncoding(path)));
    }

    public static ImportedFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines =
        [
            .. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0)
        ];

        if (lines.Count == 0)
        {
            throw new QifParseException("The file is empty, so it is not a QIF export.");
        }

        var diagnostics = new List<ImportDiagnostic>();

        // First pass: settle what "03/02/2026" and "1,234" mean in this particular file
        // before interpreting a single one of them.
        QifConventions conventions = QifConventionDetector.Detect(
            CollectValues(lines, 'D'),
            CollectValues(lines, 'T', 'U', '$'));

        if (conventions.DateOrderConflicted)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Warning,
                QifDiagnostic.ConflictingDates,
                "The dates in this file disagree about whether the day or the month comes first. Check the imported dates carefully."));
        }
        else if (conventions.DateOrderWasAmbiguous)
        {
            string assumed = conventions.EffectiveDateOrder == QifDateOrder.DayFirst
                ? "day before month"
                : "month before day";

            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Info,
                QifDiagnostic.AmbiguousDates,
                $"No date in this file has a day above the twelfth, so the order could not be established. It is being read {assumed}."));
        }

        var reader = new Reader(lines, conventions, diagnostics);
        reader.Run();

        if (reader.Transactions.Count == 0 && diagnostics.All(d => d.Severity != ImportSeverity.Error))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportSeverity.Error,
                QifDiagnostic.NoRecords,
                "This file contains no transactions."));
        }

        var statement = new ImportedStatement
        {
            Format = ImportFormat.Qif,
            Account = new ImportedAccountInfo(
                reader.AccountKind,
                BankId: null,
                AccountId: null,
                Name: reader.AccountName,
                MappedType: reader.AccountType),
            Transactions = reader.Transactions,
            PeriodStart = reader.Transactions.Count == 0 ? null : reader.Transactions.Min(t => t.Posted),
            PeriodEnd = reader.Transactions.Count == 0 ? null : reader.Transactions.Max(t => t.Posted),
        };

        return new ImportedFile
        {
            Format = ImportFormat.Qif,
            Statements = reader.Transactions.Count == 0 ? [] : [statement],
            Diagnostics = diagnostics,
        };
    }

    /// <summary>Categories the file's own <c>!Type:Cat</c> list defined, if it had one.</summary>
    public static IReadOnlyList<QifCategoryDefinition> ReadCategoryList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var definitions = new List<QifCategoryDefinition>();
        bool inCategorySection = false;

        string? name = null;
        string? description = null;
        bool isIncome = false;
        bool isTax = false;

        foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '!')
            {
                inCategorySection = line.StartsWith("!Type:Cat", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inCategorySection)
            {
                continue;
            }

            if (line[0] == '^')
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    definitions.Add(new QifCategoryDefinition(name.Trim(), description, isIncome, isTax));
                }

                name = description = null;
                isIncome = isTax = false;
                continue;
            }

            char code = line[0];
            string value = line[1..].Trim();

            switch (code)
            {
                case 'N':
                    name = value;
                    break;
                case 'D':
                    description = value;
                    break;
                case 'I':
                    isIncome = true;
                    break;
                case 'E':
                    isIncome = false;
                    break;
                case 'T':
                    isTax = true;
                    break;
                default:
                    break;
            }
        }

        return definitions;
    }

    private static IEnumerable<string> CollectValues(IReadOnlyList<string> lines, params char[] codes)
    {
        foreach (string line in lines)
        {
            if (line.Length > 1 && codes.Contains(line[0]))
            {
                yield return line[1..].Trim();
            }
        }
    }

    /// <summary>
    /// Picks an encoding for a QIF file, which declares none.
    /// </summary>
    /// <remarks>
    /// A byte-order mark is honoured if present. Otherwise the file is read as Windows-1252,
    /// the same reasoning as for OFX: it is a strict superset of ASCII, so nothing is lost
    /// for a plain file, and accented merchant names survive.
    /// </remarks>
    private static System.Text.Encoding DetectEncoding(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        int read = stream.Read(head);

        if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return System.Text.Encoding.UTF8;
        }

        if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            return System.Text.Encoding.Unicode;
        }

        return Ofx.OfxEncodingSupport.WindowsLatin1;
    }

    /// <summary>Walks the lines once, assembling records as it goes.</summary>
    private sealed class Reader
    {
        private readonly IReadOnlyList<string> _lines;
        private readonly QifConventions _conventions;
        private readonly List<ImportDiagnostic> _diagnostics;

        private bool _inTransactionSection;
        private bool _inAccountSection;
        private bool _warnedAboutInvestments;

        public Reader(
            IReadOnlyList<string> lines,
            QifConventions conventions,
            List<ImportDiagnostic> diagnostics)
        {
            _lines = lines;
            _conventions = conventions;
            _diagnostics = diagnostics;
        }

        public List<ImportedTransaction> Transactions { get; } = [];

        public string? AccountName { get; private set; }

        public AccountType? AccountType { get; private set; }

        public ImportedAccountKind AccountKind { get; private set; } = ImportedAccountKind.Unknown;

        public void Run()
        {
            var record = new RecordBuilder();

            foreach (string line in _lines)
            {
                if (line[0] == '!')
                {
                    ApplySectionHeader(line);
                    record.Reset();
                    continue;
                }

                if (line[0] == '^')
                {
                    Commit(record);
                    record.Reset();
                    continue;
                }

                if (_inAccountSection)
                {
                    ReadAccountLine(line);
                    continue;
                }

                if (_inTransactionSection)
                {
                    record.Add(line[0], line.Length > 1 ? line[1..].Trim() : string.Empty);
                }
            }

            // Real exports routinely omit the final caret.
            Commit(record);
        }

        private void ApplySectionHeader(string line)
        {
            string header = line.Trim();

            _inAccountSection = header.StartsWith("!Account", StringComparison.OrdinalIgnoreCase);

            if (_inAccountSection)
            {
                _inTransactionSection = false;
                return;
            }

            foreach ((string name, AccountType? type) in TransactionSections)
            {
                if (header.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    _inTransactionSection = true;

                    AccountType ??= type;
                    AccountKind = type == Core.Enums.AccountType.CreditCard
                        ? ImportedAccountKind.CreditCard
                        : ImportedAccountKind.Bank;

                    return;
                }
            }

            _inTransactionSection = false;

            if (header.StartsWith("!Type:Invst", StringComparison.OrdinalIgnoreCase)
                && !_warnedAboutInvestments)
            {
                _warnedAboutInvestments = true;

                // Same reasoning as for OFX: importing the cash legs while dropping every buy
                // and sell would produce a register that looks complete and is wrong.
                _diagnostics.Add(new ImportDiagnostic(
                    ImportSeverity.Warning,
                    QifDiagnostic.InvestmentUnsupported,
                    "This file contains investment transactions, which MyFinance cannot import yet. They were skipped."));
            }
        }

        private void ReadAccountLine(string line)
        {
            char code = line[0];
            string value = line.Length > 1 ? line[1..].Trim() : string.Empty;

            switch (code)
            {
                case 'N':
                    AccountName ??= value;
                    break;

                case 'T':
                    AccountType ??= value.ToUpperInvariant() switch
                    {
                        "BANK" => Core.Enums.AccountType.Checking,
                        "CASH" => Core.Enums.AccountType.Cash,
                        "CCARD" => Core.Enums.AccountType.CreditCard,
                        _ => null,
                    };

                    if (AccountType == Core.Enums.AccountType.CreditCard)
                    {
                        AccountKind = ImportedAccountKind.CreditCard;
                    }

                    break;

                default:
                    break;
            }
        }

        private void Commit(RecordBuilder record)
        {
            if (!record.HasContent)
            {
                return;
            }

            if (!TryReadDate(record.Date, out DateOnly posted))
            {
                _diagnostics.Add(new ImportDiagnostic(
                    ImportSeverity.Warning,
                    QifDiagnostic.UnreadableDate,
                    $"Skipped a record with an unreadable date ({record.Date ?? "none given"})."));
                return;
            }

            if (!TryReadAmount(record.Amount, out Money amount))
            {
                _diagnostics.Add(new ImportDiagnostic(
                    ImportSeverity.Warning,
                    QifDiagnostic.UnreadableAmount,
                    $"Skipped a record dated {posted:d} with an unreadable amount ({record.Amount ?? "none given"})."));
                return;
            }

            string? category = record.Category;
            string? transferAccount = null;

            // "L[Savings]" marks a transfer to another of the user's accounts.
            if (category is not null && category.StartsWith('[') && category.EndsWith(']'))
            {
                transferAccount = category[1..^1].Trim();
                category = null;
            }

            var splits = new List<ImportedSplit>();

            foreach (QifSplitLine split in record.Splits)
            {
                if (!TryReadAmount(split.Amount, out Money splitAmount))
                {
                    continue;
                }

                splits.Add(new ImportedSplit(NormalizeCategory(split.Category), split.Memo, splitAmount));
            }

            if (splits.Count > 0)
            {
                Money total = Money.Sum(splits.Select(s => s.Amount));

                if (total != amount)
                {
                    // The transaction total is authoritative; a split list that disagrees is
                    // dropped rather than allowed to break the invariant that they sum to it.
                    _diagnostics.Add(new ImportDiagnostic(
                        ImportSeverity.Warning,
                        QifDiagnostic.SplitMismatch,
                        $"The split lines on the transaction dated {posted:d} add up to {total.ToAccountingString()} but the transaction is {amount.ToAccountingString()}. It was imported without them."));

                    splits.Clear();
                }
            }

            if (transferAccount is not null)
            {
                // Imported as an ordinary transaction rather than a linked transfer. A QIF
                // export covers one account, so the far leg is not in the file; creating one
                // would invent a transaction in an account the user did not import, and
                // importing that account's own export later would then duplicate it.
                _diagnostics.Add(new ImportDiagnostic(
                    ImportSeverity.Info,
                    QifDiagnostic.TransferKept,
                    $"The transaction dated {posted:d} is marked as a transfer to \"{transferAccount}\". It is imported as an ordinary transaction; link it by hand if you want both sides."));
            }

            Transactions.Add(new ImportedTransaction
            {
                Posted = posted,
                Amount = amount,
                Name = record.Payee,
                Memo = record.Memo,
                CheckNumber = record.Number,
                Cleared = ReadClearedStatus(record.Cleared),
                CategoryPath = NormalizeCategory(category),
                TransferAccountName = transferAccount,
                Splits = splits,
            });
        }

        private bool TryReadDate(string? text, out DateOnly date)
        {
            date = default;

            if (!QifConventionDetector.TrySplitDate(text, out int first, out int second, out int year))
            {
                return false;
            }

            (int month, int day) = _conventions.EffectiveDateOrder == QifDateOrder.DayFirst
                ? (second, first)
                : (first, second);

            // A file that contradicts itself still has individual dates that only read one
            // way; fall back to the other order rather than dropping the row.
            if (month is < 1 or > 12 || day < 1)
            {
                (month, day) = (day, month);
            }

            if (month is < 1 or > 12 || year is < 1 or > 9999 || day < 1
                || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            date = new DateOnly(year, month, day);
            return true;
        }

        private bool TryReadAmount(string? text, out Money amount)
        {
            amount = Money.Zero;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            char decimalMark = _conventions.EffectiveDecimalMark == QifDecimalMark.Comma ? ',' : '.';
            var cleaned = new System.Text.StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (char.IsAsciiDigit(character) || character is '-' or '+')
                {
                    cleaned.Append(character);
                }
                else if (character == decimalMark)
                {
                    cleaned.Append('.');
                }

                // Everything else — the grouping separator, currency symbols, spaces — is
                // discarded.
            }

            if (cleaned.Length == 0)
            {
                return false;
            }

            if (!decimal.TryParse(
                    cleaned.ToString(),
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out decimal value))
            {
                return false;
            }

            try
            {
                amount = Money.FromDecimal(Math.Round(value, 2, MidpointRounding.ToEven));
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Turns QIF's colon-separated path into the display form used elsewhere.
        /// </summary>
        /// <remarks>
        /// A trailing <c>/Class</c> is dropped: Quicken's classes are a second dimension this
        /// application does not model, and keeping them would fragment one category into many.
        /// </remarks>
        private static string? NormalizeCategory(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string value = raw.Trim();

            int slash = value.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                value = value[..slash].Trim();
            }

            if (value.Length == 0)
            {
                return null;
            }

            return string.Join(
                " : ",
                value.Split(':', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));
        }

        private static ClearedStatus? ReadClearedStatus(string? flag) =>
            flag?.Trim().ToUpperInvariant() switch
            {
                "X" or "R" => ClearedStatus.Reconciled,
                "*" or "C" => ClearedStatus.Cleared,
                null or "" => null,
                _ => ClearedStatus.Uncleared,
            };
    }

    /// <summary>One split line being assembled.</summary>
    private sealed class QifSplitLine
    {
        public string? Category { get; set; }

        public string? Memo { get; set; }

        public string? Amount { get; set; }
    }

    /// <summary>Accumulates the lines of one record until its terminator.</summary>
    private sealed class RecordBuilder
    {
        public string? Date { get; private set; }

        public string? Amount { get; private set; }

        public string? Payee { get; private set; }

        public string? Memo { get; private set; }

        public string? Category { get; private set; }

        public string? Number { get; private set; }

        public string? Cleared { get; private set; }

        public List<QifSplitLine> Splits { get; } = [];

        public bool HasContent => Date is not null || Amount is not null || Payee is not null;

        public void Add(char code, string value)
        {
            switch (code)
            {
                case 'D':
                    Date = value;
                    break;

                // T and U are the same figure; Quicken writes both and they must not be
                // treated as two transactions or added together.
                case 'T':
                case 'U':
                    Amount ??= value;
                    break;

                case 'P':
                    Payee = value;
                    break;

                case 'M':
                    Memo = value;
                    break;

                case 'L':
                    Category = value;
                    break;

                case 'N':
                    Number = value;
                    break;

                case 'C':
                    Cleared = value;
                    break;

                case 'S':
                    Splits.Add(new QifSplitLine { Category = value });
                    break;

                case 'E':
                    if (Splits.Count > 0)
                    {
                        Splits[^1].Memo = value;
                    }

                    break;

                case '$':
                    if (Splits.Count > 0)
                    {
                        Splits[^1].Amount = value;
                    }

                    break;

                default:
                    // Address lines, flags and anything a later Quicken invented are ignored.
                    break;
            }
        }

        public void Reset()
        {
            Date = Amount = Payee = Memo = Category = Number = Cleared = null;
            Splits.Clear();
        }
    }
}
