using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class LlmConfigConfiguration : IEntityTypeConfiguration<LlmConfig>
{
    public void Configure(EntityTypeBuilder<LlmConfig> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BaseUrl)
            .HasMaxLength(500);

        builder.Property(x => x.ApiKey)
            .HasMaxLength(200);

        builder.Property(x => x.Model)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.TimeoutSeconds)
            .HasDefaultValue(30);
    }
}
