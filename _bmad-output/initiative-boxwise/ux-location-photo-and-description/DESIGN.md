---
name: BoxWise 收纳位置
description: 家庭收纳 PWA 中位置描述与位置照片的视觉增量；继承 MudBlazor 与现有 BoxWise 主题，不新增配色。
status: final
updated: 2026-11-25
colors:
  primary: '#546E7A'
  primary-contrast: '#FFFFFF'
  error: '#EF5350'
  success: '#66BB6A'
  background: '#FAFAFA'
  surface: '#FFFFFF'
  surface-muted: '#F5F5F5'
  text-primary: '#000000DE'
  text-secondary: '#00000099'
  line-default: '#0000001F'
typography:
  dialog-title:
    note: 'MudBlazor Typo.h6；现有对话框标题'
  section-label:
    note: 'MudBlazor Typo.subtitle2；表单与信息面板的小节标签'
  body:
    note: 'MudBlazor Typo.body2；描述、路径与表单正文'
  meta:
    note: 'MudBlazor Typo.caption；辅助说明与状态'
rounded:
  sm: 4px
spacing:
  '1': 4px
  '2': 8px
  '3': 12px
  '4': 16px
  '6': 24px
components:
  location-form-panel:
    background: '{colors.surface-muted}'
    radius: '{rounded.sm}'
    padding: '{spacing.3}'
  location-description-field:
    foreground: '{colors.text-primary}'
  location-photo-field:
    foreground: '{colors.text-primary}'
    secondary-text: '{colors.text-secondary}'
  saved-photo-preview:
    background: '{colors.surface-muted}'
    radius: '{rounded.sm}'
    padding: '{spacing.2}'
  local-photo-preview:
    background: '{colors.surface-muted}'
    radius: '{rounded.sm}'
    padding: '{spacing.2}'
  browse-location-panel:
    background: '{colors.surface-muted}'
    radius: '{rounded.sm}'
    padding: '{spacing.3}'
  location-photo-entry:
    foreground: '{colors.primary}'
    min-target: 44px
  location-photo-dialog:
    background: '{colors.surface}'
    frame-background: '{colors.surface-muted}'
    frame-radius: '{rounded.sm}'
    frame-padding: '{spacing.2}'
  dialog-controls:
    min-target: 44px
  inline-notice:
    foreground: '{colors.text-primary}'
    error-icon: '{colors.error}'
    success-icon: '{colors.success}'
  field-error:
    foreground: '{colors.text-primary}'
    error-icon: '{colors.error}'
  delete-confirm:
    foreground: '{colors.text-primary}'
    error-icon: '{colors.error}'
  item-photo-area:
    note: '现有物品照片样式保持不变；位置照片不进入此区域。'
  location-tree:
    note: '现有树样式与键盘模型保持不变；不加入照片缩略图或角标。'
---

## Brand & Style

BoxWise 是家庭成员在家里、常常单手拿着手机使用的收纳 PWA。位置描述和位置照片只回答「在哪」，不把每个位置变成第二件物品。

位置照片不出现在物品照片区，不做物品卡缩略图、详情主图或浏览时自动展示。它在浏览/物品详情里的唯一外部表现是文字入口「查看位置照片」；点击后照片才出现在共用弹窗里。位置管理表单里的已保存照片也在字段内按需展开，默认不加载。物品照片回答「这是什么」，位置照片回答「在哪个抽屉/柜子/箱子里」——两者不共享容器、入口或加载时机。

整体继承现有 MudBlazor 页面：浅色主题、灰蓝主色、4px 默认圆角、`MudPaper`/`MudDialog` 的原有层级。不新增配色、字体、图标体系、页面或导航项。

布局已于 2026-11-25 由用户确认。静态稿仅作视觉参考；与本文或 `EXPERIENCE.md` 冲突时以 spine 为准。

四张离线静态稿及各自展示的状态见 [设计入口](ux-location-photo-and-description.md)。触控尺寸以本文规格为准，不以示意图量尺。

## Colors

调色板继承 `MainLayout` 的 `MudTheme` 与 MudBlazor 默认值；本功能不新增颜色。

