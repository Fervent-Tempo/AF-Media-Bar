# 安装程序（Inno Setup 6）

本目录是 AF Media Bar 的安装包工程。它把 `src/AFMediaBar` 的单文件自包含发布结果打包成一个可分发的 `AFMediaBar-Setup-v<版本>-win-x64.exe`，并定义自动更新使用的静默安装契约。

## 1. 构建

```powershell
# 完整链路：restore -> 单文件发布 -> Inno 编译 -> 输出哈希
.\installer\build-installer.ps1

# 复用已有 artifacts\publish（CI 中已发布过一次时）
.\installer\build-installer.ps1 -SkipPublish

# 找不到编译器时用 Chocolatey 安装（CI 使用）
.\installer\build-installer.ps1 -EnsureIscc
```

| 参数 | 默认值 | 说明 |
|---|---|---|
| `-Version` | 读 `AFMediaBar.csproj` 的 `<Version>` | 安装包版本；不传则与程序集版本同源 |
| `-PublishDir` | `artifacts\publish` | 单文件发布目录，对应 `AFMediaBar.iss` 的 `PublishDir` |
| `-OutputDir` | `artifacts` | 安装包与 `installer-sha256.txt` 的输出目录 |
| `-IsccPath` | 自动查找 | `ISCC.exe` 路径；查找顺序 PATH → `Program Files (x86)\Inno Setup 6` → `Program Files` → `%LOCALAPPDATA%\Programs` |
| `-SkipPublish` | 关 | 不重新发布，直接使用现有发布目录 |
| `-NoRestore` | 关 | 跳过 restore（要求调用方已用同一 RID restore） |
| `-EnsureIscc` | 关 | 找不到编译器时尝试 `choco install innosetup` |

