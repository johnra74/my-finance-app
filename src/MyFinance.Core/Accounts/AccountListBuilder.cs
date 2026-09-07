using MyFinance.Core.Enums;

namespace MyFinance.Core.Accounts;

/// <summary>
/// Arranges account summaries into the grouped, subtotalled list the Banking screen shows.
/// </summary>
/// <remarks>
/// This is pure and lives in Core so the exact figures the user reads off the screen —
/// subtotals and the grand total — are covered by tests that need no database and no
/// Windows. The Banking view binds to the result and does no arithmetic of its own.
/// </remarks>
public static class AccountListBuilder
{
    /// <summary>Group headings, in the order they appear on screen.</summary>
    private static readonly AccountGroup[] GroupOrder =
    [
        AccountGroup.Bank,
        AccountGroup.Credit,
        AccountGroup.Other,
    ];

    public static string HeaderFor(AccountGroup group) => group switch
    {
        AccountGroup.Bank => "Bank accounts",
        AccountGroup.Credit => "Credit cards",
        AccountGroup.Other => "Other accounts",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown account group."),
    };

    /// <summary>
    /// Builds the grouped list. Groups with no accounts are omitted rather than shown empty,
    /// so a book with no credit cards does not display a "Credit cards" heading over nothing.
    /// </summary>
    public static AccountListSummary Build(IEnumerable<AccountSummary> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        List<AccountSummary> materialized = [.. accounts];
        var groups = new List<AccountGroupSummary>();

        foreach (AccountGroup group in GroupOrder)
        {
            List<AccountSummary> members =
            [
                .. materialized
                    .Where(a => a.Group == group)
                    .OrderBy(a => a.IsClosed)
                    .ThenBy(a => a.Account.SortOrder)
                    .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            ];

            if (members.Count > 0)
            {
                groups.Add(new AccountGroupSummary
                {
                    Group = group,
                    Header = HeaderFor(group),
                    Accounts = members,
                });
            }
        }

        return new AccountListSummary { Groups = groups };
    }
}
