using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoxWise.Server.Models;

namespace BoxWise.Server.Data.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // IdentityUser 默认映射到 AspNetUsers，无需额外配置
        // 后续 Story 可在此添加自定义字段配置
    }
}
