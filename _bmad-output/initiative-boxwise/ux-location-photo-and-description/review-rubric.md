# Spine Pair Review — BoxWise 收纳位置

## Overall verdict

这份 spine pair 的覆盖度很高：Story #1 的五条 Requirements 全部有同名 Key Flow，状态表也把部分成功、明确失败和结果未知分开了。但它还不是一份干净的实现契约：4 个 high 问题集中在「已确认的布局仍被标成待定」「Story 尚未写回已确认的 UX 决策」「描述是否截断两份 spine 说法相反」「表单内已保存照片预览没有失败状态」。其余问题多为精确性、重复约束和验收边界，不是范围冲突。

**Findings:** critical 0 · high 4 · medium 8 · low 3.

## 1. Flow coverage — strong

已核对 Story Requirements 1–5（`story-location-photo-and-description.md:19-38`）与 EXPERIENCE Key Flows 1–5（`EXPERIENCE.md:246-298`）：五个名称逐字对应，均有具名主角、编号步骤、Climax 和失败/未知路径。Flow 6 补删除，Flow 7 补相机权限失败后改选设备图；两者虽不是 Requirement 名称，但对应 memlog 已确认的决策，未越界。

### Findings

无发现。

## 2. Token completeness — adequate

已提取 DESIGN frontmatter 中全部 token 和正文/组件中的 `{path.to.token}` 引用：15 个唯一引用全部可解析；颜色均为 hex；单浅色主题，不需要 light/dark 对；typography 使用 `note` 继承 MudBlazor 属于 spec 允许的平台约定；primary/error/success/text 的关键组合对比度均有说明。

### Findings

- **[medium]** `line-default` 同时被定义为「分隔线与输入边框」，又被声明「不作为唯一的状态或控件边界」；`#0000001F` 合成到白底约为 `#E0E0E0`，对比度约 1.3:1，若它是输入框唯一可见边界，则达不到 3:1 的控件边界目标（`DESIGN.md:103`, `DESIGN.md:166`）。*Fix:* 要么为输入框单列一个满足 3:1 的 `border-control` token，要么明确说明输入框由 MudBlazor 的标签/填充承担识别、`line-default` 只用于分隔线，并补上这个例外的对比度说明。

## 3. Component coverage — adequate

已提取两份 spine 中出现的全部组件名。DESIGN 正文有 12 个组件行，EXPERIENCE Component Patterns 有 11 行；`位置资料表单`、`位置描述字段`、`位置照片字段`、`本地照片预览`、`浏览位置信息面板`、`位置照片入口`、`位置照片弹窗`、`位置删除确认`、`状态提示条`、`物品照片区`、`位置树` 均有双边规格。

### Findings

- **[medium]** `字段错误` 在 DESIGN 有独立视觉行（`DESIGN.md:162`），但 EXPERIENCE Component Patterns 没有对应行；其行为散落在描述字段/照片字段行和 `字段校验失败` 状态里（`EXPERIENCE.md:88-89`, `EXPERIENCE.md:145`）。下游无法从组件表直接读出错误的放置、字段关联、播报和消失规则。*Fix:* 在 Component Patterns 增加一行「字段错误」，或明确把它折叠进两个字段行并删除 DESIGN 的独立组件名。

## 4. State coverage — adequate

已走 IA 的四个 surface：位置管理、浏览、物品详情、位置照片弹窗。冷加载、空、保存中、完整成功、部分成功、明确失败、结果未知、字段校验、相机不可用、编辑各阶段、删除各阶段、弹窗加载/成功/失败/重试/关闭/长内容/文件缺失/离线均有覆盖。

### Findings

- **[high]** 位置管理表单内「查看位置照片」展开已保存照片时，只写了「图片只在展开时加载」（`EXPERIENCE.md:89`, `EXPERIENCE.md:147-148`, `EXPERIENCE.md:200`），没有展开中的加载态和加载失败态；弹窗有失败+重试（`EXPERIENCE.md:185`），表单内预览没有。图片请求失败时实现者只能自行发明行为。*Fix:* 增加「表单内预览加载中 / 加载失败」状态，给出文案、重试或「收起」、焦点和 `aria-live` 规则，并说明是否复用状态提示条。
- **[medium]** 「移除照片失败」只写了「旧照片保留；提示失败；可重试」（`EXPERIENCE.md:156`），没有说明同一次保存里已修改的描述是否已保存、表单是否留在编辑态。与 `资料独立性` 表和 `编辑部分成功` 只覆盖「替换失败」不一致（`EXPERIENCE.md:112-119`, `EXPERIENCE.md:153`）。*Fix:* 明确「描述已保存 + 移除失败」的组合状态：表单不关闭、旧照片仍可查看、描述以服务端状态为准，并给出下一步文案。
- **[medium]** 照片上传结果未知时没有已承诺的微文案。唯一的结果未知文案是新建场景的「暂时无法确认是否保存成功。请关闭表单查看位置列表；若已出现该位置……」（`EXPERIENCE.md:76`），但此时位置和描述已保存、位置一定存在，这句话不适用；`EXPERIENCE.md:142` 只描述行为没有给文案。移除照片失败也没有独立文案（`EXPERIENCE.md:156`）。*Fix:* 为「位置/描述已保存、照片状态未知」和「移除照片失败」各加一条固定文案，放进 Voice and Tone 和状态表。
- **[low]** 弹窗的系统/浏览器返回键行为未定义。设计承诺不改 URL、关闭后保留筛选（`EXPERIENCE.md:44`, `EXPERIENCE.md:93`, `EXPERIENCE.md:197`），但关闭方式只列了 X、「关闭」、Esc 和背景点击；手机 PWA 上按返回键可能直接离开浏览页。*Fix:* 明确返回键是关闭弹窗还是离开页面；若不支持关闭，明确写出并纳入验收，避免「不打断浏览」在真机上失效。