- **Primary `#546E7A`** — 入口文字、表单主按钮、焦点环。白底约 5.40:1，满足 AA 正文；不单独表达「有没有照片」。
- **Primary contrast `#FFFFFF`** — 只与 primary 填充按钮搭配。
- **Error `#EF5350`** — 只做错误图标和删除确认的辅助色。白底约 3.49:1，不承载小字号正文；错误说明一律用 text-primary。
- **Success `#66BB6A`** — 只做成功图标。白底约 2.36:1，不能单独承载状态；成功/失败都同时有文字。
- **Background / Surface** — 现有页面与对话框底色，不改变。
- **Surface muted** — 表单面板、浏览信息面板、照片框底色；沿用现有灰。
- **Text primary**（白底约 16.1:1）— 描述、路径、表单正文、错误/成功说明。
- **Text secondary**（白底约 5.74:1）— 辅助说明、空状态、未保存提示。
- **Line default** — 只用于装饰性分隔线，不作为输入框唯一边界。输入框沿用 MudBlazor outlined field 的标签与轮廓；实现验收时实测轮廓对比度应达到 3:1，不达标时使用主题已有的更深轮廓，不新增品牌色。

**对比度规则：** 正文、描述、入口文字使用 primary 或 text-primary/secondary，均满足 AA；error/success 不承担小字号文本。任何状态都不能只靠颜色表达。

## Typography

| 角色 | Token | 现有 MudBlazor |
|---|---|---|
| 对话框标题 | `{typography.dialog-title}` | `Typo.h6` |
| 小节标签 | `{typography.section-label}` | `Typo.subtitle2` |
| 正文 | `{typography.body}` | `Typo.body2` |
| 辅助/状态 | `{typography.meta}` | `Typo.caption` |

- 位置名称、描述、完整路径使用正文；路径不缩小，描述不截断。
- 辅助说明、空状态、未保存提示使用 meta + `{colors.text-secondary}`。
- 位置名称不是 hero；新增内容里只有照片弹窗使用标题层级。

## Layout & Spacing

沿用 MudBlazor 的 4px 间距阶梯与现有页面外壳；不新增页面、导航项或全屏路由。

- **位置资料表单。** 位于现有「管理位置」`MudDialog`（Small / FullWidth），在位置列表上方。表单是内嵌 `MudPaper`，全宽、`{spacing.3}` 内边距、`{spacing.2}` 字段间距。字段顺序固定：标题 → 名称 → 上级位置 → 描述（可选）→ 照片（可选）。编辑时上级位置只读。标题栏和底部操作区固定在滚动区外；表单正文是唯一纵向滚动容器（`min-height:0`、`overflow-y:auto`、`overscroll-behavior:contain`），「保存」「取消」始终可达。操作按钮在空间不足时换行。
- **浏览页。** 沿用现有 `MudGrid`：位置树在左列（xs 12 / sm 6），标签筛选在右列。浏览信息面板放在位置树下方、同一列内，上边距 `{spacing.2}`；xs 顺序为位置树 → 信息面板 → 标签筛选。描述全文保留换行，参与正常页面滚动，不设内部 `max-height`、不折叠/截断；选中位置后不自动跳焦点、不自动滚入视口。
- **物品详情。** 入口紧跟「位置: A → B → C」行，作为行内元素自然换行；换行时上边距 `{spacing.2}`。永远在元数据区，不进入顶部物品照片区。
- **位置照片弹窗。** `MaxWidth.Medium`、`FullWidth`；内容内边距 `{spacing.4}`；路径、照片、描述纵向排列，间距 `{spacing.3}`。标题栏与底部操作区固定，正文是唯一纵向滚动容器；照片最大高度 `min(60vh, 60dvh)`，照片框至少 200px 高；不横向滚动，长名称/路径使用 `overflow-wrap:anywhere`。
- **删除确认。** 沿用现有行内确认，不新增确认弹窗；后果说明换行显示在位置名下方，按钮在空间不足时换行，间距 `{spacing.2}`。

## Elevation & Depth

沿用 MudBlazor 与现有页面的层级语言，不新增阴影。

- 表单内嵌面板、浏览信息面板使用 `MudPaper Elevation="0"` + `{colors.surface-muted}`，靠色调分组。
- 本地照片预览沿用现有照片预览的 `MudPaper` 抬升（Elevation 1），低于弹窗层级。
- 位置照片弹窗使用 `MudDialog` 既有阴影；照片本身不加投影、描边或悬浮效果。
- 「有没有照片」只靠文字入口和表单状态行，不靠阴影、角标或抬升。

