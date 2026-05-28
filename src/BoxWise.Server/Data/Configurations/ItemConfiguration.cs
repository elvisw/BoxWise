using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Note)
            .HasMaxLength(2000);

        builder.Property(x => x.PhotoPath)
            .HasMaxLength(500);

        builder.Property(x => x.ThumbPath)
            .HasMaxLength(500);

        builder.Property(x => x.MediumPath)
            .HasMaxLength(500);

        builder.HasOne(x => x.Location)
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Version)
            .IsConcurrencyToken();

        builder.HasMany(x => x.Tags)
            .WithMany(t => t.Items)
            .UsingEntity("ItemTag");
    }
}
