using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Number).HasMaxLength(32);
        builder.Property(t => t.Memo).HasMaxLength(1024);
        builder.Property(t => t.FitId).HasMaxLength(128);

        // The register's primary access path: one account, ordered by date.
        builder.HasIndex(t => new { t.AccountId, t.Date, t.SequenceInDay, t.Id });

        // Reports slice by date across all accounts.
        builder.HasIndex(t => t.Date);

        // Duplicate detection on import. Unique per account, and filtered so the many rows
        // with no FITID (hand-entered ones) do not collide with each other.
        builder.HasIndex(t => new { t.AccountId, t.FitId })
            .IsUnique()
            .HasFilter("\"FitId\" IS NOT NULL");

        builder.HasIndex(t => t.PayeeId);
        builder.HasIndex(t => t.ImportBatchId);

        builder.HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Payee)
            .WithMany(p => p.Transactions)
            .HasForeignKey(t => t.PayeeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.ImportBatch)
            .WithMany(b => b.Transactions)
            .HasForeignKey(t => t.ImportBatchId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.ScheduledTransaction)
            .WithMany()
            .HasForeignKey(t => t.ScheduledTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        // A transfer is two rows pointing at each other. Modelled as a self-referencing
        // one-to-one so the database itself refuses a leg that points at a missing peer.
        builder.HasOne(t => t.TransferPeer)
            .WithOne()
            .HasForeignKey<Transaction>(t => t.TransferPeerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(t => t.IsTransfer);
        builder.Ignore(t => t.EffectiveAmount);
        builder.Ignore(t => t.IsUncategorized);
        builder.Ignore(t => t.IsSplit);
    }
}

internal sealed class TransactionSplitConfiguration : IEntityTypeConfiguration<TransactionSplit>
{
    public void Configure(EntityTypeBuilder<TransactionSplit> builder)
    {
        builder.ToTable("TransactionSplits");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Memo).HasMaxLength(1024);

        builder.HasIndex(s => s.TransactionId);

        // Every spending report groups by category over this table.
        builder.HasIndex(s => s.CategoryId);

        builder.HasOne(s => s.Transaction)
            .WithMany(t => t.Splits)
            .HasForeignKey(s => s.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Category)
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
