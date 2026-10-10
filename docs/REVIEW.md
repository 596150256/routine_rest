# 代码及安全审查

日期：2026-09-28。范围：新建本地 Windows 工具，核心状态机与全部 Windows 集成代码。用户明确选择本地实现，未创建 GitHub issue、远程仓库、PR 或 CI。

## 验证证据

- Release 编译：零警告、零错误，启用 Nullable 和 TreatWarningsAsErrors。
- 行为测试覆盖：首次操作启动、50/10 自动转换、阅读不误判、短锁屏与连续离开、跨午夜统计、喝水范围和重复提交、紧急解除、重启恢复、坏 JSON 备份恢复、拒绝损坏字段、未填写喝水仍计时、保留历史待填记录、校时回退、组合键持续时长和软件注入拒绝、离线跨日归属。
- Windows 真实短时测试：两块显示器遮罩成功显示/关闭；低级键鼠钩子成功安装；模拟 F24 按下/释放及零位移鼠标事件被拦截；3 秒安全截止释放；音频播放进度实际推进；线程的亮屏请求申请与释放均通过返回状态检查。
- 休息、统计、喝水三个实际 WPF 窗口均渲染为 PNG 并检查；修正进度条只读绑定、按钮文字对比度、历史表格列宽。
- 不宣称做过真实持续一小时的人工使用验证。紧急组合键完整8秒逻辑有单元测试，真实低级钩子有短时集成测试；没有自动模拟物理紧急组合键（软件注入有意不被接受）。
- 没有实测所有第三方后台任务、所有音频解码器、屏幕热插拔、合盖/休眠和多用户切换。
- 查询全机 `powercfg /requests` 需要提升的管理员命令行；没有为此修改权限。改用当前线程的 API 返回状态验证亮屏请求已释放。

## 审查修复

1. 喝水表单未提交时仍能进入下一轮，防止无限暂停计时。
2. 紧急解除不消耗之前未填写的喝水记录，状态非负。
3. 离线恢复统计按实际休息发生的日期归属，而非重启当天。
4. 全屏输入阻挡与 UI 分线程，回调不做磁盘 I/O，不记录输入文本。
5. 所有正常结束、错误和退出路径释放原生资源；心跳与绝对截止提供故障恢复。
6. JSON 反序列化后的值域与枚举校验，主文件损坏时保留证据并恢复备份。
7. 音频路径使用本地文件，不拼接 shell 命令；空列表有内置音乐回退。
8. 窗口可处理屏保消息，亮屏仅为临时线程请求，不永久改动电源计划。

## OWASP 安全检查

| 类别 | 检查结果 |
|---|---|
| A01 访问控制 | asInvoker 普通用户运行；仅当前会话；不修改系统安全策略；保留紧急解除和 OS 安全入口 |
| A02 加密与敏感数据 | 无账户、密码或密钥；健康习惯数据仅本地明文 JSON，由 Windows 用户目录权限保护 |
| A03 注入 | 无 SQL、浏览器模板或 shell 执行；CSV 日期/数值来自已校验类型；输入毫升范围校验 |
| A04 不安全设计 | 无普通跳过；预览不使用钩子；临时限制有倒计时、紧急恢复、心跳和绝对上限 |
| A05 安全配置 | 不要求管理员、不配置服务器/端口、不改变任务管理器和密码；开机启动须用户勾选 |
| A06 组件 | dotnet list package --include-transitive 无第三方包；使用本机 .NET 8 运行时，未将本机运行时宣称为已完成漏洞审计 |
| A07 认证 | 不涉及登录、认证和会话令牌；Windows 会话事件仅用于计时 |
| A08 完整性 | 类型化 JSON、版本和值域校验、文件大小上限、原子替换、上一份备份 |
| A09 日志 | 中断原因和异常日志可查；不记录键盘内容；保存失败可见提示 |
| A10 SSRF | 不发起网络请求，不接受远程音乐地址 |

无未处理的高危发现。限制和未实测的硬件场景已记录在 README。普通桌面应用并非系统级不可绕过锁定。

## 水杯目标更新（2026-09-28）

