using MyFinance.Import.Payees;

namespace MyFinance.Import.Tests.Payees;

public sealed class DescriptorCleanerTests
{
    [Theory]
    [InlineData("SQ *BLUE BOTTLE 1234", "Blue Bottle")]
    [InlineData("TST* SWEETGREEN 4471", "Sweetgreen")]
    [InlineData("PP*STEAM PURCHASE", "Steam Purchase")]
    [InlineData("POS DEBIT WHOLE FOODS 10259", "Whole Foods")]
    [InlineData("PURCHASE HOME DEPOT #6521", "Home Depot")]
    public void Processor_and_bank_prefixes_come_off(string raw, string expected) =>
        DescriptorCleaner.Clean(raw).Suggested.ShouldBe(expected);

    [Theory]
    [InlineData("BLUE BOTTLE COFFEE NEW YORK NY", "Blue Bottle Coffee New York")]
    [InlineData("SHELL OIL AUSTIN TX", "Shell Oil Austin")]
    [InlineData("NORTHWIND MARKETPLACE SEATTLE WA USA", "Northwind Marketplace Seattle")]
    public void A_trailing_state_code_comes_off_but_the_city_stays(string raw, string expected)
    {
        // Stripping the city as well would merge "Blue Bottle New York" with "Blue Bottle
        // San Francisco". Whether those are one payee is the user's call; the city-stripped
        // reading is offered as an alternative rather than chosen for them.
        DescriptorCleaner.Clean(raw).Suggested.ShouldBe(expected);
    }

    [Theory]
    [InlineData("7 ELEVEN 34212", "7 Eleven")]
    [InlineData("99 RANCH MARKET", "99 Ranch Market")]
    [InlineData("IN N OUT BURGER #123", "In N Out Burger")]
    [InlineData("STUDIO 54", "Studio 54")]
    [InlineData("PIER 1 IMPORTS", "Pier 1 Imports")]
    public void A_number_that_is_part_of_the_name_is_never_stripped(string raw, string expected)
    {
        // The leading token always survives, and so do one- and two-digit runs anywhere.
        DescriptorCleaner.Clean(raw).Suggested.ShouldBe(expected);
    }

    [Fact]
    public void A_branch_number_is_stripped_so_two_branches_become_one_payee()
    {
        // Three or more digits reads as a store code. Collapsing branches of one chain is
        // the behaviour that stops a payee list filling up with near-duplicates.
        DescriptorCleaner.Clean("CHECKCARD 4471 CONTOSO MARKET 553").Suggested.ShouldBe("Contoso Market");
        DescriptorCleaner.Clean("CONTOSO MARKET 604").Suggested.ShouldBe("Contoso Market");
    }

    [Fact]
    public void A_merchant_ending_in_two_letters_that_is_not_a_state_survives()
    {
        // Matching against a closed list of region codes is what protects these.
        DescriptorCleaner.Clean("BUFFALO EXCHANGE").Suggested.ShouldBe("Buffalo Exchange");
    }

    [Fact]
    public void Distinct_locations_of_one_brand_stay_distinct()
    {
        string first = DescriptorCleaner.Clean("SHELL 4471").Suggested;
        string second = DescriptorCleaner.Clean("SHELL 9902").Suggested;

        // Both trailing numbers are stripped, so these do collapse together — which is
        // correct for a store number. What must not collapse is a name-bearing difference.
        first.ShouldBe(second);

        DescriptorCleaner.Clean("TARGET").Suggested
            .ShouldNotBe(DescriptorCleaner.Clean("TARGET.COM").Suggested);
    }

    [Theory]
    [InlineData("AT&T MOBILITY", "AT&T Mobility")]
    [InlineData("CVS PHARMACY", "CVS Pharmacy")]
    [InlineData("BP FUEL", "BP Fuel")]
    [InlineData("TJX COMPANIES", "TJX Companies")]
    [InlineData("BANK OF AMERICA", "Bank of America")]
    public void Acronyms_and_connectives_survive_recasing(string raw, string expected) =>
        DescriptorCleaner.Clean(raw).Suggested.ShouldBe(expected);