## Shapes

- 输入框、按钮、图片、表单面板、信息面板、弹窗照片框统一使用 `{rounded.sm}`，与现有 `LayoutProperties.DefaultBorderRadius` 一致。
- 现有设置列表行的 8px 圆角保持不变，不复制到新面板。
- 位置照片是矩形，4px 圆角，`object-fit:contain` 完整显示；不裁掉抽屉边缘。
- 不使用胶囊、徽章或圆形头像式的位置照片。

## Components

| 组件 | 视觉规格 |
|---|---|
| 位置资料表单 | 对话框标题沿用「管理位置」。表单使用 `MudPaper Elevation="0"`、`{colors.surface-muted}` 底、`{rounded.sm}` 圆角、`{spacing.3}` 内边距。表单内小节标题为「添加位置」或「编辑位置」，用 `{typography.section-label}`。布局见「Layout & Spacing」。「保存」用 primary 填充 + primary-contrast 文字；「取消」为文本按钮；两者最小点击区域为 44×44，空间不足时换行。 |
| 位置描述字段 | `MudTextField` outlined、多行、至少 3 行；标签「描述（可选）」，正文用 `{typography.body}`。辅助说明写明上限与「保存后在浏览页显示」，用 `{typography.meta}` + text-secondary；具体字数规则以 `EXPERIENCE.md.Data Contracts` 为准。保留换行；不显示实时计数器，不在输入时静默截断。超限时字段下方显示错误，错误与字段关联。 |
| 位置照片字段 | 带标签「位置照片（可选）」的字段组，不是缩略图。来源为「拍照」「从设备选择」两个描边按钮，下方辅助说明写明格式/大小限制。相机不可用或权限被拒绝时显示专用提示，「从设备选择」保持可用。格式/大小错误显示在字段下方，不改变已保存照片或已有本地预览。已保存时默认只显示「已保存位置照片」文字，加「查看位置照片」「更换照片」「移除照片」文本按钮，不渲染图片；点「查看位置照片」在字段内展开预览，再点「收起」隐藏。所有新按钮至少 44×44，允许换行。 |
| 已保存照片预览（表单内） | 展开后的 disclosure 内容，不叠弹窗。加载中显示 `MudProgressCircular` + 「正在加载位置照片…」；失败显示错误文本 + 「重试」，错误文本与重试控件分开；「收起」始终可用；「重试」「收起」至少 44×44。成功显示图片，`max-width:100%`、`max-height:200px`、`object-fit:contain`，`alt` 与「已保存位置照片」状态一致。预览框与本地预览同规格，但不显示「未保存」。 |
| 本地照片预览 | `MudPaper Elevation="1"`，`{colors.surface-muted}` 底、`{rounded.sm}` 圆角、`{spacing.2}` 内边距；仅在文件通过格式与大小校验后显示；图片 `max-width:100%`、`max-height:200px`、`object-fit:contain`。标注「新选照片（未保存）」和「仅本机预览，保存后才会写入位置照片」，用 `{typography.meta}` + text-secondary。不触发服务器请求。 |
| 浏览位置信息面板 | 位于位置树下方、同一列内；`MudPaper Elevation="0"`，`{colors.surface-muted}` 底、`{rounded.sm}` 圆角、`{spacing.3}` 内边距、上边距 `{spacing.2}`。「描述」标签用 `{typography.meta}` + text-secondary；描述正文用 `{typography.body}` + text-primary，完整显示、保留换行、随页面滚动。只有照片没有描述时，只包含入口，不显示空标签。不渲染缩略图或占位。 |
| 位置照片入口 | 纯文字按钮，primary、`{typography.body}`，无填充、无边框、无缩略图；文案固定「查看位置照片」。触控目标至少 44×44（含 padding）；浏览页位于信息面板底部，物品详情位于路径之后，空间不足时自然换行。默认不加图标；即使加也不能是照片缩略图。 |
| 位置照片弹窗 | 浏览与物品详情共用的 `MudDialog`，`MaxWidth.Medium`、`FullWidth`；标题「位置照片」，标题栏有 44×44 的可见 X，底部有 44px 高的「关闭」文本按钮。标题栏与操作区固定，正文唯一纵向滚动；内容依次为完整路径（可换行）、照片区域、描述（如有，完整保留换行）。照片区域 `{colors.surface-muted}` 底、`{rounded.sm}` 圆角、`{spacing.2}` 内边距；图片 `max-width:100%`、`max-height:min(60vh, 60dvh)`、`object-fit:contain`，横/竖/方图均完整显示。加载中/失败时照片区域保留至少 200px 高度，避免弹窗跳动；失败错误文本与「重试」分开。不做多图、缩略图条、缩放、轮播或下载按钮。 |
| 位置删除确认 | 沿用现有行内确认，不新增弹窗。位置名加粗；后果说明见 `EXPERIENCE.md` 的「Voice and Tone」，使用 `{typography.meta}` + text-secondary。「删除」使用 error 色边框/图标 + text-primary 文字；「取消」为文本按钮；两者至少 44×44，空间不足时换行，间距 `{spacing.2}`。 |
| 状态提示条 | 放在位置资料表单的正文顶部；`MudPaper Elevation="0"`、`{colors.surface-muted}` 底、`{rounded.sm}` 圆角、`{spacing.3}` 内边距。图标 + 文字：错误图标 error、成功图标 success，说明文字 text-primary。失败/部分成功/结果未知常驻显示，带「重试上传」「重试」或「关闭提示」，动作至少 44×44，不自动消失。完整成功可用现有 `MudSnackbar`。明确失败与「结果未知」用不同文案。 |
| 字段错误 | 位于对应字段下方，间距 `{spacing.1}`；错误图标 error，文字 text-primary + `{typography.meta}`；不用红色小字单独承载状态。描述超限、照片格式/大小不合规、相机不可用都同时有图标和文字。 |
| 物品照片区 | 保持现状；位置照片永远不渲染在此区域，也不替代物品照片。 |
| 位置树 | 保持现状：只显示文字，不显示照片缩略图、照片角标，不改变排序或现有键盘模型。 |

