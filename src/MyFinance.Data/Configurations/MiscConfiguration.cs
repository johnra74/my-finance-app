using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class CategorizationRuleConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    public void Configure(EntityTypeBuilder<CategorizationRule> builder)
    {
        builder.ToTable("CategorizationRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Pattern).HasMaxLength(512).IsRequired();
        builder.Property(r => r.IsEnabled).HasDefaultValue(true);

        // Rules are evaluated in priority order on every imported row.
        builder.HasIndex(r => new { r.IsEnabled, r.Priority, r.Id });

        builder.HasOne(r => r.Account)
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetCategory)
            .WithMany()
            .HasForeignKey(r => r.TargetCategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetPayee)
            .WithMany()
            .HasForeignKey(r => r.TargetPayeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.SourceFileName).HasMaxLength(512);
        builder.HasIndex(b => b.ImportedUtc);

        builder.HasOne(b => b.Account)
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key).HasMaxLength(128);
        builder.Property(s => s.Value).HasMaxLength(4000);
    }
}

internal sealed class MerchantCodeCategoryConfiguration : IEntityTypeConfiguration<MerchantCodeCategory>
{
    public void Configure(EntityTypeBuilder<MerchantCodeCategory> builder)
    {
        builder.ToTable("MerchantCodeCategories");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Code).HasMaxLength(16).IsRequired();
        builder.HasIndex(m => m.Code).IsUnique();

        // Deleting a category takes its code mappings with it rather than leaving rows that
        // point at nothing and would suggest a category the book no longer has.
        builder.HasOne(m => m.Category)
            .WithMany()
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
