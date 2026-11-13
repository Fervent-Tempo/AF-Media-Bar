// 媒体卡片：一块版面承担“静息展示”和“点亮控制”两种身份。
// 数据走现有的媒体快照、歌词投影、频谱采样与系统指标，不另开采集通道。
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Views.Windows;

/// <summary>媒体卡片窗口：静息态展示封面与歌词，指针进入后让出位置给控制条。</summary>
public partial class MediaCardWindow : Window
{
    private const double SpectrumCanvasWidth = 82;
    private const double SpectrumCanvasHeight = 48;
    private const double ImmersiveSpectrumOpacity = 0.42;
    private const double CoverScaleLit = 0.692;
    private const double IdleLingerMilliseconds = 800;

    // 点亮态波浪进度条：Canvas 高 16，波形以中线为基线。
    private const double LitWaveHeight = 16;
    private const int WavePointCount = 160;
    private const double WaveBaseAmplitude = 2.4;
    private const double WaveEnergyAmplitude = 1;
    private const double WaveLengthDip = 28;
    private const double WaveCyclesPerSecond = 0.42;
    private const double WaveHeadTaperDip = 12;
    // 曲目行无缝循环的恒定速度。按像素而不是按周期算，长歌名才不会越滚越快。
    private const double MetaScrollPixelsPerSecond = 32;
    // 一轮滚完、歌名重新回到开头时停一下，别一刻不停地跑。
    private const double MetaLoopPauseSeconds = 1.5;
    // 卡片组合字体按字符范围选字；汉字范围在前，避免西文字体自带的汉字字形盖住用户选的字体。
    // 汉字与兜底范围和主程序组合字体共用，避免两份常量漂移；假名范围是卡片三语字体独有的。
    private const string CardHanRanges = InstalledFontCatalog.HanRanges;
    private const string CardKanaRanges = "3040-30FF, 31F0-31FF, FF61-FF9F";
    private const string CardAllCharacters = InstalledFontCatalog.AllCharacters;

