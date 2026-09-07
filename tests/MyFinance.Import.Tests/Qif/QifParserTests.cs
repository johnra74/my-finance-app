using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Import.Model;
using MyFinance.Import.Qif;

namespace MyFinance.Import.Tests.Qif;

public sealed class QifParserTests
{
    [Fact]
    public void An_ordinary_export_reads()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PBlue Bottle Coffee
            MBeans
            LFood:Coffee
            N1236
            CX
            ^
            D03/13/2026
            T3200.00
            PACME Corp Payroll
            LIncome:Salary
            ^
            """);

        file.HasBlockingError.ShouldBeFalse();
        ImportedStatement statement = file.Statements.Single();

        statement.Format.ShouldBe(ImportFormat.Qif);
        statement.Transactions.Count.ShouldBe(2);

        ImportedTransaction first = statement.Transactions[0];
        first.Posted.ShouldBe(new DateOnly(2026, 3, 2));
        first.Amount.ShouldBe(Money.FromDecimal(-18.40m));
        first.Name.ShouldBe("Blue Bottle Coffee");
        first.Memo.ShouldBe("Beans");
        first.CheckNumber.ShouldBe("1236");
        first.Cleared.ShouldBe(ClearedStatus.Reconciled);

        // QIF carries the user's own categorization, which is the reason to support it.
        first.CategoryPath.ShouldBe("Food : Coffee");
    }

    [Fact]
    public void A_record_without_a_closing_caret_is_still_read()
    {
        // Real exports routinely omit the final terminator.
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PBlue Bottle
            """);

        file.Statements.Single().Transactions.Count.ShouldBe(1);
    }

    [Fact]
    public void Splits_are_read_with_their_categories_and_memos()
    {
        ImportedTransaction transaction = Single("""
            !Type:Bank
            D03/02/2026
            T-142.83
            PCostco
            SFood:Groceries
            EWeekly shop
            $-118.20
            SHome:Household supplies
            EDetergent
            $-24.63
            ^
            """);

        transaction.Splits.Count.ShouldBe(2);
        transaction.Splits[0].CategoryPath.ShouldBe("Food : Groceries");
        transaction.Splits[0].Memo.ShouldBe("Weekly shop");
        transaction.Splits[0].Amount.ShouldBe(Money.FromDecimal(-118.20m));
        transaction.Splits[1].Amount.ShouldBe(Money.FromDecimal(-24.63m));

        Money.Sum(transaction.Splits.Select(s => s.Amount)).ShouldBe(transaction.Amount);
    }

    [Fact]
    public void Splits_that_do_not_add_up_are_dropped_and_reported()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-142.83
            PCostco
            SFood:Groceries
            $-100.00
            ^
            """);

        ImportedTransaction transaction = file.Statements.Single().Transactions.Single();

        // The transaction total is authoritative. Keeping a split list that disagrees would
        // break the invariant every report depends on.
        transaction.Splits.ShouldBeEmpty();
        transaction.Amount.ShouldBe(Money.FromDecimal(-142.83m));
        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.SplitMismatch);
    }

    [Fact]
    public void A_transfer_is_imported_as_a_plain_transaction_and_flagged()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-400.00
            PTransfer
            L[Savings]
            ^
            """);

        ImportedTransaction transaction = file.Statements.Single().Transactions.Single();

        // A QIF export covers one account, so the far leg is not in the file. Inventing one
        // would create a transaction in an account the user never imported, and would then be
        // duplicated when they import that account's own export.
        transaction.TransferAccountName.ShouldBe("Savings");
        transaction.CategoryPath.ShouldBeNull();
        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.TransferKept);
    }

    [Theory]
    [InlineData("X", ClearedStatus.Reconciled)]
    [InlineData("R", ClearedStatus.Reconciled)]
    [InlineData("*", ClearedStatus.Cleared)]
    [InlineData("c", ClearedStatus.Cleared)]
    public void The_cleared_flag_is_understood(string flag, ClearedStatus expected)
    {
        ImportedTransaction transaction = Single($"""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            C{flag}
            ^
            """);

        transaction.Cleared.ShouldBe(expected);
    }

    [Fact]
    public void A_missing_cleared_flag_leaves_the_state_unstated()
    {
        Single("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            ^
            """).Cleared.ShouldBeNull();
    }

    [Fact]
    public void The_account_header_names_the_account_and_its_type()
    {
        ImportedFile file = QifParser.Parse("""
            !Account
            NEveryday Checking
            TBank
            ^
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            ^
            """);

        ImportedStatement statement = file.Statements.Single();
        statement.Account.Name.ShouldBe("Everyday Checking");
        statement.Account.MappedType.ShouldBe(AccountType.Checking);
    }

    [Fact]
    public void A_credit_card_section_is_recognised()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:CCard
            D03/02/2026
            T-18.40
            PShop
            ^
            """);

        ImportedStatement statement = file.Statements.Single();
        statement.Account.MappedType.ShouldBe(AccountType.CreditCard);
        statement.Account.Kind.ShouldBe(ImportedAccountKind.CreditCard);
    }

    [Fact]
    public void Investment_sections_are_skipped_and_reported()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            ^
            !Type:Invst
            D03/03/2026
            NBuy
            YAcme Corp
            T1000.00
            ^
            """);

        file.Statements.Single().Transactions.Count.ShouldBe(1);
        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.InvestmentUnsupported);
    }

    [Fact]
    public void A_quicken_class_suffix_is_dropped()
    {
        // Classes are a second dimension this application does not model; keeping them would
        // split one category into many.
        Single("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            LFood:Coffee/Vacation
            ^
            """).CategoryPath.ShouldBe("Food : Coffee");
    }

    [Fact]
    public void A_duplicated_amount_line_is_not_counted_twice()
    {
        // Quicken writes both T and U with the same figure.
        Single("""
            !Type:Bank
            D03/02/2026
            T-18.40
            U-18.40
            PShop
            ^
            """).Amount.ShouldBe(Money.FromDecimal(-18.40m));
    }

    [Fact]
    public void Address_lines_and_unknown_codes_are_ignored()
    {
        ImportedTransaction transaction = Single("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            A123 Main Street
            ASpringfield
            FSomethingNew
            ^
            """);

        transaction.Amount.ShouldBe(Money.FromDecimal(-18.40m));
        transaction.Name.ShouldBe("Shop");
    }

    [Fact]
    public void An_unreadable_row_is_skipped_and_the_rest_import()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PGood
            ^
            DGARBAGE
            T-22.10
            PBad
            ^
            """);

        file.Statements.Single().Transactions.Count.ShouldBe(1);
        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.UnreadableDate);
    }

    [Fact]
    public void A_file_with_no_transactions_is_reported_as_an_error()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Cat
            NFood:Coffee
            E
            ^
            """);

        file.HasBlockingError.ShouldBeTrue();
        file.Errors.ShouldContain(d => d.Code == QifDiagnostic.NoRecords);
    }

    [Fact]
    public void An_empty_file_is_refused()
    {
        Should.Throw<QifParseException>(() => QifParser.Parse("   "));
    }

    [Fact]
    public void The_category_list_can_be_read_separately()
    {
        IReadOnlyList<QifCategoryDefinition> categories = QifParser.ReadCategoryList("""
            !Type:Cat
            NFood:Coffee
            DCoffee shops
            E
            ^
            NIncome:Salary
            I
            ^
            NTaxes:Income tax
            E
            T
            ^
            """);

        categories.Count.ShouldBe(3);
        categories[0].Path.ShouldBe("Food:Coffee");
        categories[0].Description.ShouldBe("Coffee shops");
        categories[0].IsIncome.ShouldBeFalse();
        categories[1].IsIncome.ShouldBeTrue();
        categories[2].IsTaxRelated.ShouldBeTrue();
    }

    [Fact]
    public void Every_category_path_referenced_is_reported()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            LFood:Coffee
            ^
            D03/03/2026
            T-142.83
            PCostco
            SFood:Groceries
            $-100.00
            SHome:Cleaning
            $-42.83
            ^
            """);

        file.Statements.Single().CategoryPaths
            .ShouldBe(["Food : Coffee", "Food : Groceries", "Home : Cleaning"]);
    }

    private static ImportedTransaction Single(string text) =>
        QifParser.Parse(text).Statements.Single().Transactions.Single();
}

