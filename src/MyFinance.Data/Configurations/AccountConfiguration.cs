using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Institution).HasMaxLength(128);
        builder.Property(a => a.AccountNumberMasked).HasMaxLength(64);
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsRequired().HasDefaultValue("USD");
        builder.Property(a => a.Notes).HasMaxLength(4000);
        builder.Property(a => a.OfxAccountKey).HasMaxLength(64);
        builder.Property(a => a.OfxBankId).HasMaxLength(64);

        builder.HasIndex(a => a.Name).IsUnique();
        builder.HasIndex(a => new { a.IsClosed, a.SortOrder });

        // Every import looks an account up by this. Not unique: two books could in principle
        // track the same bank account, and a failed match is a dropdown, not a crash.
        builder.HasIndex(a => a.OfxAccountKey);

        // Computed from Type; nothing to persist.
        builder.Ignore(a => a.Group);
        builder.Ignore(a => a.IsLiability);
        builder.Ignore(a => a.IsReadOnly);
    }
}
