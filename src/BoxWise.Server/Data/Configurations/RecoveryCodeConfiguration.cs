using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("RecoveryCodes");

        builder.HasKey(rc => rc.Id);

        builder.Property(rc => rc.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(rc => rc.CodeHash)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(rc => rc.UserId);

        builder.HasOne(rc => rc.User)
            .WithMany(u => u.RecoveryCodes)
            .HasForeignKey(rc => rc.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