- 休息界面以多个水杯代替饮水数字大标题；每杯固定 300 ml，支持部分水位。
- 默认每日 6 杯（1800 ml），在“音乐与设置”中可调 1–12 杯。这是可自定的记录目标，不是统一医学建议。饮水需求随个人情况不同：[CDC](https://www.cdc.gov/healthy-weight-growth/water-healthy-drinks/index.html)。
- 蓝色部分表示已喝，空杯表示剩余。超过目标后所有杯子满水，原始喝水记录不会被截断。
- 记录窗口增加“一杯 300 ml”快捷项。已有数据缺少目标设置时自动采用默认值，其余记录保持不变。
- 验证：25项行为测试通过；构建零警告/错误；真实 WPF 预览已检查水位与布局。

本次安全复查：新增目标只接受1至12整杯；旧JSON保留默认值；没有更改输入拦截和电源逻辑。

## GitHub 源码发布与空闲暂停（2026-10-09）

关联任务：[#1](https://github.com/596150256/routine_rest/issues/1)。用户明确同意跳过 GitHub Project 看板；本次是空仓库的首次源码发布。

### 行为与实现

- 连续没有键鼠操作的前 300 秒仍计入工作，此后暂停累计。保留已有工作时长，再次操作时恢复，不补算闲置时间。
- 使用 Windows `GetLastInputInfo` 和匹配的 32 位单调时钟计算系统空闲时间，处理计数器回绕；重启应用不会再次给已闲置的会话增加 5 分钟。
- 对跨过暂停边界的采样区间进行截断；延迟采样后有新操作时，分别统计操作前宽限期和操作后的实际时长，保持跨午夜统计归属。
- 主窗口和托盘显示暂停状态。真正休息倒计时与锁屏/睡眠的自然休息处理保持独立。
- 不更改状态文件版本或字段，不清除已有工作、喝水和音量设置；不重新推算旧版产生的历史记录。
- README 改为面向其他 Windows 用户的 SDK 安装、源码下载、构建、运行、更新和排错说明。SDK 选择接受已安装的 .NET 8 功能版本。
- 删除之前用于发送二进制 ZIP 的脚本、运行时安装包、分享说明与 ZIP；仓库只提交源码、测试和文档。

### 验证

- RED：旧实现把半小时无操作计为 1800 秒，新回归测试期望 300 秒并失败。
- GREEN：33 项行为测试全部通过，包括 300 秒边界、半小时离开、恢复操作、累计 40 分钟后离开、跨日、重启和无效输入。
- 旧的锁屏、休息到期、紧急组合键、喝水和原子持久化回归测试通过。
- 日期测试改用运行机器的本地时区，可在 GitHub Windows runner 上执行。
- WPF Release 构建成功，Nullable 和 TreatWarningsAsErrors 开启。
- 不拦截输入的真实 WPF 预览成功导出三个窗口；检查了工作界面的新计时说明与布局。
- `dotnet list package --include-transitive` 确认没有第三方 NuGet 包。
- 尚未做 30 分钟实时人工等待验证，也没有重跑会暂时拦截用户输入的 smoke test；长时间与边界场景由确定性行为测试覆盖。

### 安全与代码审查

按 security-review 技能复查输入时间处理与首次公开发布。未记录输入文本、键值或鼠标位置，只读取最后输入时间。未修改钩子、紧急解除、权限或输入拦截的安全截止。可选空闲时间参数拒绝负数、NaN 和无穷值。

检查了注入、访问控制、敏感信息、数据完整性、错误处理、依赖和配置；认证、网络 API、数据库和 Web 模板不适用。源码未包含凭据、个人状态或安装包。CI 仅请求 contents:read，官方 Action 固定到已验证的提交，checkout 不保留凭据。

审查中修正了午夜后恢复活动的统计归属、重启时重复累计宽限时间、测试的固定时区依赖和 SDK 版本选择。无未处理发现。

**Review Status: COMPLETE. Unaddressed: 0.**

## 饮水显示与保存确认（2026-10-09）

关联任务：[#2](https://github.com/596150256/routine_rest/issues/2)。

- 休息页改为展示已记录的水量，不再绘制目标范围内的空杯。300 ml 显示一杯，150 ml 显示半杯，零饮水不显示已喝杯子；目标杯数用文字单独显示。
- 超过目标仍显示实际杯数和总量。最多绘制12杯，更多水量单独提示；用 long 计算向上取整，避免整数溢出和异常大记录造成巨量 UI 元素。
- 普通预览读取当前记录与饮水目标；开发用导出预览使用明确的300ml示例。
- 饮水记录同步持久化成功后才关闭表单。保存失败时恢复本次记录、当日水量、待填数量、阶段和检查点，表单保留并显示可重试提示；重试不重复累计。

验证：旧实现的回归测试失败（650ml期望3杯，实际6杯）；新版37项行为测试通过，覆盖一杯、半杯、零饮水、超目标、大整数、保存失败回滚和重试后的真实磁盘内容。WPF Release构建成功，实际300ml预览显示一杯及独立目标文字；饮水卡片扩大以容纳两排杯子。

安全复查按 security-review 技能检查用户输入、数据完整性和资源上限：继续限制本次饮水0–5000ml，不扩大数据文件权限，不改变状态版本；持久化仍使用原子替换和上一份备份。失败详情通过本机表单提示，不上传数据。没有新增第三方依赖。审查覆盖注入、访问控制、敏感信息、配置、组件、完整性与错误报告；网络、认证和数据库类别不适用。

未做真实磁盘损坏故障注入或长时间人工使用验证；保存失败由确定性异常测试覆盖。**Review Status: COMPLETE. Unaddressed: 0.**

## 空闲暂停的运行验证与保障（2026-10-09）

关联任务：[#4](https://github.com/596150256/routine_rest/issues/4)。

- 无新输入时间戳时，空闲时间取系统采样与单调累计经过时间的较大值。系统采样停滞或倒退不能再次开启5分钟宽限期。
- Windows空闲采样显式使用GetTickCount，与GetLastInputInfo使用同一32位时钟并处理回绕。
- 工作页显示连续无操作的分钟、秒数及暂停状态；已有主窗口即使隐藏也继续刷新，恢复显示时避免旧的倒计时或状态。
- 可选的 `--diagnostics` 每5秒原子更新一份时间状态快照，只记录时间、工作秒数、暂停状态和构建标识。普通运行不启用；写入失败不影响计时。

验证：旧代码在系统采样停在0时累计420秒，而期望300秒，回归测试失败。新版40项行为测试通过，包括“从40分钟开始，无操作7分钟后停在35分钟”、采样倒退、输入恢复及既有休息/饮水行为。Release构建成功。

本次还检查了实际Windows进程，而不是仅使用模拟时间：系统空闲超过300秒后，运行状态显示暂停；系统空闲从376秒继续增长到432秒期间，工作秒数保持完全相同。实际窗口显示“无操作已满5分钟 · 工作计时暂停”、固定倒计时和新增活动状态。诊断快照与磁盘保存检查点一致。

安全复查：只读取最后输入时间，不记录键值、输入内容或鼠标轨迹；不改变原生输入拦截和紧急解除。诊断文件位于忽略的app输出目录，不上传用户数据。新参数仍使用有限数值校验；没有新增依赖。检查输入处理、注入、权限、完整性、资源使用与失败路径，无未处理发现。**Review Status: COMPLETE. Unaddressed: 0.**

## 完整目标水杯与去除重复统计（2026-10-09）

关联任务：[#6](https://github.com/596150256/routine_rest/issues/6)。按用户最新要求，饮水卡片恢复显示完整目标杯数（默认6杯）：已喝部分填蓝色，未喝部分为空杯，同时保留明确的已喝毫升数、杯数和目标图例。“今天的节奏”删除重复的饮水项，只保留工作和休息两列。

目标可配置1–12杯。超过目标后所有目标杯填满，实际总量保留，额外水量单独提示。移除已不再使用的饮水摘要属性，没有更改记录、保存或计时逻辑。

验证：调整杯数期望后，旧实现失败（期望6杯，实际3杯）；更新后40项行为测试通过，覆盖300ml的一满五空、150ml的半杯与空杯、零饮水全部空杯、可配置目标和超目标的大整数。WPF Release构建成功，实际300ml预览确认六杯填色及两列节奏布局。

审查范围为显示映射和XAML布局，不涉及新增输入、权限或依赖。杯数仍严格限制1–12，计算没有分配无界资源；个人数据和既有设置保持不变。**Review Status: COMPLETE. Unaddressed: 0.**

## 统一数据目录与旧记录迁移（2026-10-10）

<!-- REVIEW:START -->
## Code Review Complete

Issue: #8
Scope: MAJOR (shared persistence location, single writer, legacy migration and history visibility)
Security-Sensitive: YES (local JSON input and persistence)
Reviewed: 2026-10-10

The executable's absolute base directory resolves one adjacent data directory, independent of the working directory and redirected AppData. A lifetime FileShare.None lease complements the existing named mutex across Windows package namespaces. Background duplicate launches exit without a dialog or writing default state.

Migration reads and validates all sources before creating raw backups and saving the destination atomically. It never overwrites an existing unified primary or backup. Timestamped water/release events are deduplicated; original event offsets determine historical dates. Newest live timer/settings are retained. Daily counters without event-level timing retain their largest value and expose a review marker in the UI and CSV. Source snapshots are not mutated.

Review findings fixed:
- Startup cleanup must not save an unloaded default state: stateReady gates persistence.
- Legacy aggregate water without matching events must remain represented; repeated migration is idempotent and checked arithmetic rejects overflow.
- Missing unified primary with a valid backup must restore that backup rather than reimport a legacy copy.
- Historical event dates must not shift after a machine timezone change.
- Background duplicate startup must exit quietly.

Clarity, maintainability, resource limits, failure paths, compatibility, documentation and style reviewed. Migration APIs have typed parameters and documented contracts. No changes to input guards, emergency release or power behavior.

Security review (security-review skill): injection, authentication, data exposure, access control, configuration, deserialization, components, logging and SSRF checked. No shell construction from snapshot contents, no new network access, no credentials, no elevated application privileges and no third-party packages. Snapshot size, field values, note length and integer overflow are bounded/validated. Personal state and binaries remain excluded from Git. Authentication/remote authorization/SQL/XML/Web templates are not applicable.

Validation: 50 behavior tests passed, WPF Release publish passed, migration CLI Release build passed with zero warnings/errors. File-lock and actual cross-context duplicate launch verified locally. Normal desktop restart preserved the live timer and migrated entries; diagnostics confirmed the same physical executable-adjacent data directory. Both push and PR Windows CI passed. Windows CI also builds the migration CLI.

**Review Status: COMPLETE. Unaddressed: 0.**
<!-- REVIEW:END -->
