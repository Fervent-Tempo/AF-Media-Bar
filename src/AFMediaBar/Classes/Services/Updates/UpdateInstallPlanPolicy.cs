using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 是否允许启动安装包，以及应当如何启动。
/// Whether the installer may be started, and how.
/// </summary>
/// <param name="CanInstall">当前是否可以安装。/ Whether installing is possible right now.</param>
/// <param name="UseRunAs">机器级安装且当前未提权时为 true：需要一次 UAC 提权。/ True for a machine-wide install from a non-elevated process, which needs one UAC prompt.</param>
/// <param name="BlockedReason">不能安装时的原因；可安装时为 null。/ Reason shown when installing is impossible, or null.</param>
public sealed record UpdateInstallDecision(bool CanInstall, bool UseRunAs, string? BlockedReason);

/// <summary>
/// 静默安装的参数与前置条件策略。
///
/// 参数串是与安装程序的公开契约（见 <c>installer/README.md</c>）：<c>/SILENT</c> 不显示向导但保留进度窗口，
/// 因此不用 <c>/VERYSILENT</c>——用户要求能看到安装进度。是否在装完后启动程序由 <c>AUTORELAUNCH</c> 决定：
/// 启动前安装和"立即重启并安装"均传 1；普通退出不启动安装程序。
/// Arguments and preconditions for the silent install.
///
/// The argument string is the public contract with the installer (see <c>installer/README.md</c>): <c>/SILENT</c>
/// hides the wizard but keeps the progress window, which is why <c>/VERYSILENT</c> is not used — the update is
/// supposed to be observable. Both startup installation and "restart and install now" pass <c>AUTORELAUNCH=1</c>.
/// A normal quit does not start the installer.
/// </summary>
public static class UpdateInstallPlanPolicy
{
    /// <summary>
    /// 便携版不能自动安装：没有安装记录，也就没有可以原位替换的目录。
    ///
    /// 它是属性而不是常量：常量会在编译期把某一种语言写死进调用方，而这一句会出现在设置页的状态行上，必须跟着
    /// 界面语言走。<c>UpdateState.InstallBlockedReason</c> 保存的是做出判断时那一刻的文案，因此界面比较这两者时
    /// 要使用同一个语言的取值。
    /// A portable copy cannot be installed automatically, having no installation to replace in place.
    ///
    /// It is a property rather than a constant: a constant would bake one language into every caller at compile time,
    /// while this sentence appears on the settings status line and has to follow the interface language.
    /// <c>UpdateState.InstallBlockedReason</c> holds the wording as it was when the decision was made, so a surface
    /// comparing the two has to read them in the same language.
    /// </summary>
    public static string PortableBlockedReason => Translations.Get("Update.Install.Blocked.Portable");

    /// <summary>其它实例在运行时推迟安装，避免两个实例争抢同一份设置与托盘图标。/ Installing is deferred while other instances run, so two instances cannot fight over settings and the tray icon.</summary>
    public static string OtherInstancesBlockedReason => Translations.Get("Update.Install.Blocked.OtherInstances");

    /// <summary>
    /// 判断一个已经发布的原因文本是否就是"便携版"这一条。
    ///
    /// 界面不能只把它与当前语言的取值比一次：<c>UpdateState.InstallBlockedReason</c> 保存的是**做出判断那一刻**的文案，
    /// 而用户可以在那之后切换界面语言，此时两种取值属于不同语言，比较会失败，便携版提示就会在切换语言后突然消失。
    /// 因此这里对三种语言的取值都比一次，只回答"是不是这一条原因"，不参与任何显示。
    /// Decides whether a published reason text is the portable one.
    ///
    /// A surface cannot compare it against the active language alone: <c>UpdateState.InstallBlockedReason</c> holds the wording
    /// of the *moment the decision was made*, and a user may switch the interface language afterwards, at which point the two
    /// values belong to different languages, the comparison fails, and the portable notice silently disappears. This compares
    /// against all three languages and answers only "is this that reason", taking no part in display.
    /// </summary>
    /// <param name="reason">已发布的原因文本；为 null 或空时返回 false。/ The published reason text; null or empty yields false.</param>
    public static bool IsPortableBlockedReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return false;
        }

        foreach (var language in Enum.GetValues<LocalizationLanguage>())
        {
            if (string.Equals(
                    Translations.Get("Update.Install.Blocked.Portable", language),
                    reason,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 组装静默安装参数。
    /// Builds the silent install arguments.
    /// </summary>
    /// <param name="relaunchAfterInstall">安装完成后是否启动新版本。/ Whether to start the new version afterwards.</param>
    /// <param name="logPath">安装日志路径；会被引号包裹以容忍空格。/ Install log path, quoted so spaces are tolerated.</param>
    /// <returns>完整参数串。/ Complete argument string.</returns>
    public static string BuildArguments(bool relaunchAfterInstall, string logPath) =>
        "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS " +
        $"/AUTORELAUNCH={(relaunchAfterInstall ? "1" : "0")} " +
        $"/LOG=\"{logPath}\"";

    /// <summary>
    /// 决定现在能否安装。
    /// Decides whether installing is possible now.
    /// </summary>
    /// <param name="isInstalledCopy">当前进程是否来自安装程序安装的目录。/ Whether the running process lives in a directory created by the installer.</param>
    /// <param name="isMachineWide">该安装是否为所有用户（注册表项在 HKLM）。/ Whether the installation is machine-wide, which puts its registry entry under HKLM.</param>
    /// <param name="isElevated">当前进程是否已提权。/ Whether the current process is elevated.</param>
    /// <param name="otherInstanceCount">其它正在运行的程序实例数量。/ Number of other running instances of the application.</param>
    /// <returns>是否可以安装、是否需要提权，以及不能安装时的原因。/ Whether to install, whether to elevate, and the reason when blocked.</returns>
    public static UpdateInstallDecision Decide(
        bool isInstalledCopy,
        bool isMachineWide,
        bool isElevated,
        int otherInstanceCount)
    {
        if (!isInstalledCopy)
        {
            return new UpdateInstallDecision(false, false, PortableBlockedReason);
        }

        if (otherInstanceCount > 0)
        {
            return new UpdateInstallDecision(false, false, OtherInstancesBlockedReason);
        }

        // 机器级安装写的是 Program Files，未提权时只能请用户确认一次。
        // A machine-wide installation writes into Program Files, so a non-elevated process must ask once.
        return new UpdateInstallDecision(true, isMachineWide && !isElevated, null);
    }
}
