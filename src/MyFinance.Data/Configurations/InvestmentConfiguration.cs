using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;
using SecurityEntity = MyFinance.Core.Entities.Security;

namespace MyFinance.Data.Configurations;

internal sealed class SecurityConfiguration : IEntityTypeConfiguration<SecurityEntity>
{
    public void Configure(EntityTypeBuilder<SecurityEntity> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Symbol).HasMaxLength(32);

        // The same fund held in two accounts is one security, so the name is the identity.
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.HasOne(h => h.Account)
            .WithMany()
            .HasForeignKey(h => h.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Security)
            .WithMany()
            .HasForeignKey(h => h.SecurityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One holding per security per account. Two would each be right about half the units.
        builder.HasIndex(h => new { h.AccountId, h.SecurityId }).IsUnique();
    }
}

internal sealed class InvestmentTransactionConfiguration : IEntityTypeConfiguration<InvestmentTransaction>
{
    public void Configure(EntityTypeBuilder<InvestmentTransaction> builder)
    {
        builder.Property(t => t.Memo).HasMaxLength(500);

        builder.HasOne(t => t.Account)
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Security)
            .WithMany()
            .HasForeignKey(t => t.SecurityId)
            .OnDelete(DeleteBehavior.Restrict);

        // The cash leg is an ordinary register row. Deleting it must not take the investment
        // record with it silently, so the link is severed rather than cascaded.
        builder.HasOne(t => t.CashTransaction)
            .WithMany()
            .HasForeignKey(t => t.CashTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.AccountId, t.Date });
    }
}

internal sealed class SecurityPriceConfiguration : IEntityTypeConfiguration<SecurityPrice>
{
    public void Configure(EntityTypeBuilder<SecurityPrice> builder)
    {
        builder.HasOne(p => p.Security)
            .WithMany(s => s.Prices)
            .HasForeignKey(p => p.SecurityId)
            .OnDelete(DeleteBehavior.Cascade);

        // One price per security per day: a second would make "the latest price" arbitrary.
        builder.HasIndex(p => new { p.SecurityId, p.AsOf }).IsUnique();
    }
}
