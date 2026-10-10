# Routine Rest · 规律休息

Windows 桌面休息工具：累计工作 50 分钟后，自动进入 10 分钟全屏休息。连续 3 分钟没有键盘或鼠标操作时暂停工作计时，再次操作后继续累计。

本仓库提供源码。下载或克隆后需要先构建，生成的程序位于 `app/RoutineRest.exe`。

## 下载源码后如何使用

### 1. 安装 .NET 8 SDK

在 Windows 10 / 11 电脑上打开微软官方 [.NET 8 下载页面](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)，找到 **Build apps — SDK → Windows → x64**，下载安装最新的 .NET 8 SDK。普通 Intel / AMD 64 位电脑选择 x64；Windows ARM 电脑选择 Arm64 SDK。

**这里需要 SDK，只有 Desktop Runtime 无法编译源码。** Windows 版 SDK 已包含相应的桌面运行时，无需再单独安装 Desktop Runtime。

安装完成后重新打开 PowerShell，输入：

```powershell
dotnet --list-sdks
```

输出中应包含 `8.0.xxx`。仓库的 `global.json` 会选择已安装的 .NET 8 SDK，电脑上只有 .NET 9 / 10 SDK 时仍需要安装 .NET 8 SDK。

### 2. 获取源码

已安装 Git 的用户可以运行：

```powershell
git clone https://github.com/596150256/routine_rest.git
cd routine_rest
```

没有 Git 也可以在仓库页面选择 **Code → Download ZIP**，完整解压源码，然后在包含 `README.md`、`global.json` 和 `src` 的项目根目录打开 PowerShell。

### 3. 构建并运行

在项目根目录依次运行：

```powershell
dotnet publish src/RoutineRest.App/RoutineRest.App.csproj -c Release --self-contained false -o app
.\app\RoutineRest.exe
```

构建完成后，以后可以直接打开 `app` 文件夹，双击 `RoutineRest.exe`，不用每次重新构建。保留 `app` 里的全部文件。

项目没有第三方 NuGet 包依赖。SDK 安装好后，构建不需要额外下载应用依赖，也不要求安装 Visual Studio。

## 日常使用

- 首次键鼠操作后开始工作计时。
- 连续无操作的前 3 分钟仍计入工作；满 3 分钟后暂停，保留已累计时间。再次操作后继续，不补算离开电脑的时间。
- 例如：已工作 20 分钟后离开电脑半小时，累计停在约 23 分钟，不会把整段离开时间算进去。
- 累计满 50 分钟后，自动进入 10 分钟休息，无需点击。覆盖已连接的屏幕，期间限制普通桌面键鼠操作；后台编译、下载和计算继续运行。
- **紧急解除：同时按住 Ctrl + Shift + F12，连续 8 秒。** 提前松开会重新计时，中断会单独记入统计。
- 休息到期后恢复输入、停止音乐，并打开喝水量记录窗口。暂时不填也可以继续工作，之后从托盘菜单补填。
- 关闭主窗口后程序仍在托盘运行；双击绿色叶子图标打开统计和设置。需要完全退出时使用托盘菜单。
- 可以先选择“预览休息界面”熟悉界面。预览不限制输入，可按 Escape 或关闭窗口退出。“现在开始休息”会确认后进入真正的休息。

## 音乐与饮水设置

- 音乐只在休息时播放，结束时自动停止。默认播放内置原创环境音，也可导入 MP3 / WAV / M4A / WMA 本地歌曲，按列表循环。
- 在“音乐与设置”中调整播放音量。首次运行默认 22%，如果希望更容易听到休息开始和结束，可以调到 80%。每台电脑独立保存自己的设置。
- 每杯按 300 ml 显示饮水进度，按每日目标绘制全部杯子，默认 6 杯，可调整为 1–12 杯。蓝色部分为已喝，空杯为剩余目标：300 ml 显示一满杯和五空杯，150 ml 显示一半杯和五空杯，未记录时显示六空杯。支持每次记录 0–5000 ml；这是可自行设置的记录目标。
- 休息页右侧饮水卡片显示今日已喝的毫升数与杯数，目标与图例另列说明。超过目标后全部目标杯填满，额外饮水量仍计入总量并用文字提示；“今天的节奏”只保留工作和休息统计。普通界面预览使用当前记录。
- 喝水量保存到磁盘成功后才关闭记录窗口；保存失败会保留表单并显示提示，可重试，不重复累计。
- 开机启动在设置中自行启用，默认关闭。

## 计时规则

- 工作时间和无操作时间使用单调时钟，不受系统日期、时间修改影响。
- 连续无操作满 3 分钟只暂停工作累计，不把普通闲置自动记为完成休息。
- 工作页会显示连续无操作的分钟与秒数，达到 `03:00` 后显示暂停。系统空闲时间采样停滞或倒退时，无新输入的经过时间仍会累计，不能重新开始宽限期。
- 短暂锁屏暂停工作并保留累计；连续锁屏、睡眠或会话断开满 10 分钟，可以记为自然休息并开启下一轮。
- 真正休息的倒计时不会因没有键鼠操作而暂停。
- 重启应用保留已累计工作时间，结合系统的最后输入时间恢复暂停状态；关闭应用期间不补算工作时间。
- 真实休息期间保持屏幕亮起；结束后释放亮屏请求。程序不修改全局屏保或电源计划。

