namespace MyFinance.Data.Migrations;

/// <summary>
/// Repairs cheque numbers that an earlier migration copied out of Microsoft Money verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Money's <c>TRN.szId</c> is a sort key: a one-character type flag, then — for a number — the
/// digits right-aligned in a twelve-character field, so that sorting the column as text orders
/// cheques numerically. Read verbatim it reached the register as <c>"0        1168"</c> and
/// <c>"1ATM"</c>. <c>MyFinance.Import.Mny.MoneyNumber</c> now decodes it at the point of
/// reading, which fixes every future migration; this fixes the books already written.
/// </para>
/// <para>
/// The rules match <c>MoneyNumber.Decode</c> exactly, and a test runs both over one list of
/// cases and asserts they agree — two statements of one rule is the risk this carries.
/// They are narrow on purpose: a cheque somebody numbered <c>1234</c> by hand begins with a
/// <c>1</c>, so the flag is only believed where the remainder could not be the number itself.
/// </para>
/// <para>
/// <c>GLOB</c> rather than <c>LIKE</c> because SQLite's <c>LIKE</c> has no character classes.
/// </para>
/// </remarks>
internal static class MoneyNumberRepair
{
    /// <summary>
    /// The statements the migration runs. Held here so the test executes the same text.
    /// </summary>
    public const string Sql = """
        UPDATE Transactions
        SET Number = TRIM(SUBSTR(Number, 2))
        WHERE Number IS NOT NULL
          AND LENGTH(Number) = 13
          AND SUBSTR(Number, 1, 1) = '0'
          AND TRIM(SUBSTR(Number, 2)) <> ''
          AND TRIM(SUBSTR(Number, 2)) NOT GLOB '*[^0-9]*';

        UPDATE Transactions
        SET Number = TRIM(SUBSTR(Number, 2))
        WHERE Number IS NOT NULL
          AND SUBSTR(Number, 1, 1) = '1'
          AND TRIM(SUBSTR(Number, 2)) <> ''
          AND TRIM(SUBSTR(Number, 2)) NOT GLOB '*[0-9]*';
        """;
}
