using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // IdentityUser 默认映射到 AspNetUsers
        // 后续 Story 可在此添加自定义字段配置

        // 添加 NormalizedEmail 唯一索引，配合应用层 TOCTOU 防护确保邮箱唯一性
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
