; AF Media Bar 安装脚本 / AF Media Bar installer script
;
; 构建入口：installer\build-installer.ps1（本地与 CI 共用）。
; 版本号必须由外部传入，避免出现"安装包版本与程序集版本不一致"的静默错误。
; Build entry point: installer\build-installer.ps1 (shared by local builds and CI).
; The version must be supplied from outside so an installer can never silently disagree with the assembly version.

#ifndef MyAppVersion
  #error MyAppVersion is required. Build through installer\build-installer.ps1, or pass /DMyAppVersion=<version>.
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

; 简体中文的语言文件由 build-installer.ps1 解析后通过 /DChineseMessagesFile 传入。
;
; 它**不能**写成 "compiler:Languages\ChineseSimplified.isl"：简体中文属于 Inno Setup 的"非官方语言包"，
; 官方安装程序、winget 与 choco 安装都不带这个文件，于是 CI 上必然报
; Couldn't open include file "...\Languages\ChineseSimplified.isl"（这个失败真实发生过）。
; 解析顺序是：仓库缓存 installer\languages\ChineseSimplified.isl → 本机 Inno 安装目录 → 从上游
; kira-96/Inno-Setup-Chinese-Simplified-Translation 下载到该缓存目录；脚本对每一份被采用的文件校验固定的 SHA-256。
; 仓库缓存随版本库分发，因此单独调用 ISCC 时下面的默认值直接可用，不需要先跑一次脚本。
; The Simplified Chinese messages file is resolved by build-installer.ps1 and passed in as /DChineseMessagesFile.
;
; It must **not** be written as "compiler:Languages\ChineseSimplified.isl": Simplified Chinese lives in Inno Setup's "unofficial
; languages" set, which neither the official installer nor winget nor choco ships, so CI inevitably fails with
; Couldn't open include file "...\Languages\ChineseSimplified.isl" (a failure that really happened).
; The order is: the repository cache installer\languages\ChineseSimplified.isl, then the local Inno installation, then a download
; from the upstream kira-96/Inno-Setup-Chinese-Simplified-Translation into that cache; the script checks every adopted file
; against a pinned SHA-256. The cache ships with the repository, so a bare ISCC invocation can use the default below as it is.
#ifndef ChineseMessagesFile
  #define ChineseMessagesFile "languages\ChineseSimplified.isl"
#endif

#define MyAppName "AF Media Bar"
#define MyAppExeName "AFMediaBar.exe"
#define MyAppPublisher "AmorFate"
#define MyAppUrl "https://github.com/Fervent-Tempo/AF-Media-Bar"
; AppId 必须跨版本保持固定：它决定"升级同一份安装"还是"并存安装两份"，改动它会让自动更新变成重复安装。
; AppId must stay fixed across versions: it decides whether a new package upgrades the existing installation
; or installs a second copy beside it, so changing it would turn automatic updates into duplicate installs.
#define MyAppId "{{7C1B9E4C-5B2A-4E7E-9F1D-3A6C2E8B45D1}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (c) 2026 {#MyAppPublisher}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\AFMediaBar
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
; 默认按当前用户安装（不需要 UAC）；向导首屏允许改为"为所有用户安装"，那一档才会提权。
; The default is a per-user install that needs no UAC; the wizard's first page can switch to an all-users
; install, and only that choice elevates.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 程序自身的下限是 Windows 10 1809（17763）：用到的每个系统接口在该版本上就已可用。
; 注意 .NET 10 官方只支持 Windows 10 的长期服务版与企业版（1809 E、21H2 E），因此下限写在这里只是"能装能跑"，
; 并不等于消费版 Windows 10 处于 Microsoft 的支持范围内（见 README 的「系统要求」）。
; The application's own floor is Windows 10 1809 (17763): every system interface it uses already exists there.
; Note that .NET 10 officially supports only the Windows 10 LTSC and Enterprise editions (1809 E, 21H2 E), so this floor means
; "installs and runs" rather than putting consumer Windows 10 inside Microsoft support (see the README's requirements section).
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=AFMediaBar-Setup-v{#MyAppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\AFMediaBar\Assets\icon_dark.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; 升级必须落回原目录，否则静默更新会把程序装到第二个位置。
; An upgrade must land in the existing directory, otherwise a silent update installs a second copy.
UsePreviousAppDir=yes
; 运行中的应用由 Restart Manager 关闭；Setup 自己不去重启它，"重启"由 AUTORELAUNCH 参数与程序侧决定。
; A running instance is closed through the Restart Manager; Setup never restarts it itself, because the
; relaunch contract belongs to the AUTORELAUNCH parameter and the application.
CloseApplications=yes
RestartApplications=no
; 命名互斥体用于交互式安装/卸载时识别正在运行的程序；自动更新在启动 Setup 前会先释放它（见 installer\README.md）。
; The named mutex lets interactive installs and uninstalls notice a running instance; the automatic update
; path releases it before starting Setup (see installer\README.md).
AppMutex=AFMediaBar.InstallCoordinator
SetupLogging=yes

[Languages]
; 简体中文与英文两种向导语言，交互式安装时由用户选择；静默安装（自动更新）不显示该对话框，
; 由 Inno 按系统语言匹配，匹配不到时用列表里的第一种。
; Simplified Chinese and English wizard languages, chosen by the user during an interactive install. A silent
; install (the automatic update) never shows that dialog: Inno matches the system language and otherwise falls back
; to the first entry in this list.
;
; 注意 MessagesFile 的对应关系：英文用编译器自带的 Default.isl，中文必须指向简体中文的语言文件（路径见文件开头的说明）。
; 早期版本把简体中文也指向 Default.isl，于是"中文"向导里的所有内建文案（按钮、页面标题、任务与图标说明）
; 全是英文。
; Note which MessagesFile each language uses: English uses the compiler's own Default.isl while Chinese must point
; at the Simplified Chinese messages file (see the note at the top of this file for its path). An earlier version pointed
; the Chinese entry at Default.isl as well, so every built-in string (buttons, page titles, task and icon descriptions)
; came out in English inside the "Chinese" wizard.
Name: "chinesesimplified"; MessagesFile: "{#ChineseMessagesFile}"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Setup]
; 交互式安装总是先问语言，"提供中英文选项"才是真的可选项；静默安装不受影响。
; An interactive install always asks for the language first, which is what turns "offering Chinese and English" into
; a real choice; silent installs are unaffected.
ShowLanguageDialog=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 交互式安装完成页的"启动"勾选项；静默安装不显示完成页，因此显式跳过。
; The "launch" checkbox on the interactive finished page; a silent install has no finished page, so it is skipped explicitly.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; 自动更新路径：只有调用方明确要求重启时才重新启动程序。
; Automatic update path: the app is only restarted when the caller explicitly asked for it.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: ShouldRelaunchAfterUpdate

[Code]
/// <summary>
/// 判断本次安装是否由程序的"立即重启并安装"发起：只有显式传入 AUTORELAUNCH=1 才在安装完成后启动程序。
/// 程序正常退出触发的静默安装不带该参数，因此装完不会把用户刚关掉的窗口再拉起来。
/// Decides whether this run was started by the app's "restart and install now" action: the app is only
/// started afterwards when AUTORELAUNCH=1 was passed explicitly. The silent install that runs when the user
/// simply quits carries no such parameter, so a window the user just closed does not come back.
/// </summary>
function ShouldRelaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:AUTORELAUNCH|0}') = '1';
end;
