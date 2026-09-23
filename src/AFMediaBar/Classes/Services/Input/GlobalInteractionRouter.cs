using System.Diagnostics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>解析并执行播放器表面共用的媒体滚轮语义。 / Resolves and executes media-wheel semantics shared by player surfaces.</summary>
public sealed class GlobalInteractionRouter
{
    /// <summary>标签与结果之间可能出现的分隔符：中文用全角冒号，英文用半角冒号。/ Separators that may sit between a label and its value: a full-width colon in Chinese and a colon in English.</summary>
    private static readonly char[] LabelSeparators = ['：', ':'];

    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly TrackSkipWheelGate _trackSkipWheelGate = new();

    public GlobalInteractionRouter(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService)
    {
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
    }

    public async Task<WheelTooltipResult?> ExecuteWheelAsync(
        int delta,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        var settings = SettingsManager.Current.Interaction;
        var action = GlobalWheelGesturePolicy.Resolve(
            settings,
            isShiftDown,
            isLeftButtonDown,
            isRightButtonDown);
        if (delta == 0)
            return null;

        var steps = WheelInput.GetStepCount(delta);
        switch (action)
        {
            case WheelAction.PreviousNext:
                if (!_trackSkipWheelGate.TryBegin(Stopwatch.GetTimestamp()))
                    return null;

                try
                {
                    if (delta > 0)
                        await _mediaSessionService.SkipPreviousAsync();
                    else
                        await _mediaSessionService.SkipNextAsync();
                }
                finally
                {
                    _trackSkipWheelGate.Complete();
                }

                // 结果里带上切歌之后的曲名：提示要回答的是"刚才发生了什么"，只写"下一首"等于把用户已经知道的事说了一遍。
                // 切歌是异步的：播放器要过一会儿才发布新会话，因此这里先给出当时读到的曲名，并把结果标记为"曲名可能变化"，
                // 由提示的轮询在标题真正更新后改写——否则提示会一直显示切歌**之前**的那首。
                // The result carries the title after the skip: the tooltip answers "what just happened", and saying only "next" would
                // restate what the user already knows. A skip is asynchronous — the player publishes the new session a moment later — so the
                // title read here is provisional and the result is marked as "the title may still change", letting the tooltip's poll rewrite
                // it once the new track arrives. Otherwise the tooltip keeps showing the track from *before* the skip.
                return new WheelTooltipResult(
                    WheelTooltipPolicy.BuildSkipActionName(delta),
                    ResolveCurrentTitle(),
                    DetailFollowsMedia: true);

            case WheelAction.SwitchMediaSource:
                return CycleMediaSource(delta > 0 ? -steps : steps);

            case WheelAction.CurrentApplicationVolume:
            {
                var detail = await _audioInteractionService.AdjustCurrentMediaVolumeAsync(delta > 0 ? steps : -steps);
                return new WheelTooltipResult(Translations.Get("Wheel.Action.CurrentMediaVolume"), detail);
            }

            case WheelAction.OutputDevice:
            {
                var detail = await _audioInteractionService.CycleOutputDeviceAsync(delta > 0 ? -steps : steps, deferApply: true);
                return new WheelTooltipResult(Translations.Get("Common.OutputDevice"), StripLabel(detail));
            }

            // 不绑定：什么都不做，也不写结果——"滚了但没反应"配上提示里的"已禁用"就是明确的答案。
            // Not bound: nothing happens and no result is written; "the wheel did nothing" plus "disabled" in the tooltip is the answer.
            case WheelAction.Disabled:
            default:
                return null;
        }
    }

    /// <summary>取当前媒体的曲名，用于切歌结果。/ Reads the current media title for the skip result.</summary>
    private string? ResolveCurrentTitle() => _mediaSessionService.CurrentSnapshot?.Title;

    /// <summary>
    /// 去掉音频服务返回文本里已经带上的标签。音频服务把"输出设备：X"整句交给托盘提示用，
    /// 而这里的提示自己拼"动作名：结果"，两层标签会变成"输出设备：输出设备：X"。
    ///
    /// 标签本身按当前界面语言取值，因此这里也按同一语言匹配：简繁用全角冒号，英文用半角冒号加空格，
    /// 只认一种冒号会让另一种语言整句留在结果里。
    /// Strips the label the audio service already embedded. That service hands the tray tooltip a whole sentence such as
    /// "output device: X", while this tooltip composes its own "action: result", and two labels would read
    /// "output device: output device: X".
    ///
    /// The label itself is read in the active interface language, so it is matched in that language too: Chinese writes a
    /// full-width colon and English a colon followed by a space, and accepting only one of them would leave the whole
    /// sentence in the result for the other.
    /// </summary>
    private static string? StripLabel(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        var label = Translations.Get("Common.OutputDevice");
        var remainder = detail.StartsWith(label, StringComparison.Ordinal)
            ? detail[label.Length..]
            : detail;
        return remainder.TrimStart(LabelSeparators).Trim();
    }