## 5. Visual reference coverage — strong

`.working/` 下四张 HTML 全部存在，均在 DESIGN 的「低保真稿」段和 EXPERIENCE 的 Key Screens 表中被内联链接并说明其示意内容（`DESIGN.md:84-89`, `EXPERIENCE.md:50-59`）；`imports/` 为空且已说明；没有 `mockups/`/`wireframes/`，不存在孤儿文件。四张稿的定位是待定稿而非遗失的 mockups，这一点与任务前提一致。

### Findings

无发现。参考稿状态与 memlog 的冲突计入第 7 节，不重复计。

## 6. Bloat & overspecification — adequate

已对照 token 表检查组件行中的像素值，并检查同一约束在 Requirements、Voice、Component、State、Data Contracts、Assumptions 之间的重复。

### Findings

- **[medium]** 组件行大量用原始 px 复述已存在的 token：`12px` 对应 `{spacing.3}`，`8px` 对应 `{spacing.2}`，`4px` 对应 `{spacing.1}`，`4px 圆角` 对应 `{rounded.sm}`（`DESIGN.md:153`, `DESIGN.md:155`, `DESIGN.md:157`, `DESIGN.md:159-163`）。改 token 时这些行不会跟着变。*Fix:* 组件行改用 token 引用，或只写一次「下列数值是当前解析结果，实际以 token 为准」。
- **[medium]** 300 字和照片限制被反复重述：300 字出现在 `DESIGN.md:154`, `DESIGN.md:183` 与 `EXPERIENCE.md:88`, `EXPERIENCE.md:105`, `EXPERIENCE.md:138`, `EXPERIENCE.md:145`, `EXPERIENCE.md:198`, `EXPERIENCE.md:213`, `EXPERIENCE.md:330`, `EXPERIENCE.md:336`；照片格式/大小出现在 `DESIGN.md:155`, `DESIGN.md:184` 与 `EXPERIENCE.md:89`, `EXPERIENCE.md:106`, `EXPERIENCE.md:198`, `EXPERIENCE.md:334`, `EXPERIENCE.md:337`。约束一旦调整，容易只改一处。*Fix:* 以 Data Contracts 表为唯一 canon，其余位置只保留状态特有的行为，用交叉引用代替复述。
- **[low]** `物品照片区` 复述现有实现的 150px/400px 尺寸（`DESIGN.md:163`），这是既有代码细节，不是本功能的视觉契约；它会在现有物品照片样式变化时漂移。*Fix:* 只保留「位置照片不进入该区域、不替代物品照片」的分离规则，尺寸引用现有组件行为即可。

## 7. Inheritance discipline — thin

已核对 `sources` 路径、Requirement 名称、术语表、组件名、Story/memlog 与两份 spine 的一致性。`sources` 中的 Story 和 `.memlog.md` 均可解析；五个 Flow 名称逐字对应 Requirements；组件名在两边一致。但「已确认」与「待确认」的状态没有随 memlog 更新，这是本 pair 最实质的契约问题。

### Findings

