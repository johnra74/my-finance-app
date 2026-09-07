namespace MyFinance.Data.Security;

/// <summary>
/// Which version of the database schema this build understands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Increment <see cref="Current"/> whenever a migration is added.</b> A test asserts this
/// against the number of migrations in the assembly, so forgetting fails the build rather
/// than shipping a book that misreports itself.
/// </para>
/// <para>
/// This exists because EF Core's migrations history answers "which migrations have run here",
/// not "was this file written by a build newer than me". EF reads the columns it recognises
/// and ignores those it does not, so an older build opening a newer book would find nothing
/// wrong and would then write rows shaped for a schema it has never seen — into the one file
/// that has no recovery path. A single integer, compared before anything is read, is what
/// makes that detectable.
/// </para>
/// </remarks>
public static class BookSchema
{
    /// <summary>
    /// The schema this build reads and writes. One per migration, in order.
    /// </summary>
    /// <remarks>
    /// 4 adds the investment tables — see `specs/013-investment-accounts`.
    /// 5 decodes Microsoft Money's cheque-number sort key in books already migrated. The
    /// first upgrade that repairs data rather than changing shape — see `specs/009`.
    /// </remarks>
    public const int Current = 5;

    /// <summary>
    /// The version an unstamped book is taken to have.
    /// </summary>
    /// <remarks>
    /// Books created before versioning existed carry no stamp, in either the database or the
    /// sidecar. They are read as the schema current at the time stamping shipped — not as
    /// unknown. Treating absence as unknown would make this feature's first act be to refuse
    /// every book already on disk, which is the opposite of the point.
    /// </remarks>
    public const int Unstamped = 3;

    /// <summary>What a book's recorded version means for this build.</summary>
    public enum State
    {
        /// <summary>Written by this build. Open it.</summary>
        Current,

        /// <summary>Written by an older build. Upgrade, having backed up first.</summary>
        NeedsUpgrade,

        /// <summary>Written by a newer build. Refuse.</summary>
        TooNew,
    }

    public static State Compare(int bookVersion) => bookVersion switch
    {
        < Current => State.NeedsUpgrade,
        > Current => State.TooNew,
        _ => State.Current,
    };
}