要求：Windows 10 1809 或更高、.NET 10 SDK、[Inno Setup 6](https://jrsoftware.org/isinfo.php) **6.3 或更高版本**（`ArchitecturesAllowed=x64compatible` 需要 6.3+；安装方式 `winget install --id JRSoftware.InnoSetup -e`，CI 用 `choco install innosetup`）。脚本会校验发布目录里恰好只有 `AFMediaBar.exe`；发布模式一旦退化（例如丢失 `PublishSingleFile`）就在这里失败，而不是在安装包里悄悄塞进整个运行时。

**简体中文语言文件**：`ChineseSimplified.isl` 属于 Inno Setup 的"非官方语言包"，官方安装程序、winget 与 choco **都不带**它。文件**随仓库分发**在 `installer\languages\ChineseSimplified.isl`。


安装包的下限是 `MinVersion=10.0.17763`（Windows 10 1809）：这是**程序自身**的下限，与 README 的口径一致。注意 .NET 10 官方只支持 Windows 10 的长期服务版与企业版（1809 E、21H2 E），消费版 Windows 10 不在 .NET 支持列表内，因此这个下限表示"能装能跑"，不等同于"受 Microsoft 支持"。

产物：

```text
artifacts\AFMediaBar-Setup-v<版本>-win-x64.exe
artifacts\installer-sha256.txt        # "<sha256>  AFMediaBar-Setup-v<版本>-win-x64.exe"
```

## 2. 安装行为

| 项 | 值 |
|---|---|
| 默认目录 | `{autopf}\AFMediaBar`；`PrivilegesRequired=lowest` 时解析为 `%LOCALAPPDATA%\Programs\AFMediaBar` |
| 安装范围 | 默认当前用户（无需 UAC）；向导首屏可改为「为所有用户安装」，那一档才提权并装到 `Program Files` |
| 向导语言 | 简体中文与英文，交互式安装时由用户选择（`ShowLanguageDialog=yes`）；英文用编译器自带的 `Default.isl`，中文用 `Languages\ChineseSimplified.isl`。静默安装不显示该对话框，需要固定语言时传 `/LANG=chinesesimplified` 或 `/LANG=english` |
| 安装位置 | 用户可选（`DisableDirPage=no`）；升级时 `UsePreviousAppDir=yes` 回到原目录 |
| 快捷方式 | 开始菜单始终创建；桌面快捷方式为可选任务（默认不勾选） |
| 卸载项 | 显示名 `AF Media Bar`，图标取 `{app}\AFMediaBar.exe` |
| 卸载范围 | 删除 `{app}` 与快捷方式，并清理卸载进程当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AFMediaBar` 值（仅当它指向本次安装的程序）；不处理其他用户的注册表，**不**删 `%LOCALAPPDATA%\AFMediaBar`（用户设置与已下载安装包保留） |
| 应用标识 | `AppId={{7C1B9E4C-5B2A-4E7E-9F1D-3A6C2E8B45D1}`，跨版本固定 |

### 卸载启动项验收

使用包含本次修复的新安装包，在测试用户或虚拟机中验证（旧安装包生成的卸载程序不会自动获得此逻辑）：

- 开启开机自启动后卸载，确认 `Run` 下的 `AFMediaBar` 值被删除，其他启动项与用户设置保留；覆盖含空格的安装路径。
- 关闭开机自启动后卸载，确认缺少该值时仍能正常完成。
- 将该值改为指向另一个目录的程序，确认卸载后该值保留；仅文件名相同或路径前缀相同不能触发删除。
- 覆盖带引号、路径大小写不同及带参数的启动命令；未闭合的引号应保留该值。
- 取消卸载以及覆盖安装升级后，确认启动项保留；分别验证交互式与静默卸载。
- 全用户安装的卸载只处理卸载进程当前用户的 `HKCU`；使用其他管理员凭据提权时，原用户及其他用户的启动项仍需另行处理。

## 3. 静默安装与自动更新契约

程序更新时由应用本身启动安装包，参数固定为：

```text
AFMediaBar-Setup-vX.Y.Z-win-x64.exe /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /AUTORELAUNCH=<0|1> /LOG="<日志路径>"
```

- `/SILENT`：不显示向导页、不提问，但保留安装进度窗口。**不用 `/VERYSILENT`**：本轮需求要求用户能看到安装进度，而 `/VERYSILENT` 会把进度窗也一并隐藏。
- `/AUTORELAUNCH=1`：安装完成后启动 `{app}\AFMediaBar.exe`（对应 `[Code] ShouldRelaunchAfterUpdate`）。两条更新路径都传 `1`：无论是"下一次启动前自动安装"还是用户点的"立即重启并安装"，安装完成的下一步都是把程序启动起来。
- `/LOG`：把安装日志写到程序自己的更新目录，失败时由更新状态直接指向该文件。
- 安装包**不会**创建开机启动项，也不会读写用户设置。

### 3.1 安装时机

- 自动更新的默认路径是**下一次启动之前**安装：程序启动时发现待安装文件，立即启动安装包并结束本次启动，因此用户看到的顺序是「打开程序 → 安装进度 → 新版本启动」。安装程序窗口不会出现在用户刚退出程序之后。
- 只有用户明确点击「立即重启并安装」时，才会在退出边界上启动安装包。
- 启动安装包之前会清掉待安装记录：否则安装完成后重新启动的新版本可能再次读到它并再装一遍——只要清单声明的版本高于程序集版本（发布侧写错版本号就会这样），每次启动都会启动一次安装程序，程序于是永远打不开。代价是安装失败后需要重新下载，这比无限安装循环好得多。

### 3.2 命名互斥体 `AFMediaBar.InstallCoordinator`

- 程序在启动时创建该命名互斥体（仅用于安装协调，**不**用于限制单实例，行为与旧版本一致）。
- 安装包声明 `AppMutex=AFMediaBar.InstallCoordinator`，使交互式安装/卸载在程序运行时给出"请先关闭"的提示，而不是在文件占用上失败。
- 程序在启动安装包之前**先释放**该互斥体并锁存一次性标记：Inno 在启动阶段检查互斥体，静默模式下若互斥体仍存在会直接退出；释放后即不存在这个竞态，锁定标记则保证即使安装包随后通过 Restart Manager 关闭了本进程，也不会再启动第二个安装包。
- 同一时刻只应有一个更新安装：程序侧在检测到其它 `AFMediaBar` 实例运行时推迟安装，Inno 自己的 `SetupMutex` 负责兜住并发安装包。

### 3.2 需要的写权限

静默安装必须能写安装目录。当前用户安装无需提权；「为所有用户安装」的机器级安装需要提权，程序会以 `runas` 启动安装包（出现一次 UAC），用户取消则维持"更新已就绪"状态。

