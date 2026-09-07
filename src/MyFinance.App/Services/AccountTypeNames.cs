using MyFinance.Core.Enums;

namespace MyFinance.App.Services;

/// <summary>
/// How each account type is written where a person will read it.
/// </summary>
/// <remarks>
/// In one place so the account editor's dropdown and every list that shows a type agree.
/// The enum's own names would read as "CreditCard" and "MoneyMarket", which is how the code
/// spells them and not how anybody else does.
/// </remarks>
public static class AccountTypeNames
{
    /// <summary>The types a user can choose, in the order the dropdown offers them.</summary>
    public static IReadOnlyList<AccountType> Choosable { get; } =
    [
        AccountType.Checking,
        AccountType.Savings,
        AccountType.MoneyMarket,
        AccountType.CertificateOfDeposit,
        AccountType.Cash,
        AccountType.CreditCard,
        AccountType.LineOfCredit,
    ];

    public static string Describe(AccountType type) => type switch
    {
        AccountType.Checking => "Checking",
        AccountType.Savings => "Savings",
        AccountType.MoneyMarket => "Money market",
        AccountType.CertificateOfDeposit => "Certificate of deposit",
        AccountType.Cash => "Cash",
        AccountType.CreditCard => "Credit card",
        AccountType.LineOfCredit => "Line of credit",

        // Investment and loan accounts arrive from an import but cannot be edited here.
        AccountType.UnsupportedImported => "Investment or loan",
        _ => type.ToString(),
    };
}
