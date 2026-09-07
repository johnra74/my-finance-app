using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;

namespace MyFinance.Core.Tests;

/// <summary>Builders that keep the arrange step of each test down to what it is actually about.</summary>
internal static class TestBook
{
    private static int _nextId = 1;

    public static int NextId() => _nextId++;

    public static Account Account(
        string name = "Everyday Checking",
        AccountType type = AccountType.Checking,
        decimal openingBalance = 0m) => new()
        {
            Id = NextId(),
            Name = name,
            Type = type,
            OpeningBalance = Money.FromDecimal(openingBalance),
        };

    public static Transaction Transaction(
        decimal amount,
        string date = "2026-01-01",
        ClearedStatus cleared = ClearedStatus.Uncleared,
        int accountId = 1,
        int sequenceInDay = 0,
        bool isVoid = false,
        int? id = null)
    {
        Money value = Money.FromDecimal(amount);
        var transaction = new Transaction
        {
            Id = id ?? NextId(),
            AccountId = accountId,
            Date = DateOnly.Parse(date),
            Amount = value,
            ClearedStatus = cleared,
            SequenceInDay = sequenceInDay,
            IsVoid = isVoid,
        };

        // Mirror the production invariant: one split always exists, even uncategorized.
        transaction.Splits.Add(new TransactionSplit
        {
            Id = NextId(),
            TransactionId = transaction.Id,
            Amount = value,
        });

        return transaction;
    }
}