- **[high]** 用户已在 memlog 明确「布局确认」（`.memlog.md:13`），但两份 spine 仍把布局写成草案/待确认：`DESIGN.md:4`、`DESIGN.md:80`、`DESIGN.md:84`，`EXPERIENCE.md:3`、`EXPERIENCE.md:12`、`EXPERIENCE.md:50`、`EXPERIENCE.md:52`、`EXPERIENCE.md:320-335`。`DESIGN.md:80` 甚至写「评审确认后才能作为实现契约」。这与 memlog 冲突，会让下游把已确认布局继续当假设。*Fix:* 定稿时清除已确认布局的 `[ASSUMPTION]`，把「待确认」标题改成「已确认」并注明日期，只保留真正未决的条目（见 Mechanical notes），再把 `status` 设为 final。
- **[high]** Story #1 尚未写回已确认的 UX 决策：300 字/保留换行、1 张 JPG/PNG/WebP/≤10 MB、拍照或设备选图、相机不可用仍可选图、四张布局确认、删除空位置清理照片、部分成功不重复创建，均只在 EXPERIENCE/memlog 中（`story-location-photo-and-description.md:19-38`, `:54-55`; `EXPERIENCE.md:105-106`, `EXPERIENCE.md:334-337`; `.memlog.md:11`, `:13`）。这是**定稿必补**，不是 spine 与 Story 的语义冲突；但若只按 Story 验收，会漏掉这些约束。另：设计用共享表单替代现有行内重命名（`LocationManageDialog.razor:54-63`, `:222-259`），这个行为变化也需要写进 Story 或明确记录。*Fix:* 定稿前把上述决策补进 Story 的 Notes/Requirements，或明确声明两 spine 是该 Story 的约束来源；同时记录行内重命名被共享表单替代。
- **[high]** 描述展示契约自相矛盾：DESIGN 写「描述不截断」（`DESIGN.md:118`），EXPERIENCE A7 却写「浏览/弹窗中完整显示、保留换行、不折叠仍是待确认的展示假设」（`EXPERIENCE.md:330`）；浏览面板也没有长描述的容器规则（`EXPERIENCE.md:91`），长内容状态只写在弹窗里（`EXPERIENCE.md:188`）。*Fix:* 确认浏览页和弹窗都完整显示、保留换行；为浏览面板补一条 300 字/多行描述的 max-height 或滚动规则；删除 A7 的「待确认」，两份 spine 对齐。
- **[medium]** 物品详情的 `hasPhoto` 来源未定义。EXPERIENCE 说入口可见性来自「位置元数据中的 hasPhoto 标记」（`EXPERIENCE.md:92`），也说描述和是否有照片随位置数据加载（`EXPERIENCE.md:46`），但物品详情只列了有/无照片状态（`EXPERIENCE.md:173-174`），没有说物品详情 DTO 是否带 `locationHasPhoto`，还是需要额外取位置元数据。*Fix:* 明确物品详情的数据来源（随物品 DTO 返回，或加载位置元数据），并把它写进「点击前不请求照片文件」的验收。
- **[medium]** 「300 字」的计数单位和空白描述语义未定义。`EXPERIENCE.md:88`/`:105` 与 `DESIGN.md:154` 只写「300 字」，没有说按 Unicode 码点、字素簇还是 UTF-16 单元计数，换行是否计入；「空值即无描述」也没有说全空白是否按空处理。*Fix:* 定义计数单位、换行是否计入，以及 `Trim()` 后为空是否即无描述；客户端与服务端使用同一规则并纳入验收。
- **[low]** 术语表说「查看位置照片」是「唯一的已保存照片查看入口文案」（`EXPERIENCE.md:29`），但表单的新选照片状态使用「查看现有位置照片」（`EXPERIENCE.md:89`, `DESIGN.md:155`）。*Fix:* 把术语表的「唯一」限定为浏览/物品详情入口，或统一表单里的对比入口文案，并说明表单内是 disclosure、不是弹窗。

## 8. Shape fit — strong

DESIGN 按 Brand & Style → Colors → Typography → Layout & Spacing → Elevation & Depth → Shapes → Components → Do's and Don'ts 的 canonical 顺序排列；EXPERIENCE 具备全部 required defaults：Foundation、Information Architecture、Voice and Tone、Component Patterns、State Patterns、Interaction Primitives、Accessibility Floor、Key Flows。`Data Contracts` 是产品特有、且承担独立性/删除隔离的实质内容，不是凑数；`Responsive & Platform` 因多断点触发，`Inspiration & Anti-patterns` 因多处 rejected 方案触发，均成立。

### Findings

无发现。

## Mechanical notes

- `EXPERIENCE.md:4-6` 的两个 `sources` 均可解析；`../../backlog/story-location-photo-and-description.md` 和 `.memlog.md` 存在。
- 两份 spine 的 `status` 仍为 `draft`（`DESIGN.md:4`, `EXPERIENCE.md:3`）；定稿时需改为 final 并更新 `updated`。
- `DESIGN.md` frontmatter 的 `components` 未覆盖 `位置删除确认` 和 `字段错误`；`inline-notice` 只对应 `状态提示条`。若下游按组件 key 解析，需要补 key 或声明这两个组件只用正文规格。
- EXPERIENCE 没有任何 `{path.to.token}` 引用，只有「视觉规格见 DESIGN.md」的 section 级引用。若这是有意的分层，建议写一句说明；否则至少把 44×44、对比度和圆角相关规则按 token 名引用一次。
- 「spines win on conflict」在 `DESIGN.md:80`, `DESIGN.md:84`, `EXPERIENCE.md:12`, `EXPERIENCE.md:48`, `EXPERIENCE.md:52` 以及索引页 `ux-location-photo-and-description.md:14` 多处重复；按 validate 要求应只陈述一次。
- 四张 `.working/` 链接均存在，无孤儿；`imports/` 为空且已说明，无 Mermaid。
- 真正未决、但不构成冲突的条目：A4（弹窗内是否保留「位置路径」「描述」小节标签）、A5（部分成功后切换为编辑的交互模式）、A6（待移除状态）、A8（入口是否加图标）、A10（删除按钮用 error 描边而非现有 filled Error）、A11（复用现有 ImageUploader 的相机能力）。定稿时应逐条 commit 或写进 memlog，不要继续留在 `[ASSUMPTION]`。
- 本轮只新增本文件；未修改 Story、spine、`.working` 或其他文件，未启动应用、未访问实际数据、未提交/推送。
