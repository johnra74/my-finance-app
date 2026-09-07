using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        builder.ToTable("BudgetLines");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Notes).HasMaxLength(1024);

        // One budgeted amount per category per period.
        builder.HasIndex(b => new { b.CategoryId, b.PeriodStart, b.PeriodType }).IsUnique();

        builder.HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WatchedCategoryConfiguration : IEntityTypeConfiguration<WatchedCategory>
{
    public void Configure(EntityTypeBuilder<WatchedCategory> builder)
    {
        builder.ToTable("WatchedCategories");
        builder.HasKey(w => w.Id);

        builder.HasIndex(w => w.CategoryId).IsUnique();

        builder.HasOne(w => w.Category)
            .WithMany()
            .HasForeignKey(w => w.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
