// 连接设置编辑上下文与持久化配置；任务栏横竖共用原字段，悬浮几何独立保存。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>按宿主目标读写设置，非活动页面不能提交旧编辑结果。</summary>
public sealed class LegacySettingsConfiguration(SettingsPageContext context, AFMediaBar.Classes.Services.BarRuntimeCoordinator? runtime = null) : ISettingsConfiguration
{
    public SettingsContext Context => context.Snapshot;
    public bool IsActive => context.IsActive && Context.Mode is (SettingsMode.Taskbar or SettingsMode.FloatingBar) && runtime?.IsSwitching != true;
    public CancellationToken CancellationToken => context.CancellationToken;
    public BarConfigurationTarget Target => Context.Mode == SettingsMode.FloatingBar ? BarConfigurationTarget.FloatingBar : BarConfigurationTarget.Taskbar;
    public AppSettings Current => Context.MonitorDeviceId is null && Target == BarConfigurationTarget.Taskbar
        ? SettingsManager.Current : MonitorBarProfilePolicy.Project(SettingsManager.Current, Target, Context.MonitorDeviceId);
    public event EventHandler? Activated { add => context.Activated += value; remove => context.Activated -= value; }

    private void Update(Action<AppSettings> update)
    {
        if (IsActive) update(SettingsManager.Current);
    }