    private WheelTooltipResult? CycleMediaSource(int signedSteps)
    {
        var sessions = _mediaSessionService.CurrentSessionOptions;
        if (sessions.Count == 0 || signedSteps == 0)
            return null;

        var current = sessions.ToList().FindIndex(session => session.IsSelected);
        if (current < 0)
            current = 0;
        var target = sessions[WheelInput.MoveCircular(current, signedSteps, sessions.Count)];
        _mediaSessionService.SelectSession(target.Key);
        return new WheelTooltipResult(Translations.Get("Wheel.Action.MediaSource"), target.DisplayName);
    }
}

/// <summary>一次滚轮手势的结果：动作名与结果细节，供提示文案拼装。/ Result of one wheel gesture: the action name and its detail, for the tooltip to compose.</summary>
/// <param name="ActionName">动作的显示名（例如「下一首」）。/ Display name of the action, such as "next".</param>
/// <param name="Detail">结果细节（曲名、媒体名、设备名或音量）；读不到时为 null。/ Result detail (title, media name, device name, or volume), or null when unreadable.</param>
/// <param name="DetailFollowsMedia">
/// 细节是否还会随媒体变化：切歌是异步的，提示因此要在轮询里重新读一次曲名，否则会一直显示切歌之前的那首。
/// Whether the detail still follows the media: a skip is asynchronous, so the tooltip re-reads the title on every poll instead of keeping the
/// track from before the skip.
/// </param>
public readonly record struct WheelTooltipResult(string ActionName, string? Detail, bool DetailFollowsMedia = false);

/// <summary>无 UI 依赖的全局滚轮映射。 / UI-independent global wheel mapping.</summary>
public static class GlobalWheelGesturePolicy
{
    public static WheelAction Resolve(
        GlobalInteractionSettings settings,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        return IsChordHeld(settings, isShiftDown, isLeftButtonDown, isRightButtonDown)
            ? settings.ChordWheelAction
            : settings.PrimaryWheelAction;
    }

    /// <summary>
    /// 当前是否按住了共用的组合键。判定只有一处，媒体栏读取 WPF 的输入状态、托盘读取全局钩子的状态，
    /// 两者必须得到同一个结论，否则同一个手势在两处会落到不同的槽位。
    /// Whether the shared chord key is currently held. The rule lives in exactly one place: the media bar reads WPF input state and
    /// the tray reads the global hook's state, and both must reach the same conclusion or one gesture would land in different slots.
    /// </summary>
    /// <param name="settings">交互设置（内部会归一化）。/ Interaction settings, normalized internally.</param>
    /// <param name="isShiftDown">Shift 是否按住。/ Whether Shift is held.</param>
    /// <param name="isLeftButtonDown">鼠标左键是否按住。/ Whether the left button is held.</param>
    /// <param name="isRightButtonDown">鼠标右键是否按住。/ Whether the right button is held.</param>
    public static bool IsChordHeld(
        GlobalInteractionSettings settings,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        return settings.Modifier switch
        {
            InteractionModifier.Shift => isShiftDown,
            InteractionModifier.LeftMouseButton => isLeftButtonDown,
            InteractionModifier.RightMouseButton => isRightButtonDown,
            _ => false
        };
    }

    /// <summary>解析同一修饰键下的托盘滚轮绑定。 / Resolves tray-wheel binding using the shared modifier.</summary>
    public static TrayWheelBehavior ResolveTray(
        GlobalInteractionSettings settings,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        return IsChordHeld(settings, isShiftDown, isLeftButtonDown, isRightButtonDown)
            ? settings.TrayChordWheelAction
            : settings.TrayPrimaryWheelAction;
    }
}

/// <summary>解析任务栏静置内容的点击绑定。 / Resolves click bindings for taskbar rest-layer content.</summary>
public static class PlayerClickBindingPolicy
{
    public static PlayerClickAction Resolve(GlobalInteractionSettings settings, bool artwork) =>
        artwork ? settings.Normalize().ArtworkClickAction : settings.Normalize().TextClickAction;
}
