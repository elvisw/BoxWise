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