    // 深色主题调色板（默认）。
    private static readonly Color DarkBackground = Color.FromArgb(0xE6, 0x14, 0x14, 0x18);
    private static readonly Color DarkBorder = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextPrimary = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextSecondary = Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextDim = Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextRomanization = Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextTranslation = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkChromeBorder = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkChromeHover = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkChromePressed = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkProgressTrack = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkArtFrame = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkChipForeground = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);

    // 浅色主题调色板。
    private static readonly Color LightBackground = Color.FromArgb(0xF2, 0xF7, 0xF9, 0xFC);
    private static readonly Color LightBorder = Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
    private static readonly Color LightTextPrimary = Color.FromArgb(0xE6, 0x00, 0x00, 0x00);
    private static readonly Color LightTextSecondary = Color.FromArgb(0x8F, 0x00, 0x00, 0x00);
    private static readonly Color LightTextDim = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
    private static readonly Color LightTextRomanization = Color.FromArgb(0x73, 0x00, 0x00, 0x00);
    private static readonly Color LightTextTranslation = Color.FromArgb(0x59, 0x00, 0x00, 0x00);
    private static readonly Color LightChromeBorder = Color.FromArgb(0x26, 0x00, 0x00, 0x00);
    private static readonly Color LightChromeHover = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
    private static readonly Color LightChromePressed = Color.FromArgb(0x26, 0x00, 0x00, 0x00);
    private static readonly Color LightProgressTrack = Color.FromArgb(0x26, 0x00, 0x00, 0x00);
    private static readonly Color LightArtFrame = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
    private static readonly Color LightChipForeground = Color.FromArgb(0x99, 0x00, 0x00, 0x00);

    // 两种主题共用的蓝色强调色；封面取色主题从封面主色派生。
    private static readonly Color DarkAccent = Color.FromRgb(0x37, 0x8A, 0xDD);
    private static readonly Color LightAccent = Color.FromRgb(0x1F, 0x6F, 0xB2);

    private readonly MediaSessionService _media;
    private readonly AudioMonitorService _audio;
    private readonly SystemMetricsMonitorService _metrics;
    private readonly DispatcherTimer _spectrumTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _idleTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(IdleLingerMilliseconds) };
    private readonly float[] _bands = new float[SpectrumComponentSettings.MaximumBandCount];
    private Rectangle[] _bars = [];
    private Rectangle[] _immersiveBars = [];
    private bool _immersiveEnabled;
    private double _immersiveWidth;
    private Polygon? _waveform;
    private SpectrumPoint[] _waveformPoints = [];
    // 固定容量的点集合逐帧原地改坐标，避免每帧为波形重新分配几何对象。
    private readonly PointCollection _wavePoints = new(WavePointCount);
    private double _wavePhase;
    private double _waveEnergy;
    private DateTimeOffset _lastFrameUtc;
    private readonly LinearGradientBrush _highlightMask = new()
    {
        StartPoint = new Point(0, 0.5),
        EndPoint = new Point(1, 0.5)
    };
    // OpacityMask 只看 brush 的 alpha 通道：白(不透明)=显示，Transparent(alpha 0)=隐藏。
    // 早期用 Colors.Black 作为「关」是错误的——黑的 alpha 同样是 255，蒙版整片不透明，高亮层全显示，
    // 于是逐字擦亮看起来完全没动。
    private readonly GradientStop _maskOn = new(Colors.White, 0);
    private readonly GradientStop _maskOff = new(Colors.Transparent, 0);
    private SolidColorBrush _accentBrush = new(Color.FromRgb(0x37, 0x8A, 0xDD));
    private MediaCardSettings _settings = MediaCardSettings.Default;
    private bool _showRomanization = true;
    private bool _spectrumVisible = true;
    private int _spectrumBandCount = SpectrumComponentSettings.DefaultBandCount;
    private SpectrumStyle _spectrumStyle = SpectrumStyle.Bars;
    private int _spectrumSensitivity = 100;
    private bool _performanceVisible = true;
    private IReadOnlyList<MetricKind> _metricKinds = [];
    private int _metricsIntervalMs = PerformanceComponentSettings.DefaultRefreshIntervalMilliseconds;
    private PerformanceDisplayMode _displayMode = PerformanceDisplayMode.Cycle;
    private int _metricCycleIndex;
    private int _metricSampleCount;
    private readonly List<TextBlock> _metricRows = [];
    private IDisposable? _metricsSubscription;
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private ImageSource? _artwork;
    private LyricLine? _currentLine;
    private int _currentLineIndex = -1;
    private string _lyricLine = string.Empty;
    private bool _metaScrollable;
    private double _metaCycleWidth;
    private double _metaOffset;
    private double _metaPauseRemaining;
    private bool _isLit;
    private bool _disposed;
    private FontFamily _cardBaseFont = BuildCardFont(string.Empty, string.Empty, string.Empty);
    private FontFamily _cardJapaneseFont = BuildCardFont(string.Empty, string.Empty, string.Empty);
    private string? _cardFontSignature;
    private bool _positionReady;
    private readonly DispatcherTimer _positionSaveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(600)
    };

    public MediaCardWindow(
        MediaSessionService media,
        AudioMonitorService audio,
        SystemMetricsMonitorService metrics)
    {
        _media = media;
        _audio = audio;
        _metrics = metrics;

        InitializeComponent();
        _highlightMask.GradientStops.Add(new GradientStop(Colors.White, 0));
        _highlightMask.GradientStops.Add(_maskOn);
        _highlightMask.GradientStops.Add(_maskOff);
        _highlightMask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        SetScan(0);

        var waveCenter = LitWaveHeight / 2;
        for (var index = 0; index < WavePointCount; index++)
            _wavePoints.Add(new Point(0, waveCenter));
        WavePlayed.Points = _wavePoints;

        // 透明窗口没有系统圆角：卡片自己按内容尺寸做圆角裁剪，底边进度条和模糊封面都不会溢出方角。
        CardContent.SizeChanged += (_, _) =>
        {
            UpdateCardClip();
            // 视口宽度变化后重算两个滚动视口，否则仍会停在旧的截断结果上。
            UpdateMetaScrollState();
            UpdateLyricScrollState();
        };
        UpdateCardClip();

        ApplyColorTheme();
        BuildSpectrum();

        // 先落位再显示：窗口若先以系统默认位置出现，可能正好在指针下面并被点亮一次。
        PlaceBottomRight();

        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            if (!Card.IsMouseOver && !IsKeyboardFocusWithin)
                SetLit(false);
        };
        _spectrumTimer.Tick += (_, _) => AdvanceFrame();
        _progressTimer.Tick += (_, _) => AdvanceProgress();

        _media.SnapshotChanged += OnSnapshotChanged;
        _snapshot = _media.CurrentSnapshot ?? MediaSnapshot.Disconnected;
        RenderSnapshot(_snapshot);

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
        LocationChanged += (_, _) =>
        {
            if (!_positionReady || _disposed)
                return;
            _positionSaveTimer.Stop();
            _positionSaveTimer.Start();
        };
        _positionSaveTimer.Tick += (_, _) =>
        {
            _positionSaveTimer.Stop();
            SavePosition();
        };
        ApplySettings(SettingsManager.Current.MediaCard);
        IsVisibleChanged += (_, _) =>
        {
            if (_disposed)
                return;
            if (IsVisible)
            {
                _spectrumTimer.Start();
                _progressTimer.Start();
            }
            else
            {
                _spectrumTimer.Stop();
                _progressTimer.Stop();
            }
        };
    }

    /// <summary>按卡片实际尺寸重建圆角裁剪；窗口尺寸固定，但 DPI 或布局变化后仍需跟着走。</summary>
    private void UpdateCardClip()
    {
        var width = CardContent.ActualWidth;
        var height = CardContent.ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        CardContent.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 15, 15);
    }

    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 24);
        Top = Math.Max(area.Top, area.Bottom - Height - 24);
    }

    /// <summary>
    /// 只给卡片构造的脚本映射字体：汉字、假名、拉丁分别交给用户选的字体。
    /// 它是挂在本窗口上的普通 FontFamily，不改 AppTextFontFamily 等全局资源，主程序完全不受影响。
    /// </summary>
    private static FontFamily BuildCardFont(string han, string kana, string latin)
    {
        var fallback = SystemFonts.MessageFontFamily.Source;
        var family = new FontFamily();
        family.FamilyNames.Add(XmlLanguage.GetLanguage("en-US"), "AF Media Card text");
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = CardHanRanges, Target = JoinCardFonts(han, latin, fallback) });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = CardKanaRanges, Target = JoinCardFonts(kana, latin, fallback) });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = CardAllCharacters, Target = JoinCardFonts(latin, fallback) });
        return family;
    }

    private static string JoinCardFonts(params string[] sources)
    {
        var parts = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            var exists = false;
            foreach (var part in parts)
            {
                if (string.Equals(part, source, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
                parts.Add(source);
        }
        return string.Join(", ", parts);
    }

    private static bool ContainsKana(string text)
    {
        foreach (var character in text)
        {
            if ((character >= '\u3040' && character <= '\u30FF')
                || (character >= '\u31F0' && character <= '\u31FF')
                || (character >= '\uFF61' && character <= '\uFF9F'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 含假名的行整行走日文字体（日文汉字的字形也要跟着日文字体），其余行走中文/拉丁组合。
    /// 两层歌词必须用同一套字体，逐字擦亮的位置才不会错开。
    /// </summary>
    private void ApplyCardFonts()
    {
        FontFamily = _cardBaseFont;
        ApplyCardFont(MetaText);
        ApplyCardFont(MetaRepeat);
        ApplyCardFont(LyricBaseText);
        ApplyCardFont(LyricHighlightText);
        ApplyCardFont(RomanizationText);
        ApplyCardFont(TranslationText);
    }

    private void ApplyCardFont(TextBlock block) =>
        block.FontFamily = ContainsKana(block.Text) ? _cardJapaneseFont : _cardBaseFont;

    /// <summary>恢复上次拖动到的位置；坐标已离开所有屏幕时回落到右下角。</summary>
    private void RestorePosition()
    {
        var settings = SettingsManager.Current.MediaCard;
        if (settings.Left is { } left && settings.Top is { } top && IsCardPositionVisible(left, top))
        {
            Left = left;
            Top = top;
            return;
        }
        PlaceBottomRight();
    }

    private bool IsCardPositionVisible(double left, double top)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        // 至少留出 40 DIP 可见，避免显示器拔掉后卡片整体落在屏幕外。
        return left + Width > screenLeft + 40 && left < screenRight - 40
            && top + Height > screenTop + 40 && top < screenBottom - 40;
    }

    /// <summary>位置随主程序设置持久化；只有真变动才写，避免拖动时反复刷盘。</summary>
    private void SavePosition()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top))
            return;
        var settings = SettingsManager.Current.MediaCard;
        if (settings.Left is { } savedLeft && settings.Top is { } savedTop
            && Math.Abs(savedLeft - Left) < 0.5 && Math.Abs(savedTop - Top) < 0.5)
            return;
        SettingsManager.Current.MediaCard = settings with { Left = Left, Top = Top };
    }

    /// <summary>右键复用主程序的播放器菜单（会话切换、快捷启动、设置、退出），不另造一份。</summary>
    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window is not MainWindow main)
                continue;
            main.ShowSharedContextMenu();
            e.Handled = true;
            break;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        _positionReady = true;

        // 定位后统一落回静息态；指针真的进入时再由 MouseEnter 点亮。
        _isLit = true;
        SetLit(false, animate: false);
        _spectrumTimer.Start();
        _progressTimer.Start();
        AdvanceProgress();
        // 首帧立刻画出波形，避免静息态淡入的前 33ms 是空白进度条。
        AdvanceFrame();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 位置已由拖动防抖落盘；这里不再写设置，否则设置变更会让 Alt+F4 关掉的卡片立刻自己重建。
        _positionSaveTimer.Stop();
        _disposed = true;
        _idleTimer.Stop();
        _spectrumTimer.Stop();
        _progressTimer.Stop();
        _media.SnapshotChanged -= OnSnapshotChanged;
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (_disposed)
            return;
        // 快照事件已在 UI 线程触发，直接同步更新；再套一层 BeginInvoke(Background) 会把曲目更新排到主程序后面。
        _snapshot = snapshot;
        RenderSnapshot(snapshot);
        AdvanceProgress();
    }

    private void RenderSnapshot(MediaSnapshot snapshot)
    {
        var title = string.IsNullOrWhiteSpace(snapshot.Title) ? "未在播放" : snapshot.Title;
        var meta = string.IsNullOrWhiteSpace(snapshot.Artist)
            ? title
            : $"{title}  ·  {snapshot.Artist}";
        if (!string.Equals(MetaText.Text, meta, StringComparison.Ordinal))
        {
            MetaText.Text = meta;
            MetaRepeat.Text = meta;
            ApplyCardFonts();
            UpdateMetaScrollState();
        }

        if (!ReferenceEquals(_artwork, snapshot.Artwork))
        {
            _artwork = snapshot.Artwork;
            ArtFrame.Background = snapshot.Artwork is null
                ? (Brush)Resources["CardArtFrame"]
                : new ImageBrush(snapshot.Artwork) { Stretch = Stretch.UniformToFill };

            // 封面变化时，封面取色主题需要重新提取强调色。
            if (_settings.ColorTheme == MediaCardColorTheme.FromCover)
                ApplyColorTheme();
        }

        PauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AdvanceProgress()
    {
        if (_disposed || !IsVisible)
            return;

        var now = DateTimeOffset.Now;
        UpdateLyric(now);

        var duration = _snapshot.Duration;
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, now);
        var ratio = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        // 两列都用星号时，占比是 ratio/(ratio+remaining)；只设已播放列会永远到不了 100%。
        var rest = 1 - ratio;
        LitPlayedColumn.Width = new GridLength(Math.Max(0.0001, ratio), GridUnitType.Star);
        LitRestColumn.Width = new GridLength(Math.Max(0.0001, rest), GridUnitType.Star);
        EdgePlayedColumn.Width = LitPlayedColumn.Width;
        EdgeRestColumn.Width = LitRestColumn.Width;

        LitTime.Text = FormatTime(position);
        LitDuration.Text = FormatTime(duration);
    }

    private void UpdateLyric(DateTimeOffset now)
    {
        var settings = SettingsManager.Current;
        var frame = LyricsPresentationProjector.Project(_snapshot, settings, now);
        if (!frame.IsVisible)
        {
            _currentLine = null;
            _currentLineIndex = -1;
            // 关闭实时歌词，或来源确认这首没有歌词（「纯音乐只显示标题」会屏蔽「纯音乐，请欣赏」并返回空文档）。
            var confirmedNoLyrics = _snapshot.Lyrics?.Status == LyricsResultStatus.NoLyrics
                // 专属来源通道可能不把「确认无歌词」结果挂到快照，用开关 + 取词已结束补上。
                || (settings.LyricsInstrumentalTitleOnlyEnabled
                    && _snapshot.Lyrics is null
                    && !_snapshot.IsSearchingLyrics);
            // 选项打开：主行留白，副行放「歌曲名 / 艺术家」，和单行歌词兜底口径一致；
            // 选项关闭：副行一律留白，主行直接显示歌名（纯音乐也不再空着）。
            // 搜不到歌词或检索中同样保持副行空白。
            var noLyricsToShow = !settings.LyricsEnabled || confirmedNoLyrics;
            var fillSecondary = _settings.ShowTrackInfoOnEmptyLines && noLyricsToShow;
            SetLyric(fillSecondary ? string.Empty : (string.IsNullOrWhiteSpace(_snapshot.Title) ? "未在播放" : _snapshot.Title),
                fillSecondary ? _snapshot.Title : string.Empty,
                fillSecondary ? _snapshot.Artist : string.Empty);
            return;
        }

        var lines = _snapshot.Lyrics?.Document.Lines;
        LyricLine? line = null;
        if (lines is not null && frame.CurrentLineIndex >= 0 && frame.CurrentLineIndex < lines.Count)
            line = lines[frame.CurrentLineIndex];

        if (!ReferenceEquals(line, _currentLine) || _currentLineIndex != frame.CurrentLineIndex)
        {
            _currentLine = line;
            _currentLineIndex = frame.CurrentLineIndex;
            var (romanization, translation) = ResolveCardSecondary(line, settings, lines);
            SetLyric(frame.Current, romanization, translation);
        }
    }

    /// <summary>
    /// 副行先走主程序投影器做括号清理、繁简转换与实体还原；选项开启且整首只有一行、
    /// 又没有任何罗马音和译文时，用曲名与歌手补上两行留白。
    /// 不按语言区分，中文歌只有一行时同样生效；罗马音关掉时用户本就不看这两行，不做兜底。
    /// </summary>
    private (string Romanization, string Translation) ResolveCardSecondary(
        LyricLine? line, AppSettings settings, IReadOnlyList<LyricLine>? lines)
    {
        var secondary = LyricsPresentationProjector.ResolveSecondary(line, settings);
        var romanization = _showRomanization ? secondary.Romanization : string.Empty;
        var translation = secondary.Translation;
        if (_settings.ShowTrackInfoOnEmptyLines
            && _showRomanization
            && string.IsNullOrWhiteSpace(romanization)
            && string.IsNullOrWhiteSpace(translation)
            && (lines is null || lines.Count <= 1)
            && !string.IsNullOrWhiteSpace(_snapshot.Title))
        {
            return (_snapshot.Title, _snapshot.Artist);
        }

        return (romanization, translation);
    }

    private void SetLyric(string text, string romanization, string translation)
    {
        var line = text ?? string.Empty;
        var lineChanged = !string.Equals(_lyricLine, line, StringComparison.Ordinal);
        if (lineChanged)
        {
            _lyricLine = line;
            LyricBaseText.Text = line;
            LyricHighlightText.Text = line;
            UpdateLyricScrollState();
        }

        var secondaryChanged = ApplySecondary(romanization, translation);
        if (lineChanged || secondaryChanged)
            ApplyCardFonts();
    }

    /// <summary>
    /// 曲目行超宽时铺两份相同文字，中间留一段空白，做无缝恒速循环。
    /// 周期 = 第一份宽度 + 间隔；滚满周期就归零，此时视口里正好是第二份的开头，
    /// 与归零后的第一份开头完全一致，所以看上去是「一直往前滚」而不是来回摆。
    /// </summary>
    private void UpdateMetaScrollState()
    {
        MetaText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = MetaText.DesiredSize.Width;
        var viewport = MetaViewport.ActualWidth;
        _metaScrollable = viewport > 0 && textWidth > viewport + 1;

        var repeatVisibility = _metaScrollable ? Visibility.Visible : Visibility.Collapsed;
        MetaGap.Visibility = repeatVisibility;
        MetaRepeat.Visibility = repeatVisibility;
        _metaCycleWidth = _metaScrollable ? textWidth + MetaGap.Width : 0;
        _metaOffset = 0;
        _metaPauseRemaining = 0;
        ScrollTo(MetaViewport, 0);
    }

    /// <summary>主行文本或视口宽度变化后重算滚动状态；超宽时不截断，交由每帧的平移读全。</summary>
    private void UpdateLyricScrollState()
    {
        // 先放开内容宽度，否则上一次把宽度钉在视口上会污染自然宽度测量。
        LyricContent.Width = double.NaN;
        LyricBaseText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var naturalWidth = LyricBaseText.DesiredSize.Width;
        var viewport = LyricViewport.ActualWidth;
        var scrollable = viewport > 0 && naturalWidth > viewport + 1;

        // 不滚动时把内容宽度钉在视口上，省略号才有意义；可滚动时放开到自然宽度。
        LyricContent.Width = scrollable || viewport <= 0 ? double.NaN : viewport;
        var trimming = scrollable ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        LyricBaseText.TextTrimming = trimming;
        LyricHighlightText.TextTrimming = trimming;
        if (!scrollable)
            ScrollTo(LyricViewport, 0);
    }

    /// <summary>曲目行恒速无缝循环；歌词三行改由播放进度驱动，不在这里累加时间。</summary>
    private void AdvanceMetaLoop(double delta)
    {
        if (_metaCycleWidth <= 0)
            return;

        // 一轮滚完时第二份的开头正好贴到视口左边，此时归零并停住，
        // 让歌名从开头安静地停一会儿再开始下一轮；归零本身没有视觉跳变。
        if (_metaPauseRemaining > 0)
        {
            _metaPauseRemaining -= delta;
            return;
        }

        _metaOffset += delta * MetaScrollPixelsPerSecond;
        if (_metaOffset >= _metaCycleWidth)
        {
            _metaOffset = 0;
            _metaPauseRemaining = MetaLoopPauseSeconds;
            ScrollTo(MetaViewport, 0);
            return;
        }
        ScrollTo(MetaViewport, _metaOffset);
    }

    private static void ScrollTo(ScrollViewer viewer, double offset)
    {
        // 每帧都调 ScrollToHorizontalOffset 会反复触发布局，位移小于半像素时跳过。
        if (Math.Abs(viewer.HorizontalOffset - offset) > 0.5)
            viewer.ScrollToHorizontalOffset(offset);
    }

    private bool ApplySecondary(string romanization, string translation)
    {
        var changed = false;
        // 进度定时器每次都会走到这里，只有文案真的变了才写控件，避免每帧重复触发文本布局。
        var romaji = romanization ?? string.Empty;
        if (!string.Equals(RomanizationText.Text, romaji, StringComparison.Ordinal))
        {
            RomanizationText.Text = romaji;
            RomanizationViewport.Visibility = string.IsNullOrWhiteSpace(romaji)
                ? Visibility.Collapsed
                : Visibility.Visible;
            changed = true;
        }

        var translated = translation ?? string.Empty;
        if (!string.Equals(TranslationText.Text, translated, StringComparison.Ordinal))
        {
            TranslationText.Text = translated;
            TranslationViewport.Visibility = string.IsNullOrWhiteSpace(translated)
                ? Visibility.Collapsed
                : Visibility.Visible;
            changed = true;
        }
        return changed;
    }

    /// <summary>罗马音开关切换时只刷新副行，不动主行文本；单行歌词的兜底也要跟着开关重算。</summary>
    private void RefreshSecondary()
    {
        if (_currentLine is null)
            return;
        var (romanization, translation) = ResolveCardSecondary(
            _currentLine, SettingsManager.Current, _snapshot.Lyrics?.Document.Lines);
        ApplySecondary(romanization, translation);
        ApplyCardFonts();
    }

    /// <summary>
    /// 擦亮与滚动都复用主程序的逐字时间轴策略（<see cref="LyricHighlightPolicy.ResolveProgress"/>）：
    /// 有逐字时间轴就按音节平滑推进，行级歌词返回 null 时整行常亮。擦亮开关只门控高亮层，
    /// 不门控滚动轨迹，和任务栏歌词保持一致；卡片不再自己另算一套进度。
    /// </summary>
    private void UpdateScan(double position)
    {
        if (_currentLine is null)
        {
            SetScan(1);
            // 没有歌词行时副行放的是曲目信息，按整首进度平移一次，超宽也能读全。
            var duration = _snapshot.Duration;
            var trackPhase = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
            ScrollTo(LyricViewport, 0);
            ScrollTo(RomanizationViewport, MaxOffset(RomanizationViewport) * trackPhase);
            ScrollTo(TranslationViewport, MaxOffset(TranslationViewport) * trackPhase);
            return;
        }

        var settings = SettingsManager.Current;
        var displayLine = LyricsPresentationProjector.ResolveDisplayLine(_currentLine, settings);
        var progress = LyricHighlightPolicy.ResolveProgress(displayLine, position);
        SetScan(settings.LyricsSyllableHighlightEnabled ? progress ?? 1 : 1);
        var phase = LinePhase(_currentLine, position);
        // 逐字歌词按擦亮进度定位；行级歌词在这段时间窗里匀速平移一次，不往返也不循环。
        if (progress is { } value && displayLine is { Words.Count: > 0 })
            UpdateLyricScroll(value);
        else
            ScrollTo(LyricViewport, MaxOffset(LyricViewport) * phase);
        // 两行副行同样按本行时长平移，超宽时也能读全。
        ScrollTo(RomanizationViewport, MaxOffset(RomanizationViewport) * phase);
        ScrollTo(TranslationViewport, MaxOffset(TranslationViewport) * phase);
    }

    /// <summary>本行时间窗内的线性进度；一段歌词只从开头平移到结尾一次。</summary>
    private static double LinePhase(LyricLine line, double position)
    {
        var duration = line.End - line.Start;
        return duration > 0 ? Math.Clamp((position - line.Start) / duration, 0, 1) : 0;
    }

    private static double MaxOffset(ScrollViewer viewer) =>
        Math.Max(0, viewer.ExtentWidth - viewer.ViewportWidth);

    /// <summary>逐字歌词超宽时按擦亮进度横向滚动，让当前唱到的字保持在视口 65% 锚点附近。</summary>
    private void UpdateLyricScroll(double fraction)
    {
        var maximum = MaxOffset(LyricViewport);
        if (maximum <= 0)
            return;
        ScrollTo(LyricViewport, Math.Clamp(fraction * LyricViewport.ExtentWidth - LyricViewport.ViewportWidth * 0.65, 0, maximum));
    }

    private void SetScan(double fraction)
    {
        var value = Math.Clamp(fraction, 0, 1);
        // 整行常亮时干脆摘掉蒙版，避免把所有渐变停靠点都推到 1 时的边界歧义。
        if (value >= 1)
        {
            LyricHighlightText.OpacityMask = null;
            return;
        }

        LyricHighlightText.OpacityMask ??= _highlightMask;
        // 逐字进度决定高亮边界；边界处留一小段羽化，避免一个字一个字硬跳太生硬。
        const double feather = 0.05;
        _maskOn.Offset = Math.Max(0, value - feather);
        _maskOff.Offset = Math.Min(1, value + feather);
    }

    private void BuildSpectrum()
    {
        SpectrumContent.Children.Clear();
        _bars = [];
        _waveform = null;

        // 几何沿用主程序的柱宽/柱距；超出画布宽度时整体缩放到画布内，右对齐。
        var contentWidth = SpectrumPresentationPolicy.CalculateContentWidthDip(_spectrumBandCount);
        var scale = Math.Min(1, SpectrumCanvasWidth / contentWidth);
        SpectrumScale.ScaleX = scale;
        SpectrumScale.ScaleY = scale;
        Canvas.SetLeft(SpectrumContent, SpectrumCanvasWidth - contentWidth);

        switch (_spectrumStyle)
        {
            case SpectrumStyle.Waveform:
                BuildWaveform();
                break;
            case SpectrumStyle.PixelBars:
                BuildPixelBars();
                break;
            case SpectrumStyle.MirroredBars:
                BuildBars();
                break;
            default:
                BuildBars();
                break;
        }

        BuildImmersiveSpectrum();
    }

    /// <summary>沉浸式频谱把频段左右镜像后铺满卡片底边，与右轨共用同一份采样数据。</summary>
    private void BuildImmersiveSpectrum()
    {
        ImmersiveSpectrumCanvas.Children.Clear();
        _immersiveBars = [];
        _immersiveWidth = 0;
        if (!_immersiveEnabled)
            return;

        var count = _spectrumBandCount * 2;
        var minimum = SpectrumPresentationPolicy.MinimumBarHeightDip;
        _immersiveBars = new Rectangle[count];
        for (var index = 0; index < count; index++)
        {
            var bar = new Rectangle
            {
                Height = minimum,
                Fill = _accentBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetTop(bar, ImmersiveSpectrumCanvas.Height - minimum);
            ImmersiveSpectrumCanvas.Children.Add(bar);
            _immersiveBars[index] = bar;
        }

        LayoutImmersiveBars(CardContent.ActualWidth);
    }

    /// <summary>柱距按画布实际宽度均分；宽度变化由逐帧渲染检测后重算，不必另挂布局事件。</summary>
    private void LayoutImmersiveBars(double width)
    {
        if (width <= 0 || _immersiveBars.Length == 0)
            return;

        _immersiveWidth = width;
        var pitch = width / _immersiveBars.Length;
        var barWidth = Math.Clamp(pitch * 0.28, 1.5, 3);
        for (var index = 0; index < _immersiveBars.Length; index++)
        {
            _immersiveBars[index].Width = barWidth;
            Canvas.SetLeft(_immersiveBars[index], index * pitch + (pitch - barWidth) / 2);
        }
    }

    /// <summary>镜像映射让低频落在两端、高频聚在中间，底边柱群左右对称地随音乐起伏。</summary>
    private void RenderImmersiveSpectrum()
    {
        // 宽度取卡片内容区（一定有布局值），不依赖透明 Canvas 自身的 ActualWidth。
        var width = CardContent.ActualWidth;
        var heightLimit = ImmersiveSpectrumCanvas.Height;
        if (width <= 0 || heightLimit <= 0)
            return;
        if (Math.Abs(width - _immersiveWidth) > 0.5)
            LayoutImmersiveBars(width);

        var count = _immersiveBars.Length;
        for (var index = 0; index < count; index++)
        {
            var band = index < _spectrumBandCount ? index : count - 1 - index;
            var scale = SpectrumPresentationPolicy.ResolveBarScale(
                _bands[band], _spectrumSensitivity, heightLimit);
            var height = scale * heightLimit;
            var bar = _immersiveBars[index];
            bar.Height = height;
            Canvas.SetTop(bar, heightLimit - height);
        }
    }

    private void BuildBars()
    {
        _bars = new Rectangle[_spectrumBandCount];
        for (var index = 0; index < _spectrumBandCount; index++)
        {
            var bar = new Rectangle
            {
                Width = SpectrumPresentationPolicy.BarWidthDip,
                Fill = _accentBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            SpectrumContent.Children.Add(bar);
            _bars[index] = bar;
        }
    }

    private void BuildPixelBars()
    {
        var dots = SpectrumPresentationPolicy.ResolvePixelDotCount(SpectrumCanvasHeight);
        _bars = new Rectangle[_spectrumBandCount * dots];
        var slot = 0;
        for (var index = 0; index < _spectrumBandCount; index++)
        {
            for (var dot = 0; dot < dots; dot++)
            {
                var rect = new Rectangle
                {
                    Width = SpectrumPresentationPolicy.BarWidthDip,
                    Height = SpectrumPresentationPolicy.PixelDotHeightDip,
                    Fill = _accentBrush,
                    Opacity = 0.15
                };
                Canvas.SetLeft(rect, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
                Canvas.SetTop(rect, SpectrumPresentationPolicy.ResolvePixelDotTopDip(dot, SpectrumCanvasHeight));
                SpectrumContent.Children.Add(rect);
                _bars[slot++] = rect;
            }
        }
    }

    private void BuildWaveform()
    {
        _waveform = new Polygon { Fill = _accentBrush };
        _waveformPoints = new SpectrumPoint[SpectrumPresentationPolicy.CalculateWaveformPointCount(_spectrumBandCount)];
        SpectrumContent.Children.Add(_waveform);
    }

    /// <summary>每一帧推进擦亮、横向游走、频谱与点亮态波浪进度条；共用同一个 33ms 计时器。</summary>
    private void AdvanceFrame()
    {
        if (_disposed || !IsVisible)
            return;

        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.Now);
        var now = DateTimeOffset.UtcNow;
        var delta = Math.Clamp((now - _lastFrameUtc).TotalSeconds, 0, 0.1);
        _lastFrameUtc = now;

        UpdateScan(position);
        AdvanceMetaLoop(delta);
        var wavy = UseWavyProgress;
        if (wavy)
            AdvanceWavePhase(delta);
        if (!_spectrumVisible && !wavy)
            return;

        if (_spectrumVisible)
        {
            try
            {
                if (!_audio.GetSpectrum(_bands, _spectrumBandCount))
                    Array.Clear(_bands, 0, _spectrumBandCount);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[MediaCard] spectrum unavailable: {exception.Message}");
                Array.Clear(_bands, 0, _spectrumBandCount);
            }

            if (UseImmersiveSpectrum)
                RenderImmersiveSpectrum();
            else
                RenderSpectrum();
        }

        if (wavy)
        {
            // 频谱被关掉时不再采样，波浪回到纯正弦。
            var target = _spectrumVisible ? ResolveLowBandEnergy() : 0;
            _waveEnergy += (target - _waveEnergy) * 0.12;
            RenderWaveProgress(position);
        }
    }

    /// <summary>沉浸式开启且处于静息态时，底边柱群才是当前可见的频谱。</summary>
    private bool UseImmersiveSpectrum => _immersiveEnabled && _spectrumVisible && !_isLit;

    /// <summary>波浪进度条只画在点亮态；静息态仍是底边细直条。</summary>
    private bool UseWavyProgress => _settings.WavyProgressBar && _isLit;

    /// <summary>相位按实际帧间隔推进；暂停时冻结波形。</summary>
    private void AdvanceWavePhase(double delta)
    {
        if (_snapshot.IsPlaying)
            _wavePhase += delta * Math.PI * 2 * WaveCyclesPerSecond;
    }

    /// <summary>低频能量让波形和频谱同节奏；采样不可用时返回 0。</summary>
    private double ResolveLowBandEnergy()
    {
        var count = Math.Min(6, _spectrumBandCount);
        var sum = 0d;
        for (var index = 0; index < count; index++)
            sum += _bands[index];
        return count > 0 ? Math.Clamp(sum / count, 0, 1) : 0;
    }

    /// <summary>
    /// 已播放段按正弦波绘制并在播放头前收束到基线，剩余段是直线；播放头是竖条。
    /// 进度每帧直接取主程序的时间轴推算值，不依赖 250ms 进度定时器，长时间播放也不会跳格。
    /// </summary>
    private void RenderWaveProgress(double position)
    {
        // 取外层 Grid 而非 Canvas 自身：Canvas 的期望尺寸为 0，布局未完成时拿不到可靠宽度。
        var width = LitProgressTrack.ActualWidth;
        if (width <= 0)
            return;

        var duration = _snapshot.Duration;
        var ratio = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        var head = ratio * width;
        var center = LitWaveHeight / 2;
        var amplitude = WaveBaseAmplitude + WaveEnergyAmplitude * _waveEnergy;
        for (var index = 0; index < WavePointCount; index++)
        {
            var x = head * index / (WavePointCount - 1);
            var taper = Math.Min(1, (head - x) / WaveHeadTaperDip);
            var wave = Math.Sin(x * (Math.PI * 2 / WaveLengthDip) - _wavePhase);
            _wavePoints[index] = new Point(x, center - amplitude * taper * wave);
        }

        WavePlayed.Visibility = head > 2 ? Visibility.Visible : Visibility.Collapsed;
        WaveTrack.X1 = head;
        WaveTrack.Y1 = center;
        WaveTrack.X2 = width;
        WaveTrack.Y2 = center;
        Canvas.SetLeft(WaveHead, Math.Clamp(head - WaveHead.Width / 2, 0, Math.Max(0, width - WaveHead.Width)));
        // 点集合是原地改坐标的，显式失效一次确保 Polyline 每帧都重画。
        WavePlayed.InvalidateVisual();
    }

    private void RenderSpectrum()
    {
        switch (_spectrumStyle)
        {
            case SpectrumStyle.Waveform:
                RenderWaveform();
                break;
            case SpectrumStyle.PixelBars:
                RenderPixelBars();
                break;
            case SpectrumStyle.MirroredBars:
                RenderBars(mirrored: true);
                break;
            default:
                RenderBars(mirrored: false);
                break;
        }
    }

    private void RenderBars(bool mirrored)
    {
        for (var index = 0; index < _bars.Length; index++)
        {
            var scale = SpectrumPresentationPolicy.ResolveBarScale(_bands[index], _spectrumSensitivity, SpectrumCanvasHeight);
            var height = scale * SpectrumCanvasHeight;
            var bar = _bars[index];
            bar.Height = height;
            Canvas.SetTop(bar, mirrored ? (SpectrumCanvasHeight - height) / 2 : SpectrumCanvasHeight - height);
        }
    }

    private void RenderPixelBars()
    {
        var dots = SpectrumPresentationPolicy.ResolvePixelDotCount(SpectrumCanvasHeight);
        for (var index = 0; index < _spectrumBandCount; index++)
        {
            var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(_bands[index], _spectrumSensitivity, SpectrumCanvasHeight);
            for (var dot = 0; dot < dots; dot++)
                _bars[index * dots + dot].Opacity = dot < lit ? 1 : 0.15;
        }
    }

    private void RenderWaveform()
    {
        if (_waveform is null)
            return;

        var count = SpectrumPresentationPolicy.WriteWaveformOutline(
            _bands.AsSpan(0, _spectrumBandCount),
            _spectrumSensitivity,
            SpectrumCanvasHeight,
            _waveformPoints);
        var points = new PointCollection(count);
        for (var index = 0; index < count; index++)
            points.Add(new Point(_waveformPoints[index].X, _waveformPoints[index].Y));
        _waveform.Points = points;
    }

    private void OnMetrics(SystemMetricsSnapshot snapshot)
    {
        if (_disposed)
            return;
        // 采样回调已经由服务派发到 UI 线程，直接更新即可。
        RenderMetrics(snapshot);
    }

    private void RenderMetrics(SystemMetricsSnapshot snapshot)
    {
        if (_metricRows.Count == 0)
            return;

        if (_displayMode == PerformanceDisplayMode.Parallel)
        {
            for (var index = 0; index < _metricRows.Count && index < _metricKinds.Count; index++)
            {
                var kind = _metricKinds[index];
                _metricRows[index].TextAlignment = ResolveMetricAlignment(kind);
                _metricRows[index].Text = MetricPresentationPolicy.Format(kind, snapshot);
            }
            return;
        }

        // 轮换：每 3 个采样推进一个指标。
        _metricCycleIndex = MetricPresentationPolicy.Advance(_metricCycleIndex, ++_metricSampleCount, _metricKinds.Count);
        var current = _metricKinds[_metricCycleIndex];
        _metricRows[0].TextAlignment = ResolveMetricAlignment(current);
        _metricRows[0].Text = MetricPresentationPolicy.Format(current, snapshot);
    }

    /// <summary>网速上下行单位不同，右对齐会对不齐，改成左对齐让「↑」「↓」和数字左缘统一。</summary>
    private static TextAlignment ResolveMetricAlignment(MetricKind kind) =>
        kind == MetricKind.SystemNetwork ? TextAlignment.Left : TextAlignment.Right;

    private void SetLit(bool lit, bool animate = true)
    {
        if (_isLit == lit)
            return;

        _isLit = lit;
        var duration = animate && MotionPolicy.ResolveCurrent().UseTransitions
            ? TimeSpan.FromMilliseconds(220)
            : TimeSpan.Zero;

        Animate(ArtScale, ScaleTransform.ScaleXProperty, lit ? CoverScaleLit : 1, duration);
        Animate(ArtScale, ScaleTransform.ScaleYProperty, lit ? CoverScaleLit : 1, duration);
        Animate(SecondaryLines, UIElement.OpacityProperty, lit ? 0 : 1, duration);
        Animate(ControlsPanel, UIElement.OpacityProperty, lit ? 1 : 0, duration);
        Animate(LitProgress, UIElement.OpacityProperty, lit ? 1 : 0, duration);
        ApplyRestingPlacement(lit, duration);
        Animate(VitalsPanel, UIElement.OpacityProperty, lit ? 1 : 0.55, duration);
        Animate(HeadDot, UIElement.OpacityProperty, lit ? 1 : 0, duration);

        ControlsPanel.IsHitTestVisible = lit;
    }

    private static void Animate(DependencyObject target, DependencyProperty property, double value, TimeSpan duration)
    {
        // 取当前生效值（含正在跑的动画）作为起点，否则展开时从基值到基值等于没动画。
        var from = target.GetValue(property) is double current ? current : value;
        if (target is IAnimatable animatable)
        {
            animatable.BeginAnimation(property, null);
            if (duration > TimeSpan.Zero)
            {
                animatable.BeginAnimation(property, new DoubleAnimation(from, value, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                return;
            }
        }

        target.SetValue(property, value);
    }

    private void Card_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _idleTimer.Stop();
        SetLit(true);
    }

    private void Card_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void Card_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject) is not null)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static Button? FindButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button button)
                return button;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void PreviousButton_OnClick(object sender, RoutedEventArgs e) => _ = _media.SkipPreviousAsync();

    private void PlayPauseButton_OnClick(object sender, RoutedEventArgs e) => _ = _media.TogglePlayPauseAsync();

    private void NextButton_OnClick(object sender, RoutedEventArgs e) => _ = _media.SkipNextAsync();

    /// <summary>应用设置页里的显示选项；应用层在窗口可见期间也会调用。</summary>
    public void ApplySettings(MediaCardSettings settings)
    {
        var themeChanged = settings.ColorTheme != _settings.ColorTheme;
        var romanizationChanged = settings.ShowRomanization != _showRomanization;
        var trackInfoChanged = settings.ShowTrackInfoOnEmptyLines != _settings.ShowTrackInfoOnEmptyLines;
        var immersiveChanged = settings.ImmersiveSpectrum != _settings.ImmersiveSpectrum;
        var wavyChanged = settings.WavyProgressBar != _settings.WavyProgressBar;
        _settings = settings;
        _showRomanization = settings.ShowRomanization;
        Topmost = settings.Topmost;

        var fontSignature = string.Join('\u001F', settings.ChineseFontFamily, settings.JapaneseFontFamily, settings.EnglishFontFamily);
        if (fontSignature != _cardFontSignature)
        {
            _cardFontSignature = fontSignature;
            _cardBaseFont = BuildCardFont(settings.ChineseFontFamily, settings.JapaneseFontFamily, settings.EnglishFontFamily);
            _cardJapaneseFont = BuildCardFont(settings.JapaneseFontFamily, settings.JapaneseFontFamily, settings.EnglishFontFamily);
            ApplyCardFonts();
        }

        RefreshComponentSettings();

        if (themeChanged)
            ApplyColorTheme();
        else
            RefreshPinVisual();

        if (romanizationChanged || trackInfoChanged)
            RefreshSecondary();
        if (immersiveChanged || wavyChanged)
            ApplyRestingPlacement(_isLit, TimeSpan.Zero);
    }

    /// <summary>
    /// 静息态让右轨频谱移到卡片底边、性能指标补到右上角，并恢复底边细直条；
    /// 点亮态复位右轨频谱。进度条样式（直线 / 波浪）由设置决定，切换时立即落位。
    /// </summary>
    private void ApplyRestingPlacement(bool lit, TimeSpan duration)
    {
        var immersive = _settings.ImmersiveSpectrum;
        var resting = immersive && !lit;
        Animate(SpectrumCanvas, UIElement.OpacityProperty,
            immersive ? (lit ? 1 : 0) : (lit ? 1 : 0.55), duration);
        Animate(ImmersiveSpectrumCanvas, UIElement.OpacityProperty,
            resting ? ImmersiveSpectrumOpacity : 0, duration);
        Animate(EdgeProgress, UIElement.OpacityProperty, lit ? 0 : 1, duration);
        VitalsPanel.VerticalAlignment = resting ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        // 只切点亮态进度条的两层显隐，列宽与时间文本照常更新。
        LitWaveCanvas.Opacity = _settings.WavyProgressBar ? 1 : 0;
        LitLinearProgress.Opacity = _settings.WavyProgressBar ? 0 : 1;
    }

    /// <summary>频谱显隐与参数按卡片设置决定（可跟随主程序或自定义），性能指标按卡片开关过滤，采样节奏仍跟随主程序。</summary>
    private void RefreshComponentSettings()
    {
        var current = SettingsManager.Current;
        var card = current.MediaCard;
        var spectrum = current.SpectrumComponent;
        var performance = current.PerformanceComponent;

        // 频谱：显隐由卡片开关决定，参数按模式取主程序或卡片自定义。
        var spectrumVisible = card.ShowSpectrum;
        var immersive = card.ImmersiveSpectrum;
        var (bandCount, style, sensitivity) = card.SpectrumMode == DesktopSpectrumMode.Custom
            ? (card.SpectrumBarCount, card.SpectrumStyle, card.SpectrumSensitivity)
            : (spectrum.BandCount, spectrum.Style, spectrum.SensitivityPercent);
        var spectrumChanged = spectrumVisible != _spectrumVisible
            || immersive != _immersiveEnabled
            || bandCount != _spectrumBandCount
            || style != _spectrumStyle
            || sensitivity != _spectrumSensitivity;
        _spectrumVisible = spectrumVisible;
        _immersiveEnabled = immersive;
        _spectrumBandCount = bandCount;
        _spectrumStyle = style;
        _spectrumSensitivity = sensitivity;
        SpectrumCanvas.Visibility = spectrumVisible ? Visibility.Visible : Visibility.Collapsed;
        ImmersiveSpectrumCanvas.Visibility = spectrumVisible && immersive ? Visibility.Visible : Visibility.Collapsed;
        if (spectrumChanged)
            BuildSpectrum();

        // 性能：指标由卡片开关决定，采样间隔与轮换方式仍跟随主程序性能组件。
        var metricKinds = ResolveCardMetrics(card);
        var performanceVisible = metricKinds.Count > 0;
        var metricsChanged = performanceVisible != _performanceVisible
            || !metricKinds.SequenceEqual(_metricKinds)
            || performance.RefreshIntervalMilliseconds != _metricsIntervalMs
            || performance.DisplayMode != _displayMode;
        _performanceVisible = performanceVisible;
        _metricKinds = metricKinds;
        _metricsIntervalMs = performance.RefreshIntervalMilliseconds;
        _displayMode = performance.DisplayMode;
        if (metricsChanged)
            RebuildMetricsSubscription();
    }

    private static IReadOnlyList<MetricKind> ResolveCardMetrics(MediaCardSettings card)
    {
        var metrics = new List<MetricKind>(4);
        if (card.ShowCpu) metrics.Add(MetricKind.SystemCpu);
        if (card.ShowGpu) metrics.Add(MetricKind.SystemGpu);
        if (card.ShowMemory) metrics.Add(MetricKind.SystemMemory);
        if (card.ShowNetwork) metrics.Add(MetricKind.SystemNetwork);
        return metrics;
    }

    private void RebuildMetricsSubscription()
    {
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
        _metricCycleIndex = 0;
        _metricSampleCount = 0;
        _metricRows.Clear();
        VitalsPanel.Children.Clear();

        if (!_performanceVisible || _metricKinds.Count == 0)
        {
            VitalsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        VitalsPanel.Visibility = Visibility.Visible;
        var rowCount = _displayMode == PerformanceDisplayMode.Parallel ? _metricKinds.Count : 1;
        for (var index = 0; index < rowCount; index++)
        {
            var text = new TextBlock
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                Foreground = (Brush)Resources["CardTextSecondary"],
                TextAlignment = TextAlignment.Right
            };
            VitalsPanel.Children.Add(text);
            _metricRows.Add(text);
        }

        try
        {
            _metricsSubscription = _metrics.Subscribe(_metricKinds, TimeSpan.FromMilliseconds(_metricsIntervalMs), OnMetrics);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[MediaCard] metrics unavailable: {exception.Message}");
        }
    }

    /// <summary>按当前主题重建所有配色资源，并把强调色应用到频谱条与置顶图标。</summary>
    private void ApplyColorTheme()
    {
        var light = _settings.ColorTheme == MediaCardColorTheme.Light;
        var accent = _settings.ColorTheme == MediaCardColorTheme.FromCover
            ? ResolveCoverAccent()
            : light ? LightAccent : DarkAccent;

        SetResource("CardBackground", light ? LightBackground : DarkBackground);
        SetResource("CardBorder", light ? LightBorder : DarkBorder);
        SetResource("CardTextPrimary", light ? LightTextPrimary : DarkTextPrimary);
        SetResource("CardTextSecondary", light ? LightTextSecondary : DarkTextSecondary);
        SetResource("CardTextDim", light ? LightTextDim : DarkTextDim);
        SetResource("CardTextRomanization", light ? LightTextRomanization : DarkTextRomanization);
        SetResource("CardTextTranslation", light ? LightTextTranslation : DarkTextTranslation);
        SetResource("CardAccent", accent);
        SetResource("CardChromeBorder", light ? LightChromeBorder : DarkChromeBorder);
        SetResource("CardChromeHover", light ? LightChromeHover : DarkChromeHover);
        SetResource("CardChromePressed", light ? LightChromePressed : DarkChromePressed);
        SetResource("CardProgressTrack", light ? LightProgressTrack : DarkProgressTrack);
        SetResource("CardArtFrame", light ? LightArtFrame : DarkArtFrame);
        SetResource("CardChipForeground", light ? LightChipForeground : DarkChipForeground);

        _accentBrush = Freeze(new SolidColorBrush(accent));

        foreach (var bar in _bars)
            bar.Fill = _accentBrush;
        foreach (var bar in _immersiveBars)
            bar.Fill = _accentBrush;
        if (_waveform is not null)
            _waveform.Fill = _accentBrush;
        foreach (var row in _metricRows)
            row.Foreground = (Brush)Resources["CardTextSecondary"];
        if (_artwork is null)
            ArtFrame.Background = (Brush)Resources["CardArtFrame"];

        UpdateCoverBackdrop();
        RefreshPinVisual();
    }

    /// <summary>封面取色模式下用高斯模糊的封面作卡片背景；其余主题隐藏该层，回到纯色背景。</summary>
    private void UpdateCoverBackdrop()
    {
        var fromCover = _settings.ColorTheme == MediaCardColorTheme.FromCover;
        CoverBackdrop.Source = fromCover ? _artwork : null;
        CoverBackdropLayer.Opacity = fromCover && _artwork is not null ? 1 : 0;
    }

    private void SetResource(string key, Color color) => Resources[key] = Freeze(new SolidColorBrush(color));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private Color ResolveCoverAccent()
    {
        // 直接对媒体卡片的封面采样，不依赖任务栏封面的缩略图缓存（两者加载路径不同）。
        if (_artwork is not null && ExtractDominantColor(_artwork) is { } color)
            return color;

        return DarkAccent;
    }

    private static Color? ExtractDominantColor(ImageSource source)
    {
        if (source is not BitmapSource bitmap)
            return null;

        try
        {
            var sampled = SampleForColor(bitmap, 64);
            var formatted = new FormatConvertedBitmap(sampled, PixelFormats.Bgra32, null, 0);
            var width = formatted.PixelWidth;
            var height = formatted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            var colors = DominantColorCalculator.Calculate(pixels, width, height, 1, darkTheme: true);
            return colors.Count > 0 ? colors[0] : null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[MediaCard] cover color extraction failed: {exception.Message}");
            return null;
        }
    }

    private static BitmapSource SampleForColor(BitmapSource source, int maximumSize)
    {
        var longestEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longestEdge <= maximumSize)
            return source;

        var scale = maximumSize / (double)longestEdge;
        var transformed = new TransformedBitmap();
        transformed.BeginInit();
        transformed.Source = source;
        transformed.Transform = new ScaleTransform(scale, scale);
        transformed.EndInit();
        transformed.Freeze();
        return transformed;
    }

    private void RefreshPinVisual() =>
        PinIcon.Foreground = Topmost ? _accentBrush : (Brush)Resources["CardChipForeground"];

    private void PinButton_OnClick(object sender, RoutedEventArgs e) =>
        SettingsManager.Current.MediaCard =
            SettingsManager.Current.MediaCard with { Topmost = !Topmost };

    private void ThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var next = _settings.ColorTheme switch
        {
            MediaCardColorTheme.Dark => MediaCardColorTheme.Light,
            MediaCardColorTheme.Light => MediaCardColorTheme.FromCover,
            _ => MediaCardColorTheme.Dark
        };
        SettingsManager.Current.MediaCard =
            SettingsManager.Current.MediaCard with { ColorTheme = next };
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) =>
        SettingsManager.Current.MediaCard =
            SettingsManager.Current.MediaCard with { Enabled = false };

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
            return "00:00";
        var total = (int)seconds;
        return FormattableString.Invariant($"{total / 60:00}:{total % 60:00}");
    }
}
