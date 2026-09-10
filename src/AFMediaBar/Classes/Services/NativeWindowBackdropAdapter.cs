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
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmSystemBackdropType = 38;
    private const int DwmMicaEffect = 1029;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMica = 2;
    private const int DwmBackdropAcrylic = 3;
    private const int DwmCornerRound = 2;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// 创建原生窗口背景适配器。
    /// Creates the native window backdrop adapter.
    /// </summary>
    public NativeWindowBackdropAdapter()
    {
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
        ApplyAccentPolicy(handle, AccentState.Disabled, 0);
    }

    /// <summary>
    /// 按 Windows 版本应用 Mica、Acrylic 或兼容的旧版材质。
    /// Applies Mica, Acrylic, or a compatible legacy material according to the Windows version.
    /// </summary>
    public void ApplyBackdrop(nint handle, ApplicationBackdropMode mode, bool dark)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        ResetBackdrop(handle);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            SetFrame(handle, extended: true);
            SetDwmAttribute(handle, DwmSystemBackdropType,
                mode == ApplicationBackdropMode.Mica ? DwmBackdropMica : DwmBackdropAcrylic);
            return;
        }

        if (mode == ApplicationBackdropMode.Mica)
        {
            SetFrame(handle, extended: true);
            SetDwmAttribute(handle, DwmMicaEffect, 1);
            return;
        }

        SetFrame(handle, extended: false);
        var tint = dark ? unchecked((int)0xCC202020) : unchecked((int)0xCCF9F9F9);
        ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    /// <summary>
    /// 设置 DWM 客户区扩展边距。
    /// Sets the DWM client-area frame extension margins.
    /// </summary>
    public void SetFrame(nint handle, bool extended)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        var margins = extended
            ? new DwmMargins(-1, -1, -1, -1)
            : new DwmMargins(0, 0, 0, 0);
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
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
    /// </summary>
    public void SetThemeAttributes(nint handle, bool dark)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        SetDwmAttribute(handle, DwmUseImmersiveDarkMode, dark ? 1 : 0);
        SetDwmAttribute(handle, DwmWindowCornerPreference, DwmCornerRound);
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

    private static void SetDwmAttribute(nint handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    private static unsafe void ApplyAccentPolicy(nint handle, AccentState state, int gradientColor)
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
        _ = SetWindowCompositionAttribute(handle, ref data);
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
