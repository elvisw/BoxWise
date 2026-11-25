---
type: ux
title: 收纳位置照片和描述
status: final
---

# 收纳位置照片和描述

- [DESIGN.md](DESIGN.md)：视觉与页面布局（final）。
- [EXPERIENCE.md](EXPERIENCE.md)：操作流程、状态和无障碍（final）。

## 静态稿（`mockups/`，布局已确认）

以下 HTML 只用于确认布局关系与关键状态；全部离线、无 JS，照片为自绘 SVG 示意。示意图不按 1:1 表达触控尺寸，也不证明真实应用、320 CSS px、200% 文本缩放或读屏已经通过。

- [管理位置](mockups/key-manage-location.html)：共用表单与固定操作区、已保存照片折叠/展开（加载/失败/重试）、新建部分成功、描述超限与相机失败。
- [浏览选中位置](mockups/key-browse-location.html)：描述全文、按需入口、长描述正常页面滚动；另示有照片无描述。
- [物品详情](mockups/key-item-detail.html)：物品照片区与收纳路径旁入口明显分离；另示物品无照片、长路径换行。
- [共用查看弹窗](mockups/key-photo-dialog.html)：成功（横/竖/方）、加载中、失败可重试/关闭、长路径/300 字量级描述纵向滚动。

## 评审与验证

- [设计契约评审](review-rubric.md)
- [无障碍与窄屏评审](review-accessibility.md)
- [合并验证报告（Markdown）](validation-report.md)
- [合并验证报告（HTML）](validation-report.html)
