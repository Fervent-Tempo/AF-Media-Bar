using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算窗口请求材质在当前系统环境下的有效材质。
/// Resolves the effective window backdrop for the requested mode and current system environment.
/// </summary>
public static class WindowBackdropPolicy
{
    /// <summary>
    /// 在高对比度或不支持 Mica 的系统上回退到 FluentSolid。
    /// Falls back to FluentSolid for high contrast or systems that do not support Mica.
    /// </summary>
    public static ApplicationBackdropMode Resolve(
        ApplicationBackdropMode requested,
        bool highContrast,
        bool supportsMica)
    {
        if (highContrast || requested == ApplicationBackdropMode.Mica && !supportsMica)
            return ApplicationBackdropMode.FluentSolid;

        return requested;
    }
}
