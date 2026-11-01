// 单个宿主拥有气泡与超时；WPF Popup 负责工作区边缘调整，卸载时主动关闭。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

internal sealed class PetSpeechBubble : IDisposable
{
    private readonly Popup _popup;
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly Border _surface;

    internal PetSpeechBubble()
    {
        var close = new Button { Content = "×", MinWidth = 28, MinHeight = 28, VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(FrameworkElement.ToolTipProperty, "Loc.Common.Close");
        System.Windows.Automation.AutomationProperties.SetName(close, Translations.Get("Common.Close"));
        close.Click += (_, _) => Close();
        var panel = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        panel.Children.Add(close);
        panel.Children.Add(_text);
        _surface = new Border { Child = panel, CornerRadius = new CornerRadius(12), Padding = new Thickness(14), BorderThickness = new Thickness(1) };
        _surface.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        _surface.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _popup = new Popup { Child = _surface, AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Top, VerticalOffset = -8 };
        _surface.MouseEnter += (_, _) => _timer.Stop();
        _surface.MouseLeave += (_, _) => { if (_popup.IsOpen) _timer.Start(); };
        _surface.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Escape) Close(); };
        _timer.Tick += OnTimeout;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_text, System.Windows.Automation.AutomationLiveSetting.Polite);
    }

    internal bool Show(FrameworkElement anchor, string text, bool replace = true)
    {
        if (!anchor.IsVisible || !anchor.IsLoaded || PresentationSource.FromVisual(anchor) is null) return false;
        if (!replace && _popup.IsOpen && _text.Text != text) return false;
        _popup.PlacementTarget = anchor;
        _text.Text = text;
        _popup.IsOpen = true;
        _timer.Stop();
        _timer.Start();
        return true;
    }

    private void OnTimeout(object? sender, EventArgs args) => Close();
    internal void Close() { _timer.Stop(); _popup.IsOpen = false; _popup.PlacementTarget = null; }
    public void Dispose() { Close(); _timer.Tick -= OnTimeout; }
}