    private void UpdateGeometry(Action<AppSettings> update)
    {
        if (!IsActive || Context.Mode is not (SettingsMode.Taskbar or SettingsMode.FloatingBar)) return;
        // 模式几何不能以已经合成本屏位置覆盖的 Current 为基底。
        var root = SettingsManager.Current;
        var current = Target == BarConfigurationTarget.Taskbar ? root :
            (root.FloatingBar.Profile ?? BarDisplaySettings.Capture(root)).ApplyTo(root);
        update(current);
        if (Target == BarConfigurationTarget.FloatingBar)
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Profile = BarDisplaySettings.Capture(current) };
    }

    public void SetTaskbarExperience(TaskbarExperienceSettings settings)
    {
        if (!IsActive) return;
        settings = settings.Normalize();
        var root = SettingsManager.Current;
        var previous = Current;
        var proposed = previous.Clone();
        proposed.TaskbarExperience = settings;
        if (!string.IsNullOrWhiteSpace(Context.MonitorDeviceId) && new[] { BarLayoutState.Resident, BarLayoutState.Idle }
            .Any(state => !ComponentLayoutPolicy.ResolveAll(previous, state).SequenceEqual(ComponentLayoutPolicy.ResolveAll(proposed, state))))
        {
            if (!Context.EnvironmentAvailable) return;
            var profile = MonitorBarProfilePolicy.Capture(root, Target, Context.MonitorDeviceId);
            MonitorBarProfilePolicy.Store(root, profile with
            {
                Resident = ComponentLayoutPolicy.ResolveAll(proposed, BarLayoutState.Resident),
                Idle = ComponentLayoutPolicy.ResolveAll(proposed, BarLayoutState.Idle)
            });
        }
        if (Context.MonitorDeviceId is not null)
        {
            var globalLayouts = root.TaskbarExperience.ComponentLayouts;
            var incomingHover = settings.ComponentLayouts?.Hover;
            var sameHover = incomingHover is null ? globalLayouts?.Hover is null : globalLayouts?.Hover is { } hover && incomingHover.SequenceEqual(hover);
            settings = settings with
            {
                ComponentLayouts = sameHover ? globalLayouts :
                (globalLayouts ?? new()) with { Hover = incomingHover }
            };
        }
        var shared = Target == BarConfigurationTarget.Taskbar ? settings : settings with
        {
            LengthMode = root.TaskbarExperience.LengthMode,
            FixedLengthDip = root.TaskbarExperience.FixedLengthDip,
            Arrangement = root.TaskbarExperience.Arrangement
        };
        // 归一化会复制列表；按序列化值比较，避免一次本屏排序额外发布全局外观变化。
        if (System.Text.Json.JsonSerializer.Serialize(shared) != System.Text.Json.JsonSerializer.Serialize(root.TaskbarExperience))
            root.TaskbarExperience = shared;
        if (Target == BarConfigurationTarget.FloatingBar &&
            (previous.TaskbarExperience.LengthMode != settings.LengthMode || previous.TaskbarExperience.FixedLengthDip != settings.FixedLengthDip ||
                previous.TaskbarExperience.Arrangement != settings.Arrangement))
            UpdateGeometry(current => current.TaskbarExperience = current.TaskbarExperience with
            { LengthMode = settings.LengthMode, FixedLengthDip = settings.FixedLengthDip, Arrangement = settings.Arrangement });
    }
    public void SetAppearance(AppearanceSettings settings)
    {
        if (IsActive) SettingsManager.SetAppearanceSettings(settings.Normalize());
    }
    public void SetTaskbarAppearance(AppearanceSettings settings) => Update(current =>
    {
        current.Appearance = current.Appearance with
        {
            PlayerForegroundMode = settings.PlayerForegroundMode,
            TaskbarBackgroundMaterial = settings.TaskbarBackgroundMaterial,
            TaskbarBackgroundOpacityPercent = settings.TaskbarBackgroundOpacityPercent,
            ShowTaskbarBackgroundWhenIdle = settings.ShowTaskbarBackgroundWhenIdle,
            ShowTaskbarBackgroundTint = settings.ShowTaskbarBackgroundTint,
            ShowTaskbarBackgroundBorder = settings.ShowTaskbarBackgroundBorder,
            ShowTaskbarBackgroundTexture = settings.ShowTaskbarBackgroundTexture,
            TaskbarFrostedStyle = settings.TaskbarFrostedStyle,
            IdleNoteColor = settings.IdleNoteColor,
        };
    });
    public void SetSpectrum(SpectrumComponentSettings settings) => Update(current => current.SpectrumComponent = settings.Normalize());
    public void SetPerformance(PerformanceComponentSettings settings) => Update(current =>
    {
        var next = settings.Normalize();
        var previous = current.PerformanceComponent.Normalize();
        if (!previous.Metrics!.SequenceEqual(next.Metrics!) || !previous.GetMetricOrder().SequenceEqual(next.GetMetricOrder()))
            current.TaskbarExperience = ComponentLayoutPolicy.PreserveImplicitLayouts(current);
        current.PerformanceComponent = next;
    });
    public void SetExperiments(ExperimentalSettings settings)
    {
        if (!context.IsActive) return;
        SettingsManager.Current.Experiments = settings.Normalize();
    }
    public void SetFloatingBar(FloatingBarSettings settings) { if (IsActive) SettingsManager.Current.FloatingBar = settings; }
    public void SetTaskbarTargets(IReadOnlyList<string> deviceIds)
    {
        if (!IsActive) return;
        if (Target == BarConfigurationTarget.FloatingBar)
            SetFloatingBar(SettingsManager.Current.FloatingBar with { MonitorDeviceIds = deviceIds.ToArray() });
        else
        {
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds = deviceIds.ToArray();
            SettingsManager.Current.TaskbarTargetMonitorDeviceId = null;
        }
    }
    public void SetTaskbarPlacement(bool? locked = null, bool? avoidIcons = null, double? crossAxisOffsetDip = null, bool reset = false, TaskbarBarPosition? alignment = null)
    {
        if (!IsActive) return;
        if (Context.MonitorDeviceId is { Length: > 0 } monitorId && (reset || alignment.HasValue))
        {
            if (!Context.EnvironmentAvailable) return;
            var root = SettingsManager.Current;
            if (reset)
            {
                MonitorBarProfilePolicy.ResetPlacement(root, SettingsManager.UserDefaults ?? new(), Target, monitorId);
                if (Target == BarConfigurationTarget.FloatingBar)
                {
                    var positions = root.FloatingBar.Positions.Where(position => !string.Equals(position.MonitorDeviceId, monitorId, StringComparison.OrdinalIgnoreCase));
                    var saved = (SettingsManager.UserDefaults ?? new()).FloatingBar.Positions
                        .FirstOrDefault(position => string.Equals(position.MonitorDeviceId, monitorId, StringComparison.OrdinalIgnoreCase));
                    root.FloatingBar = root.FloatingBar with { Positions = saved is null ? positions.ToArray() : positions.Append(saved).ToArray() };
                }
            }
            else if (alignment is { } selected && Enum.IsDefined(selected))
                MonitorBarProfilePolicy.Store(root, MonitorBarProfilePolicy.Capture(root, Target, monitorId) with
                { BodyAlignment = selected, BodyPrimaryDip = null });
            return;
        }
        if (reset && IsActive && Target == BarConfigurationTarget.FloatingBar)
            SetFloatingBar(SettingsManager.Current.FloatingBar with { Positions = [] });
        UpdateGeometry(current =>
        {
            if (alignment is { } position && Enum.IsDefined(position) && current.Position != position)
            {
                current.TaskbarBarManualPadding = 0;
                current.TaskbarExperience = current.TaskbarExperience with { Arrangement = null };
                current.Position = position;
            }
            if (locked is { } lockValue) current.TaskbarBarPositionLocked = lockValue;
            if (avoidIcons is { } avoidValue) current.TaskbarBarAvoidIcons = avoidValue;
            if (crossAxisOffsetDip is { } offset) current.TaskbarBarCrossAxisOffsetDip = offset;
            if (reset) { current.Position = TaskbarBarPosition.Start; current.TaskbarBarManualPadding = 0; current.TaskbarBarCrossAxisOffsetDip = 0; }
        });
        if (IsActive && Target == BarConfigurationTarget.Taskbar)
            SettingsManager.RaiseLayoutSettingsChanged(SettingsManager.Current.WindowMode, Current.LayoutOrientationMode);
    }
    public void SetLyricsEnabled(bool value) => Update(current => current.LyricsEnabled = value);
    public void SetTwoLineLyricsEnabled(bool value) => Update(current => current.TwoLineLyricsEnabled = value);
    public void SetLyricsSecondaryLineSettings(LyricsSecondaryLineSettings value) => Update(current => current.LyricsSecondaryLine = value);
    public void SetLyricsTextAlignment(LyricsTextAlignment value) => Update(current => current.LyricsTextAlignment = value);
    public void SetLyricsSyllableHighlightEnabled(bool value) => Update(current => current.LyricsSyllableHighlightEnabled = value);
    public void SetLyricsChineseConversion(LyricsChineseConversionMode value) => Update(current => current.LyricsChineseConversion = value);
    public void SetLyricsFuriganaFilterEnabled(bool value) => Update(current => current.LyricsFuriganaFilterEnabled = value);
    // 长歌词动态滚动是全局呈现行为：任务栏与悬浮栏读同一份配置，因此走普通更新，
    // 由 LyricsSettingsChanged 通知两端重新投影并申请新宽度。
    public void SetLyricsLongLineScrollEnabled(bool value) => Update(current => current.LyricsLongLineScrollEnabled = value);
    public void SetLyricsBracketSymbolsHidden(bool value) => Update(current => current.LyricsBracketSymbolsHidden = value);
    public void SetLyricsUnsungOpacityPercent(int value) => Update(current => current.LyricsUnsungOpacityPercent = value);
    public void SetLyricsCharacterSpacingPercent(int value) => Update(current => current.LyricsCharacterSpacingPercent = value);
    public void SetLyricsLineGapPercent(int value) => Update(current => current.LyricsLineGapPercent = value);
    public void SetLyricsFixedWidthEnabled(bool value) => UpdateGeometry(current => current.LyricsFixedWidthEnabled = value);
    public void SetLyricsFixedWidthDip(int value) => UpdateGeometry(current => current.LyricsFixedWidthDip = value);
    public void SetInteractionSettings(GlobalInteractionSettings value) => Update(current => current.Interaction = value);
    public void SetAllowBrowserAndVideoLyrics(bool value) { if (IsActive) SettingsManager.Current.AllowBrowserAndVideoLyrics = value; }
    public void SetLyricsInfoLineFilterEnabled(bool value) { if (IsActive) SettingsManager.Current.LyricsInfoLineFilterEnabled = value; }
    public void SetLyricsInstrumentalTitleOnlyEnabled(bool value) { if (IsActive) SettingsManager.Current.LyricsInstrumentalTitleOnlyEnabled = value; }
    public void SetLyricsSourceSettings(LyricsSourceSettings value) { if (IsActive) SettingsManager.Current.LyricsSource = value; }
    public void SetLyricsArtistSeparators(string value) { if (IsActive) SettingsManager.Current.LyricsArtistSeparators = value; }
    public void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings value) { if (IsActive) SettingsManager.Current.TrackChangeNotification = value; }
    public void SetAudioDeviceSettings(AudioDeviceSettings value) { if (IsActive) SettingsManager.Current.AudioDevices = value; }

    public void Reset(SettingsResetScope scope)
    {
        if (!IsActive) return;
        if (Context.MonitorDeviceId is { Length: > 0 } localMonitor && scope is SettingsResetScope.RestState or SettingsResetScope.ContentLayout)
        {
            if (!Context.EnvironmentAvailable) return;
            var localRoot = SettingsManager.Current;
            var localDefaults = MonitorBarProfilePolicy.Project(SettingsManager.UserDefaults ?? new(), Target, localMonitor);
            var profile = MonitorBarProfilePolicy.Capture(localRoot, Target, localMonitor);
            MonitorBarProfilePolicy.Store(localRoot, profile with
            {
                Resident = ComponentLayoutPolicy.ResolveAll(localDefaults, BarLayoutState.Resident),
                Idle = ComponentLayoutPolicy.ResolveAll(localDefaults, BarLayoutState.Idle)
            });
            if (scope == SettingsResetScope.RestState)
                localRoot.TaskbarExperience = localRoot.TaskbarExperience with
                {
                    SmallArtwork = localDefaults.TaskbarExperience.SmallArtwork,
                    ArtworkProgressVisible = localDefaults.TaskbarExperience.ArtworkProgressVisible,
                    RestProgressVisible = localDefaults.TaskbarExperience.RestProgressVisible
                };
            return;
        }
        if (Context.MonitorDeviceId is { Length: > 0 } monitorId && scope == SettingsResetScope.Layout)
        {
            SetTaskbarPlacement(reset: true);
            return;
        }
        if (SettingsSectionResetPolicy.Handles(scope))
        {
            var sectionRoot = SettingsManager.Current;
            var sectionDefaults = (SettingsManager.UserDefaults ?? new AppSettings()).Clone();
            if (Target == BarConfigurationTarget.FloatingBar)
                sectionDefaults = (sectionDefaults.FloatingBar.Profile ?? BarDisplaySettings.Capture(sectionDefaults)).ApplyTo(sectionDefaults);
            var sectionNext = SettingsSectionResetPolicy.Apply(Current, sectionDefaults, scope);
            var modeGeometry = Target == BarConfigurationTarget.Taskbar ? sectionRoot :
                (sectionRoot.FloatingBar.Profile ?? BarDisplaySettings.Capture(sectionRoot)).ApplyTo(sectionRoot);
            sectionNext.Position = modeGeometry.Position;
            sectionNext.TaskbarBarManualPadding = modeGeometry.TaskbarBarManualPadding;
            if (Target == BarConfigurationTarget.FloatingBar)
            {
                var sectionFloating = sectionNext.FloatingBar with { Profile = BarDisplaySettings.Capture(sectionNext) };
                sectionNext = BarDisplaySettings.Capture(sectionRoot).ApplyTo(sectionNext);
                sectionNext.FloatingBar = sectionFloating;
            }
            MergeLocalLists(sectionNext, sectionDefaults, scope);
            SettingsManager.Replace(sectionNext, scope);
            return;
        }
        switch (scope)
        {
            case SettingsResetScope.Lyrics: SettingsManager.ResetLyrics(); return;
            case SettingsResetScope.DeviceManagement: SettingsManager.ResetDeviceManagement(); return;
            case SettingsResetScope.Interaction: SettingsManager.ResetInteraction(); return;
            case SettingsResetScope.Components: SettingsManager.ResetComponents(); return;
            case SettingsResetScope.RestState: SettingsManager.ResetRestState(); return;
            case SettingsResetScope.Appearance: SettingsManager.ResetTaskbarAppearance(); return;
        }
        if (Target == BarConfigurationTarget.Taskbar)
        {
            switch (scope)
            {
                case SettingsResetScope.DisplayModes: SettingsManager.ResetDisplayModes(); return;
                case SettingsResetScope.ContentLayout: SettingsManager.ResetContentLayout(); return;
                case SettingsResetScope.Layout: SettingsManager.ResetScreenAndPlacement(); return;
                default: throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }
        var root = SettingsManager.Current;
        var defaults = SettingsManager.UserDefaults ?? new AppSettings();
        var geometry = defaults.FloatingBar.Profile ?? BarDisplaySettings.Capture(defaults);
        var next = Current;
        if (scope == SettingsResetScope.Layout)
        {
            next.TaskbarBarPositionLocked = geometry.TaskbarBarPositionLocked;
            next.FloatingBar = next.FloatingBar with
            {
                Positions = defaults.FloatingBar.Positions,
                MonitorDeviceIds = defaults.FloatingBar.MonitorDeviceIds,
                Orientation = defaults.FloatingBar.Orientation,
                Layer = defaults.FloatingBar.Layer,
                OverlayTaskbar = defaults.FloatingBar.OverlayTaskbar,
                EdgeCollapseEnabled = defaults.FloatingBar.EdgeCollapseEnabled
            };
        }
        else if (scope is SettingsResetScope.ContentLayout or SettingsResetScope.DisplayModes)
        {
            next.TaskbarExperience = SettingsResetPolicy.Content(next.TaskbarExperience, defaults.TaskbarExperience) with
            { LengthMode = geometry.LengthMode, FixedLengthDip = geometry.FixedLengthDip, Arrangement = geometry.Arrangement };
            if (scope == SettingsResetScope.DisplayModes)
            {
                // 旧入口同时恢复组件参数；内容与布局只恢复显隐和顺序。
                next.TaskbarExperience = next.TaskbarExperience with
                { SpectrumVisible = defaults.TaskbarExperience.SpectrumVisible, PerformanceVisible = defaults.TaskbarExperience.PerformanceVisible };
                next.SpectrumComponent = defaults.SpectrumComponent;
                next.PerformanceComponent = defaults.PerformanceComponent;
                next.BluetoothBatteryWidget = next.BluetoothBatteryWidget with { Enabled = defaults.BluetoothBatteryWidget.Enabled };
            }
            next.LyricsTextAlignment = defaults.LyricsTextAlignment;
            next.LyricsFixedWidthEnabled = geometry.LyricsFixedWidthEnabled;
            next.LyricsFixedWidthDip = geometry.LyricsFixedWidthDip;
        }
        else throw new ArgumentOutOfRangeException(nameof(scope));
        var floating = next.FloatingBar with { Profile = BarDisplaySettings.Capture(next) };
        // 一次发布同时更新共用值和悬浮几何，任务栏几何保持原值。
        next = BarDisplaySettings.Capture(root).ApplyTo(next);
        next.FloatingBar = floating;
        SettingsManager.Replace(next, scope);
    }

    private void MergeLocalLists(AppSettings next, AppSettings defaults, SettingsResetScope scope)
    {
        if (Context.MonitorDeviceId is not { Length: > 0 } monitorId) return;
        var root = SettingsManager.Current;
        var profile = MonitorBarProfilePolicy.Capture(root, Target, monitorId);
        var localDefaults = MonitorBarProfilePolicy.Project(defaults, Target, monitorId);
        var restored = next.TaskbarExperience.ComponentLayouts;
        if (scope is SettingsResetScope.RestState or SettingsResetScope.ContentLayout)
            MonitorBarProfilePolicy.Store(next, profile with
            {
                Resident = ComponentLayoutPolicy.ResolveAll(localDefaults, BarLayoutState.Resident),
                Idle = ComponentLayoutPolicy.ResolveAll(localDefaults, BarLayoutState.Idle)
            });
        var shared = root.TaskbarExperience.ComponentLayouts;
        next.TaskbarExperience = next.TaskbarExperience with
        {
            ComponentLayouts = restored is null && shared is null ? null :
            (restored ?? new()) with { Resident = shared?.Resident, Idle = shared?.Idle }
        };
    }
}
