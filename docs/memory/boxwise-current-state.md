---
name: boxwise-current-state
description: BoxWise 项目当前状态 — 11 Epics 全部完成，261 测试通过
metadata: 
  node_type: memory
  type: project
  originSessionId: 4b67de02-6453-49b3-b222-1f179c273181
---

BoxWise 项目在 2026-06-02 全部完成，共 11 个 Epic、43 个 Story，261 个测试全部通过（29 Client + 232 Server）。

**2026-05-27 新增：设置页与导航重构**
- 底部导航从 3 Tab 扩展为 4 Tab（新增"设置"）
- 新建 `Settings.razor` 设置页（位置管理/标签管理/退出登录/账户预留）
- 新建 `LocationManageDialog.razor` 位置管理弹窗（增/改/删 + 父节点树形选择）
- 新建 `TagManageDialog.razor` 标签管理弹窗（增/改/删 + 物品计数）
- 顶栏移除退出登录按钮（移至设置页），后退按钮精简
- 浏览页移除位置管理齿轮入口
- 标签系统补齐后端 Rename/Delete 端点 + 前端管理 UI
- Tag 模型添加 Items 反向导航属性，TagDto 增加 ItemCount
- TagRepository 新增 5 个 Rename/Delete 单元测试 + 1 个级联删除测试
- TagRepository GetAllAsync 改用数据库投影 Select 避免全量加载
- 迁移 AddTagItemsNavigation：ItemTag 表外键列重命名
- 新增 MudPopoverProvider 支持 MudSelect 下拉菜单
- 修复：父节点下拉框显示名称（ToStringFunc）、键盘可访问性（role/tabindex）

**2026-05-26 新增：物品录入拍照功能**
- JS 模块 `camera-capture.js`：原生 `<input capture="environment">` + FileReader base64 编码
- `ImageUploader.razor` 改造：新增拍照按钮 + JS 互操作
- 修复 4 个 Bug（MultipartFormDataContent 字段名、图片 URL、PhysicalFileHttpResult、JS 回调）

**Epic 1: 项目搭建与账户认证** ✅
**Epic 2: 位置体系与标签管理** ✅
**Epic 3: 物品录入与智能识别** ✅
**Epic 4: 查找浏览与生产部署** ✅
**设置页与导航重构** ✅

**关键文档:** `CLAUDE.md`, `docs/superpowers/specs/2026-05-27-settings-navigation-design.md`, `docs/superpowers/plans/2026-05-27-settings-navigation-plan.md`