## 数据保存与更新

数据保存在 EXE 旁的 `data` 文件夹。例如本项目构建后的路径为：

```text
app\data\state.json
```

同一个 EXE 的 Windows 登录自启动、直接双击和其他启动方式共用这一份文件，不使用当前工作目录，也不会因启动父进程的 AppData 重定向而分成多份。设置页显示绝对路径。程序用文件锁保证同一数据目录只能由一个实例写入；目录不可写时提示启动失败，不偷偷换目录创建第二套记录。请放在当前用户可写的目录中使用。

包括设置、每日工作/休息统计、喝水记录及紧急解除记录。所有数据留在本机，不上传，不记录键盘输入内容。每 5 秒和重要状态变化后保存，原子替换文件并保留上一份备份；主窗口可导出每日 CSV。

更新源码后，先从托盘退出应用，再重新执行构建命令，然后启动 `app/RoutineRest.exe`。**保留 `app/data` 文件夹**；换到另一个程序目录时，把整个 `data` 文件夹一起复制。它包含个人记录，给别人分享程序时不要带上这个文件夹。下载新版本后不会自动重算历史统计。

首次启动且统一记录不存在时，会复制当前可见的旧 `%LOCALAPPDATA%\RoutineRest\state.json`，原文件不修改，原始副本保存在 `data/migration-backups`。统一记录一旦存在，旧文件不会再次导入或覆盖它。旧记录损坏时停止迁移并提示，不以空白记录覆盖。

如果过去有多份旧记录，先完全退出应用，把各份 `state.json` 复制到普通可写目录，再在统一记录尚不存在时运行迁移工具：

```powershell
dotnet run --project tools/RoutineRest.Migrate -c Release -- "D:\Projects\routine_rest\app\data" "D:\backup\desktop.json" "D:\backup\cache.json"
```

迁移会保留所有日期、最新计时和设置，按时间及毫升数去重饮水记录。缺少事件明细的旧工作/休息累计不能可靠相加：同一天保留较大值，并在历史表中用 `*` 标记需要核对；CSV 也有相应列。所有来源文件保留在迁移备份中。迁移工具拒绝覆盖已经存在的统一记录。

## 常见问题

**提示找不到 `dotnet`：** 安装 .NET 8 SDK 后重新打开终端，检查 `dotnet --list-sdks`。

**提示找不到指定 SDK：** 确认安装了 .NET 8 SDK，而不只是 Runtime。`global.json` 支持已安装的 .NET 8 各个 SDK 功能版本。

**构建提示文件被占用：** 先从托盘退出正在运行的 Routine Rest，再重新构建。

**PowerShell 不允许运行 `build.ps1`：** 直接使用上面的 `dotnet publish` 命令即可，不需要更改系统执行策略。

**启动后看不到窗口：** 检查系统托盘，双击绿色叶子图标。应用只能同时运行一个正常实例。

**音乐无法播放：** 更换本地音乐文件或恢复内置环境音；格式支持取决于电脑上的 Windows 媒体解码器。音频失败不影响休息到期。

## 开发与验证

```powershell
# 完整行为测试：使用独立测试目录，不修改个人记录。
dotnet run --project tests/RoutineRest.Tests/RoutineRest.Tests.csproj -c Release

# 构建桌面程序。
dotnet publish src/RoutineRest.App/RoutineRest.App.csproj -c Release --self-contained false -o app

# 生成界面预览并退出，不限制输入。
.\app\RoutineRest.exe --preview
```

也可使用根目录的 `test.ps1` 和 `build.ps1`。GitHub Actions 在 Windows 上自动执行行为测试和 Release 构建。

项目结构：

```text
src/RoutineRest.Core/    50/10 状态机、空闲暂停、统计、饮水、状态保存
src/RoutineRest.App/     WPF 界面、托盘、音乐、Windows 输入与电源接口
tests/RoutineRest.Tests/ 无第三方测试框架依赖的行为测试
docs/REVIEW.md          验证与审查记录
app/                   本机构建输出，不提交到仓库
```

开发用 `--smoke-test` 会真实限制输入约 3 秒，验证钩子、安全截止、音乐、遮罩与亮屏释放，并写出 `smoke-result.txt`；使用独立测试数据。日常试用请用界面里的“预览休息界面”。

排查计时问题时可使用 `RoutineRest.exe --diagnostics`。它每5秒覆盖程序旁的 `timing-diagnostics.json`，仅保存当前空闲秒数、工作秒数、暂停状态和构建标识，不记录输入内容；普通启动不生成此文件。

## Windows 系统边界

普通用户桌面应用无法阻止 Ctrl + Alt + Delete 安全桌面、管理员终止进程、其他用户会话或重启。键鼠拦截设有紧急解除、心跳和时间上限，异常或退出时清理限制。依赖桌面输入注入的自动化可能在休息期间受影响；Windows 原始输入、特殊驱动和安全桌面不在普通钩子的封锁范围内。
