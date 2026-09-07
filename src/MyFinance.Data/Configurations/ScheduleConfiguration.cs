using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class ScheduledTransactionConfiguration : IEntityTypeConfiguration<ScheduledTransaction>
{
    public void Configure(EntityTypeBuilder<ScheduledTransaction> builder)
    {
        builder.ToTable("ScheduledTransactions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Memo).HasMaxLength(1024);
        builder.Property(s => s.Interval).HasDefaultValue(1);
        builder.Property(s => s.DaysAheadToEnter).HasDefaultValue(5);
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasIndex(s => new { s.IsActive, s.StartDate });

        builder.HasOne(s => s.Account)
            .WithMany()
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Payee)
            .WithMany()
            .HasForeignKey(s => s.PayeeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ScheduledTransactionSplitConfiguration
    : IEntityTypeConfiguration<ScheduledTransactionSplit>
{
    public void Configure(EntityTypeBuilder<ScheduledTransactionSplit> builder)
    {
        builder.ToTable("ScheduledTransactionSplits");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Memo).HasMaxLength(1024);
        builder.HasIndex(s => s.ScheduledTransactionId);

        builder.HasOne(s => s.ScheduledTransaction)
            .WithMany(s => s.Splits)
            .HasForeignKey(s => s.ScheduledTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Category)
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScheduleOccurrenceConfiguration : IEntityTypeConfiguration<ScheduleOccurrence>
{
    public void Configure(EntityTypeBuilder<ScheduleOccurrence> builder)
    {
        builder.ToTable("ScheduleOccurrences");
        builder.HasKey(o => o.Id);

        // One row per series per due date: the guard that stops a bill being entered twice.
        builder.HasIndex(o => new { o.ScheduledTransactionId, o.DueDate }).IsUnique();

        // "What is overdue" is a scan of pending occurrences by date.
        builder.HasIndex(o => new { o.State, o.DueDate });

        builder.HasOne(o => o.ScheduledTransaction)
            .WithMany(s => s.Occurrences)
            .HasForeignKey(o => o.ScheduledTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Transaction)
            .WithMany()
            .HasForeignKey(o => o.TransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