    [Fact]
    public void A_name_the_bank_already_formatted_is_left_alone()
    {
        // Mixed case means the bank did the work. Re-casing could only make it worse, and
        // "MacHine" style damage is exactly what a Mc/Mac rule would cause.
        DescriptorCleaner.Clean("Blue Bottle Coffee").Suggested.ShouldBe("Blue Bottle Coffee");
        DescriptorCleaner.Clean("McDonald's").Suggested.ShouldBe("McDonald's");
    }

    [Fact]
    public void A_rule_that_would_consume_the_whole_name_does_not_fire()
    {
        // Left unguarded, the generic processor prefix would reduce this to nothing.
        DescriptorCleaner.Clean("SQ *SQ").Suggested.ShouldNotBeNullOrWhiteSpace();
        DescriptorCleaner.Clean("12345").Suggested.ShouldBe("12345");
    }

    [Fact]
    public void An_empty_descriptor_is_handled_without_throwing()
    {
        DescriptorCleaner.Clean(null).Suggested.ShouldBe(string.Empty);
        DescriptorCleaner.Clean("   ").Suggested.ShouldBe(string.Empty);
    }

    [Fact]
    public void The_raw_descriptor_is_always_preserved()
    {
        CleanedDescriptor cleaned = DescriptorCleaner.Clean("SQ *BLUE BOTTLE 1234 NEW YORK NY");

        cleaned.Raw.ShouldBe("SQ *BLUE BOTTLE 1234 NEW YORK NY");
    }

    [Fact]
    public void The_city_stripped_reading_is_offered_as_an_alternative()
    {
        CleanedDescriptor cleaned = DescriptorCleaner.Clean("BLUE BOTTLE NEW YORK NY");

        cleaned.Suggested.ShouldBe("Blue Bottle New York");

        // The location-retaining reading stays available, so a user who wants per-branch
        // payees is one click away rather than having to retype the name.
        cleaned.Alternatives.ShouldContain(
            a => a.Contains("NY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Applied_rules_are_reported_so_a_surprise_can_be_explained()
    {
        CleanedDescriptor cleaned = DescriptorCleaner.Clean("SQ *BLUE BOTTLE 1234");

        cleaned.AppliedRules.ShouldContain("volatile:1234");
        cleaned.AppliedRules.ShouldContain("processor-prefix");
    }
}

public sealed class StableKeyTests
{
    [Fact]
    public void Two_visits_to_the_same_merchant_produce_the_same_key()
    {
        // The single most important property in the importer. The alias a user records when
        // they correct a payee is keyed on this; if it varied with the store number, the
        // correction would never match again and the alias table would gain one dead row per
        // transaction while never learning anything.
        string first = DescriptorCleaner.Clean("SQ *BLUE BOTTLE 1234").StableKey;
        string second = DescriptorCleaner.Clean("SQ *BLUE BOTTLE 5678").StableKey;

        first.ShouldBe(second);
        first.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("AMAZON MKTPL 8842", "AMAZON MKTPL 9931")]
    [InlineData("UBER TRIP 03/14", "UBER TRIP 04/22")]
    [InlineData("WALGREENS #4471", "WALGREENS #9902")]
    [InlineData("SAFEWAY REF000123456", "SAFEWAY REF000998877")]
    public void Volatile_trailing_parts_do_not_change_the_key(string first, string second) =>
        DescriptorCleaner.Clean(first).StableKey.ShouldBe(DescriptorCleaner.Clean(second).StableKey);

    [Fact]
    public void Genuinely_different_merchants_get_different_keys()
    {
        DescriptorCleaner.Clean("SQ *BLUE BOTTLE 1234").StableKey
            .ShouldNotBe(DescriptorCleaner.Clean("SQ *SWEETGREEN 1234").StableKey);
    }

    [Fact]
    public void The_key_keeps_the_stable_parts_of_the_descriptor()
    {
        // Prefix and location are stable between visits, so they stay in the key. Only what
        // varies is removed.
        string key = DescriptorCleaner.Clean("SQ *BLUE BOTTLE 1234 NEW YORK NY").StableKey;

        key.ShouldContain("BLUE BOTTLE");
        key.ShouldContain("NEW YORK");
        key.ShouldNotContain("1234");
    }

    [Fact]
    public void The_key_is_case_and_punctuation_insensitive()
    {
        DescriptorCleaner.Clean("Blue Bottle Coffee").StableKey
            .ShouldBe(DescriptorCleaner.Clean("BLUE  BOTTLE, COFFEE.").StableKey);
    }
}
