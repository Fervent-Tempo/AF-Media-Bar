using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;

namespace AFMediaBar.Classes.Services;

/// <summary>解析并执行所有播放器表面共用的滚轮语义。 / Resolves and executes wheel semantics shared by every player surface.</summary>
public sealed class GlobalInteractionRouter
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;

    public GlobalInteractionRouter(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService)
    {
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
    }

    public async Task<string?> ExecuteWheelAsync(
        int delta,
        bool isLeftButtonDown,
        bool isRightButtonDown,
        bool isTray = false)
    {
        var settings = SettingsManager.Current.Interaction;
        var action = GlobalWheelGesturePolicy.Resolve(
            settings,
            isLeftButtonDown,
            isRightButtonDown,
            isTray);
        if (action is null || delta == 0)
            return null;

        var steps = WheelInput.GetStepCount(delta);
        switch (action.Value)
        {
            case WheelAction.PreviousNext:
                for (var index = 0; index < steps; index++)
                {
                    if (delta > 0)
                        await _mediaSessionService.SkipPreviousAsync();
                    else
                        await _mediaSessionService.SkipNextAsync();
                }
                return delta > 0 ? "上一首" : "下一首";
            case WheelAction.CurrentApplicationVolume:
                return await _audioInteractionService.AdjustCurrentMediaVolumeAsync(
                    delta > 0 ? steps : -steps);
            case WheelAction.OutputDevice:
                // 向上滚动选择前一个设备，向下滚动选择下一个设备。
                // Wheel-up selects the previous device and wheel-down selects the next one.
                return await _audioInteractionService.CycleOutputDeviceAsync(
                    delta > 0 ? -steps : steps,
                    deferApply: true);
            default:
                return null;
        }
    }
}

/// <summary>无 UI 依赖的全局滚轮映射。 / UI-independent global wheel mapping.</summary>
public static class GlobalWheelGesturePolicy
{
    public static WheelAction? Resolve(
        GlobalInteractionSettings settings,
        bool isLeftButtonDown,
        bool isRightButtonDown,
        bool isTray)
    {
        settings = settings.Normalize();
        if (isTray && !settings.TrayUsesGlobalWheel)
            return null;

        if (!isTray && settings.Mode == MediaInteractionMode.Buttons)
            return null;

        var chordPressed = settings.ChordWheelEnabled && settings.ChordButton switch
        {
            MouseChordButton.Left => isLeftButtonDown,
            MouseChordButton.Right => isRightButtonDown,
            _ => false
        };
        return chordPressed ? settings.ChordWheelAction : settings.PrimaryWheelAction;
    }
}
