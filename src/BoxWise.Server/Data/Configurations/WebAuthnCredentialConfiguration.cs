using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class WebAuthnCredentialConfiguration : IEntityTypeConfiguration<WebAuthnCredential>
{
    public void Configure(EntityTypeBuilder<WebAuthnCredential> builder)
    {
        builder.ToTable("WebAuthnCredentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(c => c.CredentialId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PublicKey)
            .IsRequired();

        builder.Property(c => c.DeviceName)
            .IsRequired()
            .HasMaxLength(100);

        // SignCount 作为并发令牌，防止重放攻击
        builder.Property(c => c.SignCount)
            .IsConcurrencyToken();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