public sealed class QifConventionTests
{
    [Fact]
    public void A_day_above_twelve_proves_the_order_is_day_first()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D25/12/2026
            T-18.40
            PShop
            ^
            D03/02/2026
            T-22.10
            POther
            ^
            """);

        List<ImportedTransaction> rows = [.. file.Statements.Single().Transactions];

        // The first date can only be read one way, and that settles the whole file — which
        // is why the second, ambiguous on its own, is read as the third of February.
        rows[0].Posted.ShouldBe(new DateOnly(2026, 12, 25));
        rows[1].Posted.ShouldBe(new DateOnly(2026, 2, 3));
    }

    [Fact]
    public void A_month_above_twelve_in_second_place_proves_month_first()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D12/25/2026
            T-18.40
            PShop
            ^
            D03/02/2026
            T-22.10
            POther
            ^
            """);

        List<ImportedTransaction> rows = [.. file.Statements.Single().Transactions];

        rows[0].Posted.ShouldBe(new DateOnly(2026, 12, 25));
        rows[1].Posted.ShouldBe(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void A_wholly_ambiguous_file_says_so()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-18.40
            PShop
            ^
            """);

        // Nothing in the file settles the order, so the assumption is stated rather than
        // made silently.
        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.AmbiguousDates);
    }

    [Fact]
    public void A_file_that_contradicts_itself_is_flagged()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D25/12/2026
            T-18.40
            PShop
            ^
            D12/25/2026
            T-22.10
            POther
            ^
            """);

        file.Diagnostics.ShouldContain(d => d.Code == QifDiagnostic.ConflictingDates);

        // Both rows still import: each individual date only reads one way.
        file.Statements.Single().Transactions.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("D03/02'26", 2026)]
    [InlineData("D03/02/26", 2026)]
    [InlineData("D03/02/2026", 2026)]
    [InlineData("D 3/ 2/2026", 2026)]
    [InlineData("D03-02-2026", 2026)]
    public void Every_shape_quicken_writes_a_date_in_parses(string dateLine, int expectedYear)
    {
        ImportedFile file = QifParser.Parse($"""
            !Type:Bank
            {dateLine}
            T-18.40
            PShop
            ^
            """);

        file.Statements.Single().Transactions.Single().Posted.Year.ShouldBe(expectedYear);
    }

    [Fact]
    public void Grouped_amounts_with_a_full_stop_decimal_read_correctly()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-1,234.56
            PShop
            ^
            """);

        file.Statements.Single().Transactions.Single().Amount
            .ShouldBe(Money.FromDecimal(-1234.56m));
    }

    [Fact]
    public void A_european_file_reads_its_comma_as_the_decimal_point()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D25/12/2026
            T-1.234,56
            PShop
            ^
            D26/12/2026
            T-18,40
            POther
            ^
            """);

        List<ImportedTransaction> rows = [.. file.Statements.Single().Transactions];

        rows[0].Amount.ShouldBe(Money.FromDecimal(-1234.56m));
        rows[1].Amount.ShouldBe(Money.FromDecimal(-18.40m));
    }

    [Fact]
    public void A_currency_symbol_does_not_stop_an_amount_parsing()
    {
        ImportedFile file = QifParser.Parse("""
            !Type:Bank
            D03/02/2026
            T-$18.40
            PShop
            ^
            """);

        file.Statements.Single().Transactions.Single().Amount
            .ShouldBe(Money.FromDecimal(-18.40m));
    }
}