以下现有组件按 MudBlazor 默认规格原样继承：`MudDialog`、`MudTextField`、`MudSelect`、`MudButton`、`MudPaper`、`MudTreeView`、`MudChip`、`MudSnackbar`。

## Do's and Don'ts

| Do | Don't |
|---|---|
| 用「查看位置照片」标明入口查看的是位置 | 用「查看照片」「位置主图」「封面」等会和物品照片混淆的说法 |
| 在浏览和物品详情中，点击后才加载位置照片 | 把位置照片做成物品卡缩略图、详情主图或树节点角标 |
| 用文字描述 + 文字入口回答「在哪」 | 选中位置就自动加载照片、自动跳焦点或自动滚到面板 |
| 完整显示描述，保留换行，随页面正常滚动 | 折叠、截断、内部滚动或改写成摘要 |
| 表单里明确区分「已保存位置照片」和「新选照片（未保存）」 | 让已保存照片在表单里自动出现缩略图 |
| 在位置管理表单内展开已保存照片预览（含加载/失败/重试） | 在管理对话框上再叠一个照片弹窗 |
| 固定标题栏/操作区，正文纵向滚动，关闭与保存/取消可达 | 用 `overflow:hidden` 裁剪对话框正文或固定高度锁死按钮 |
| 新增控件至少 44×44，允许按钮文字换行 | 用 `white-space:nowrap` 或 28–40px 的按钮照抄静态稿 |
| 失败/部分成功/结果未知时让提示条或重试控件获得焦点并滚入视口 | 把提示留在视口外，让用户以为没有发生 |
| 沿用现有 MudBlazor 调色板、字体、圆角和组件 | 新增配色、深色模式、字体、图标体系或新页面 |
| 关闭弹窗后保留筛选、滚动和焦点 | 关闭时刷新页面、重置筛选或把焦点丢到页首 |
| 一个位置一张照片，描述和照片独立保存 | 多图相册、照片搜索、描述搜索、轮播或缩放 |
| 编辑位置时上级位置只读 | 在编辑表单里重新挂载父位置 |
| 删除空位置时只清理它自己的描述和照片 | 级联删除物品照片，或先删照片再报错 |
| 明确区分「保存失败」「保存结果未知」「用户取消相机」 | 把超时/响应丢失说成「没有保存」，或把用户取消说成相机故障 |
| 不抢占系统返回键，保留可见关闭与 Esc | 改写浏览器历史来强制关闭弹窗 |
