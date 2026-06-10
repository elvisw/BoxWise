---
title: 'Docker 独立部署（不含 Caddy）'
type: 'feature'
created: '2026-06-10'
status: 'done'
route: 'one-shot'
---

## Intent

**Problem:** 当前 Docker 部署方案强制打包 Caddy 容器作为反向代理，用户无法使用已有的 Nginx/Caddy/Traefik 等自有反代。

**Approach:** 新增 `docker-compose.standalone.yml`（仅 boxwise 服务，绑定 127.0.0.1:5000），在 README 和部署指南中添加独立部署说明及 Nginx/Caddy 反代配置示例。

## Suggested Review Order

1. `docker-compose.standalone.yml` — 新增的独立部署文件，与原始 compose 对比确认差异
2. `README.md:499-548` — 新增"使用自有反向代理"章节
3. `docs/deployment-guide.md:104-145` — 新增"2.1 使用自有反向代理"章节

## Review Findings

- [x] [Review][Patch] Nginx 示例缺少图片缓存头 — 已修复，补充 `/api/images/` location 块含 `expires 1d` + `Cache-Control` 头
- [x] [Review][Defer] ForwardedHeaders KnownNetworks 硬编码 — `Program.cs:248-250` 仅信任 Docker 桥接子网（172.17-19.0.0/16），独立部署中反代从 `127.0.0.1` 连接时 `X-Forwarded-Proto` 可能不被信任，导致 Cookie Secure 标志失败。需单独修复 Program.cs 添加 `127.0.0.0/8` 到 KnownNetworks。
- [x] [Review][Defer] 独立 compose 环境变量比原始 compose 更完整 — 原始 `docker-compose.yml` 缺少 WebAuthn/TwoFactor/RateLimit 变量，两份文件不一致。建议同步更新原始 compose。
- [x] [Review][Defer] README 与 deployment-guide 反代示例内容重复 — 两处 Nginx/Caddy 配置逐字重复，未来修改需双处同步。
