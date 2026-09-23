using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AFMediaBar.Classes.Services;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
    /// <summary>
    /// 设置页说明条：用主题画刷呈现一段解释性文字，替代以前写死色值的 Border。
    /// Settings callout: presents one explanatory note with theme brushes instead of a hardcoded Border colour.
    ///
    /// 只承载文案和语气，不持有状态、不读取设置，也不参与任务栏或灵动岛的呈现路径。
    /// It carries text and tone only: no state, no settings access, and it never joins the taskbar or
    /// dynamic-island presentation path.
    /// </summary>
    public partial class SettingsCallout : UserControl
    {
        /// <summary>说明条左侧图标的标识。/ Identifies the glyph shown before the note.</summary>
        public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
            nameof(Symbol),
            typeof(SymbolRegular),
            typeof(SettingsCallout),
            new PropertyMetadata(SymbolRegular.Info24));

        /// <summary>说明条正文；调用方负责给出完整句子。/ Callout body; the caller supplies a complete sentence.</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SettingsCallout),
            new PropertyMetadata(string.Empty));

        /// <summary>
        /// 是否使用主色语气。主色用于“必须知道的约束”，中性用于“有用但可忽略的提示”。
        /// Whether the accent tone is used. Accent marks a constraint the user must know; neutral marks a hint.
        /// </summary>
        public static readonly DependencyProperty IsAccentProperty = DependencyProperty.Register(
            nameof(IsAccent),
            typeof(bool),
            typeof(SettingsCallout),
            new PropertyMetadata(false));

        /// <summary>
        /// 创建说明条。/ Creates the callout.
        /// </summary>
        public SettingsCallout()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception exception) when (exception is IOException or System.Windows.Markup.XamlParseException)
            {
                // 增量编译/热重载偶尔会留下一个缺少该 BAML 的临时程序集。说明条不是设置页的关键路径：
                // 用等价的最小视觉树继续显示设置，并把完整异常写入日志，不能让一次右键打开设置把进程带崩。
                // Incremental builds or hot reload can occasionally leave a temporary assembly without this BAML. A callout is not
                // critical to the settings page, so keep the page usable with an equivalent minimal tree and log the full exception
                // instead of letting a right-click settings action terminate the process.
                AppLogService.Current?.Error(
                    "Settings",
                    "说明条 XAML 资源不可用，已使用安全后备 / SettingsCallout XAML unavailable; using safe fallback",
                    exception);
                BuildFallbackContent();
            }
        }

        private void BuildFallbackContent()
        {
            var glyph = new SymbolIcon
            {
                Margin = new Thickness(0, 1, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                FontSize = 16
            };
            glyph.SetBinding(SymbolIcon.SymbolProperty, new Binding(nameof(Symbol)) { Source = this });
            glyph.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

            var body = new System.Windows.Controls.TextBlock
            {
                FontSize = 12,
                LineHeight = 18,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap
            };
            body.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding(nameof(Text)) { Source = this });
            body.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
            Grid.SetColumn(body, 1);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(glyph);
            content.Children.Add(body);

            var surface = new Border
            {
                Padding = new Thickness(14, 12, 14, 12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = content
            };
            surface.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
            surface.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            Content = surface;
        }

        /// <summary>左侧图标；默认是信息图标。/ Leading glyph; defaults to the information icon.</summary>
        public SymbolRegular Symbol
        {
            get => (SymbolRegular)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        /// <summary>说明正文。/ The note body.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>是否使用主色语气；默认关闭。/ Whether the accent tone is used; off by default.</summary>
        public bool IsAccent
        {
            get => (bool)GetValue(IsAccentProperty);
            set => SetValue(IsAccentProperty, value);
        }
    }
}
