using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 封装窗口背景材质、DWM 属性、AccentPolicy 和 Popup Z 序的原生适配。
/// Encapsulates native adaptation for window backdrops, DWM attributes, AccentPolicy, and Popup Z-order.
/// </summary>
public sealed class NativeWindowBackdropAdapter
{
    private readonly Func<nint, int, int, bool>? _attributeOverride;
    private readonly Func<nint, bool, bool>? _frameOverride;
    private readonly int _windowsBuild = Environment.OSVersion.Version.Build;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmSystemBackdropType = 38;
    private const int DwmMicaEffect = 1029;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMica = 2;
    private const int DwmBackdropAcrylic = 3;
    private const int DwmBackdropMicaAlt = 4;
    private const int DwmCornerRound = 2;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// 当前系统可用的深色属性号：Windows 10 18985 起为 20，更早的版本只有 19；首次失败后自动改写为可用的那个。
    /// The dark-mode attribute number this system accepts: 20 from Windows 10 18985 on, 19 on anything earlier; after a failure it is
    /// rewritten to whichever one worked.
    /// </summary>
    private int _immersiveDarkModeAttribute = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985)
        ? DwmUseImmersiveDarkMode
        : DwmUseImmersiveDarkModeLegacy;

    /// <summary>
    /// 创建原生窗口背景适配器。
    /// Creates the native window backdrop adapter.
    /// </summary>
    public NativeWindowBackdropAdapter()
    {
    }

    // 在回归测试中注入原生失败结果，覆盖新旧 DWM 路径而不依赖机器实际材质能力。
    internal NativeWindowBackdropAdapter(int windowsBuild, Func<nint, int, int, bool> attribute, Func<nint, bool, bool> frame)
    {
        _windowsBuild = windowsBuild;
        _attributeOverride = attribute;
        _frameOverride = frame;
    }

    /// <summary>
    /// 清除句柄上的所有系统背景材质。
    /// Clears all system backdrop materials from the window handle.
    /// </summary>
    public void ResetBackdrop(nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        SetDwmAttribute(handle, DwmSystemBackdropType, DwmBackdropNone);
        SetDwmAttribute(handle, DwmMicaEffect, 0);
        _ = ApplyAccentPolicy(handle, AccentState.Disabled, 0);
    }

    /// <summary>
    /// 按 Windows 版本应用 Mica、Mica Alt、Acrylic 或兼容的旧版材质。
    /// Applies Mica, Mica Alt, Acrylic, or a compatible legacy material according to the Windows version.
    /// </summary>
    /// <param name="handle">窗口句柄。/ Window handle.</param>
    /// <param name="mode">已经过 <see cref="WindowBackdropPolicy.Resolve"/> 回退的有效材质。/ Effective backdrop already run through <see cref="WindowBackdropPolicy.Resolve"/>.</param>
    /// <param name="tint">Accent 模糊路径的 ARGB 底色（来自材质浓度）。/ ARGB tint for the Accent blur path, taken from the material concentration.</param>
    /// <returns>必要的边距和材质调用成功时为 true；DWM 或 Accent 失败时调用方必须回退纯色。</returns>
    public bool ApplyBackdrop(nint handle, ApplicationBackdropMode mode, int tint)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        ResetBackdrop(handle);

        if (_windowsBuild >= 22621)
        {
            return SetFrame(handle, extended: true) && SetDwmAttribute(handle, DwmSystemBackdropType, mode switch
            {
                ApplicationBackdropMode.Mica => DwmBackdropMica,
                ApplicationBackdropMode.MicaAlt => DwmBackdropMicaAlt,
                _ => DwmBackdropAcrylic
            });
        }

        // 22000–22620 上只有旧式云母属性，没有云母 Alt；此处按云母处理，回退链已在上层策略里走过一遍。
        // Only the legacy Mica attribute exists on 22000-22620 and Mica Alt does not; it is treated as Mica here, the fallback
        // chain having already run in the policy above.
        if (mode is ApplicationBackdropMode.Mica or ApplicationBackdropMode.MicaAlt)
        {
            return SetFrame(handle, extended: true) && SetDwmAttribute(handle, DwmMicaEffect, 1);
        }

        return SetFrame(handle, extended: false) && ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    /// <summary>
    /// 为不获取焦点的短暂窗口应用材质。
    ///
    /// Windows 11 的三种系统材质（云母、亚克力、云母 Alt）由 DWM 采样桌面壁纸绘制，而 DWM 只为**前台**窗口绘制它们：
    /// 实测（26200）同一个非激活窗口上三种材质都是单色填充——窗口内亮度标准差 0.0，把同一个窗口激活后同一位置变成 262 种颜色、
    /// 标准差 14.4，并跟随背后壁纸的明暗。请求云母也一样退化成单色，因此"非激活窗口不要用系统材质"不是亚克力的特例，
    /// 而是所有系统材质的共同前提。
    ///
    /// 所以永不激活的窗口统一走 Accent 模糊路径：这条路径由窗口自己绘制，实测在非激活状态下依然在模糊背后的内容
    /// （窗口内亮度标准差 2.1–7.4，随浓度变化，而背后壁纸是 42.5），浓度由材质浓度设置给出。
    /// Applies a backdrop to a non-activating transient window.
    ///
    /// Windows 11's three system materials (Mica, Acrylic, Mica Alt) are painted by DWM from the desktop wallpaper, and DWM only
    /// paints them for the **foreground** window: on one and the same non-activated window all three measured as a flat fill — a
    /// luminance deviation of 0.0 inside the window, while activating that same window turned the same spot into 262 colours with a
    /// deviation of 14.4 that tracked the wallpaper behind it. Requesting Mica degrades just as much, so "no system material on a
    /// non-activating window" is a property of every system material rather than an Acrylic special case.
    ///
    /// Windows that never activate therefore all use the Accent blur path: that path is painted by the window itself and measured
    /// to keep blurring the content behind it while non-activated (a luminance deviation of 2.1-7.4 inside the window depending on
    /// concentration, against 42.5 for the wallpaper behind it), with its concentration coming from the material-concentration
    /// setting.
    /// </summary>
    /// <param name="handle">窗口句柄。/ Window handle.</param>
    /// <param name="mode">请求的有效材质；<see cref="ApplicationBackdropMode.FluentSolid"/> 由调用方处理。/ Requested effective backdrop; <see cref="ApplicationBackdropMode.FluentSolid"/> is handled by the caller.</param>
    /// <param name="tint">Accent 模糊路径的 ARGB 底色（来自材质浓度）。/ ARGB tint for the Accent blur path, taken from the material concentration.</param>
    /// <returns>材质确实应用上了时为 true；Accent 路径失败时返回 false，调用方必须退回纯色。
    /// True when the material really was applied; false when the Accent path failed, in which case the caller has to fall back to solid.</returns>
    public bool ApplyNonActivatingBackdrop(nint handle, ApplicationBackdropMode mode, int tint)
    {
        if (handle == nint.Zero || mode == ApplicationBackdropMode.FluentSolid)
        {
            return false;
        }

        ResetBackdrop(handle);
        return SetFrame(handle, extended: false) && ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    /// <summary>
    /// 设置 DWM 客户区扩展边距。
    /// Sets the DWM client-area frame extension margins.
    /// </summary>
    public bool SetFrame(nint handle, bool extended)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        if (_frameOverride is not null)
            return _frameOverride(handle, extended);

        var margins = extended
            ? new DwmMargins(-1, -1, -1, -1)
            : new DwmMargins(0, 0, 0, 0);
        return DwmExtendFrameIntoClientArea(handle, ref margins) >= 0;
    }

    /// <summary>
    /// 设置窗口标题栏和边框颜色是否透明。
    /// Sets whether the window caption and border colors are transparent.
    /// </summary>
    public void SetNonClientColors(nint handle, bool transparent)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        var color = transparent ? DwmColorNone : DwmColorDefault;
        SetDwmAttribute(handle, DwmCaptionColor, color);
        SetDwmAttribute(handle, DwmBorderColor, color);
    }

    /// <summary>
    /// 应用深色模式和圆角等通用 DWM 属性。
    /// Applies common DWM attributes such as dark mode and rounded corners.
    ///
    /// 深色属性号分两档：Windows 10 18985（20H1）起、以及 Windows 11 用 20，17863–18984 只认 19。这里按系统版本先选一个，
    /// 失败再换另一个并把可用值记住，因此旧版 Windows 10 上标题栏与边框的深色着色不再静默失效，后续窗口也不会重复那次失败调用。
    /// The dark-mode attribute number has two tiers: 20 from Windows 10 18985 (20H1) and on Windows 11, but only 19 on 17763-18984.
    /// This picks one by system version, retries with the other when the call fails, and remembers which one worked, so dark caption
    /// and border colouring no longer fails silently on older Windows 10 and later windows do not repeat the failing call.
    /// </summary>
    public void SetThemeAttributes(nint handle, bool dark)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        SetImmersiveDarkMode(handle, dark);
        SetDwmAttribute(handle, DwmWindowCornerPreference, DwmCornerRound);
    }

    private void SetImmersiveDarkMode(nint handle, bool dark)
    {
        if (SetDwmAttribute(handle, _immersiveDarkModeAttribute, dark ? 1 : 0))
        {
            return;
        }

        var fallback = _immersiveDarkModeAttribute == DwmUseImmersiveDarkModeLegacy
            ? DwmUseImmersiveDarkMode
            : DwmUseImmersiveDarkModeLegacy;
        if (SetDwmAttribute(handle, fallback, dark ? 1 : 0))
        {
            _immersiveDarkModeAttribute = fallback;
        }
    }

    /// <summary>
    /// 将 Popup HWND 提升到菜单所需的非激活顶层 Z 序。
    /// Promotes a Popup HWND to the required non-activating top-level Z-order.
    /// </summary>
    public void PromotePopup(nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            handle,
            -1,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 写一个 DWM 窗口属性。
    /// Writes one DWM window attribute.
    /// </summary>
    /// <returns>HRESULT 非负时为 true；失败时调用方重试兼容属性或回退。</returns>
    private bool SetDwmAttribute(nint handle, int attribute, int value) =>
        _attributeOverride?.Invoke(handle, attribute, value)
        ?? DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) >= 0;

    /// <summary>
    /// 应用 Accent 模糊策略。
    /// Applies the Accent blur policy.
    /// </summary>
    /// <returns>调用成功时为 true；Windows 10 上该 API 未公开，失败时调用方要退回纯色而不是留下透明窗口。
    /// True on success; the API is undocumented on Windows 10, so on failure the caller has to fall back to a solid surface instead of
    /// leaving a transparent window behind.</returns>
    private static unsafe bool ApplyAccentPolicy(nint handle, AccentState state, int gradientColor)
    {
        var policy = new AccentPolicy
        {
            State = state,
            GradientColor = gradientColor
        };
        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.AccentPolicy,
            Data = (nint)(&policy),
            SizeOfData = sizeof(AccentPolicy)
        };
        return SetWindowCompositionAttribute(handle, ref data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins
    {
        public DwmMargins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint handle, ref DwmMargins margins);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(nint handle, ref WindowCompositionAttributeData data);
}
