namespace AFMediaBar.Classes.Settings;

/// <summary>桌面媒体卡片的显示设置；不参与任务栏栏位的布局计算。</summary>
public sealed record MediaCardSettings
{
    /// <summary>是否在桌面上显示独立的迷你控制器。</summary>
    public bool Enabled { get; init; }

    /// <summary>仅显示媒体卡片：隐藏任务栏媒体栏与悬浮条，减少后台绘制；卡片关闭后自动恢复。</summary>
    public bool OnlyCard { get; init; }

    /// <summary>卡片上次所在的屏幕坐标（DIP）；为空时回落到屏幕右下角。</summary>
    public double? Left { get; init; }

    /// <summary>卡片上次所在的屏幕坐标（DIP）；为空时回落到屏幕右下角。</summary>
    public double? Top { get; init; }

    /// <summary>中文文字使用的字体族名；空表示跟随系统。</summary>
    public string ChineseFontFamily { get; init; } = string.Empty;

    /// <summary>日文文字（含假名与日文汉字）使用的字体族名；空表示跟随系统。</summary>
    public string JapaneseFontFamily { get; init; } = string.Empty;

    /// <summary>英文、数字与拉丁文字使用的字体族名；空表示跟随系统。</summary>
    public string EnglishFontFamily { get; init; } = string.Empty;

    /// <summary>是否始终置顶；关闭后会被其它窗口覆盖。</summary>
    public bool Topmost { get; init; } = true;

    /// <summary>卡片配色：深色、浅色或从封面取主色作为强调色。</summary>
    public MediaCardColorTheme ColorTheme { get; init; } = MediaCardColorTheme.Dark;

    /// <summary>是否显示罗马音；关闭后只显示译文（若有）。罗马音文本仍走主程序同一套歌词处理。</summary>
    public bool ShowRomanization { get; init; } = true;

    /// <summary>副行没有罗马音和译文时，用歌曲名与艺术家填补；关闭后副行留白，无歌词曲目只在主行显示歌名。</summary>
    public bool ShowTrackInfoOnEmptyLines { get; init; }

    /// <summary>是否显示右轨频谱。</summary>
    public bool ShowSpectrum { get; init; } = true;

    /// <summary>沉浸式频谱：指针移出卡片后，频谱从右轨移到进度条上方沿底边跳动。</summary>
    public bool ImmersiveSpectrum { get; init; }

    /// <summary>鼠标移入卡片时，用随音乐轻微起伏的波浪线显示播放进度；关闭则用直线进度条。</summary>
    public bool WavyProgressBar { get; init; } = true;

    /// <summary>频谱参数来源：跟随主程序频谱组件，或使用本卡片的自定义参数。</summary>
    public DesktopSpectrumMode SpectrumMode { get; init; } = DesktopSpectrumMode.FollowMain;

    /// <summary>自定义模式下的频谱样式。</summary>
    public SpectrumStyle SpectrumStyle { get; init; } = SpectrumStyle.Bars;

    /// <summary>自定义模式下的频谱条数。</summary>
    public int SpectrumBarCount { get; init; } = SpectrumComponentSettings.DefaultBandCount;

    /// <summary>自定义模式下的频谱灵敏度（百分比）。</summary>
    public int SpectrumSensitivity { get; init; } = 100;

    /// <summary>是否显示 CPU 占用。</summary>
    public bool ShowCpu { get; init; } = true;

    /// <summary>是否显示 GPU 占用。</summary>
    public bool ShowGpu { get; init; } = true;

    /// <summary>是否显示内存占用。</summary>
    public bool ShowMemory { get; init; } = true;

    /// <summary>是否显示网络速率。</summary>
    public bool ShowNetwork { get; init; } = true;

    public static MediaCardSettings Default { get; } = new();

    public MediaCardSettings Normalize() => this with
    {
        ColorTheme = Enum.IsDefined(ColorTheme) ? ColorTheme : MediaCardColorTheme.Dark,
        SpectrumMode = Enum.IsDefined(SpectrumMode) ? SpectrumMode : DesktopSpectrumMode.FollowMain,
        SpectrumStyle = Enum.IsDefined(SpectrumStyle) ? SpectrumStyle : SpectrumStyle.Bars,
        SpectrumBarCount = SpectrumBarCount < SpectrumComponentSettings.MinimumBandCount
            ? SpectrumComponentSettings.DefaultBandCount
            : Math.Clamp(SpectrumBarCount, SpectrumComponentSettings.MinimumBandCount, SpectrumComponentSettings.MaximumBandCount),
        SpectrumSensitivity = SpectrumComponentSettings.SnapSensitivityPercent(SpectrumSensitivity)
    };
}
