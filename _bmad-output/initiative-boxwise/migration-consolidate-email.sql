-- 部署迁移脚本：同步 Email 与 EmailForTwoFactor 分歧数据
-- 运行前确认：仅当 Email 有有效值时同步，绝不覆盖 EmailForTwoFactor 为 NULL/空
-- （保留外部登录用户的独立 2FA 邮箱）
-- 执行方式：直接对 SQLite 数据库运行此 SQL，或通过 EF Core 迁移 Seed 方法执行

UPDATE AspNetUsers
SET EmailForTwoFactor = Email
WHERE Email IS NOT NULL
  AND Email != ''
  AND (EmailForTwoFactor IS NULL OR EmailForTwoFactor != Email);
