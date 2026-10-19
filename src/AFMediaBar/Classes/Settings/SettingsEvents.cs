// 定义设置变更事件与重置范围。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AFMediaBar.Classes.Settings;


/// <summary>设置重置范围。 / Settings reset scope.</summary>
public enum SettingsResetScope { General, Appearance, Layout, DisplayModes, ExtraFeatures, Interaction, Lyrics, All, Components, ContentLayout, DeviceManagement }

/// <summary>设置变更通知参数。 / Settings change notification arguments.</summary>
public sealed class SettingsChangedEventArgs(SettingsResetScope? resetScope = null, string? propertyName = null) : EventArgs
{
    public SettingsResetScope? ResetScope { get; } = resetScope;
    public string? PropertyName { get; } = propertyName;
}

/// <summary>外观设置变更参数。 / Appearance settings change arguments.</summary>
public sealed class AppearanceSettingsChangedEventArgs(AppearanceSettings appearance) : EventArgs { public AppearanceSettings Appearance { get; } = appearance; }
/// <summary>布局设置变更参数。 / Layout settings change arguments.</summary>
public sealed class LayoutSettingsChangedEventArgs(WindowMode windowMode, LayoutOrientationMode orientationMode) : EventArgs
{
    public WindowMode WindowMode { get; } = windowMode;
    public LayoutOrientationMode OrientationMode { get; } = orientationMode;
}
