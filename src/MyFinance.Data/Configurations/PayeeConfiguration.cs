using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyFinance.Core.Entities;

namespace MyFinance.Data.Configurations;

internal sealed class PayeeConfiguration : IEntityTypeConfiguration<Payee>
{
    public void Configure(EntityTypeBuilder<Payee> builder)
    {
        builder.ToTable("Payees");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(256).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(4000);

        builder.HasIndex(p => p.Name).IsUnique();

        // The importer matches thousands of downloaded descriptors against this on every
        // import, so it carries its own index.
        builder.HasIndex(p => p.NormalizedName);

        builder.HasOne(p => p.LastCategory)
            .WithMany()
            .HasForeignKey(p => p.LastCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PayeeAliasConfiguration : IEntityTypeConfiguration<PayeeAlias>
{
    public void Configure(EntityTypeBuilder<PayeeAlias> builder)
    {
        builder.ToTable("PayeeAliases");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.NormalizedPattern).HasMaxLength(256).IsRequired();
        builder.HasIndex(a => a.NormalizedPattern).IsUnique();

        builder.HasOne(a => a.Payee)
            .WithMany(p => p.Aliases)
            .HasForeignKey(a => a.PayeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PayeeEmbeddingConfiguration : IEntityTypeConfiguration<PayeeEmbedding>
{
    public void Configure(EntityTypeBuilder<PayeeEmbedding> builder)
    {
        builder.ToTable("PayeeEmbeddings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TextHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Model).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Vector).IsRequired();

        // One vector per payee; a rename replaces it rather than adding a second.
        builder.HasIndex(e => e.PayeeId).IsUnique();

        // Deleting a payee takes its vector with it, or the index would go on suggesting a
        // merchant the book no longer knows.
        builder.HasOne(e => e.Payee)
            .WithMany()
            .HasForeignKey(e => e.PayeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
