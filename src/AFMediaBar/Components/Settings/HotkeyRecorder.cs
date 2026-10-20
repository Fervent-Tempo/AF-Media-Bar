// 只表达录制输入；IsRecording 由 ViewModel 转为注册暂停租约，控件不拥有全局快捷键服务。
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Classes.Services.Input;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

/// <summary>可通过 Tab 到达的快捷键录制框；Esc 取消，退格或 Delete 清除。</summary>
public sealed class HotkeyRecorder : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(nameof(Hotkey), typeof(string), typeof(HotkeyRecorder),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyChanged));
    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(nameof(IsRecording), typeof(bool), typeof(HotkeyRecorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string Hotkey { get => (string)GetValue(HotkeyProperty); set => SetValue(HotkeyProperty, value); }
    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }
    public ICommand ClearHotkeyCommand { get; }

    public HotkeyRecorder()
    {
        ClearHotkeyCommand = new RelayCommand(ClearHotkey);
        SetResourceReference(StyleProperty, "AfHotkeyRecorderStyle");
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Loaded += (_, _) => RefreshText();
        Unloaded += (_, _) => SetCurrentValue(IsRecordingProperty, false);
    }

    private static void OnHotkeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((HotkeyRecorder)sender).RefreshText();
    private void RefreshText() => Text = string.IsNullOrWhiteSpace(Hotkey) ? Translations.Get("Device.Hotkey.Record") : Hotkey;
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (!ReferenceEquals(e.NewFocus, this)) return;
        SetCurrentValue(IsRecordingProperty, true);
        Text = Translations.Get("Device.Hotkey.Recording");
    }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        SetCurrentValue(IsRecordingProperty, false);
        RefreshText();
        base.OnLostKeyboardFocus(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsKeyboardFocused) { base.OnPreviewKeyDown(e); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        if (key == Key.Escape) { Finish(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        { ClearHotkey(); return; }
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        var gesture = string.Join("+", parts);
        if (modifiers.HasFlag(ModifierKeys.Windows) || !AudioDeviceHotkeyService.TryParse(gesture, out _, out _))
        { Text = Translations.Get("Device.Hotkey.Invalid"); return; }
        SetCurrentValue(HotkeyProperty, gesture);
        Finish();
    }
    private void Finish()
    {
        SetCurrentValue(IsRecordingProperty, false);
        RefreshText();
        Keyboard.ClearFocus();
    }

    private void ClearHotkey()
    {
        SetCurrentValue(HotkeyProperty, string.Empty);
        Finish();
    }
}
