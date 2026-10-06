// Owns the draft text and slider for one numeric setting; only committed finite values reach its binding, on the UI thread.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>Pairs a settings slider with exact input while preserving the setting's range, precision, and unit.</summary>
[TemplatePart(Name = "PART_Slider", Type = typeof(Slider))]
[TemplatePart(Name = "PART_Input", Type = typeof(TextBox))]
public sealed class SettingsNumericEditor : Control
{
    /// <summary>The committed numeric value; drafts do not update it.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(SettingsNumericEditor), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    /// <summary>Current minimum, including dynamic taskbar constraints.</summary>
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    /// <summary>Current maximum, including dynamic taskbar constraints.</summary>
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 100d);
    /// <summary>Legal numeric precision, independent of accelerated slider adjustment.</summary>
    public static readonly DependencyProperty StepProperty = Register(nameof(Step), 1d);
    /// <summary>Whether dragging snaps to slider ticks; smooth dynamic-width sliders retain their original behavior.</summary>
    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(nameof(IsSnapToTickEnabled),
        typeof(bool), typeof(SettingsNumericEditor), new PropertyMetadata(true, OnOptionsChanged));
    /// <summary>Localized unit displayed outside the input.</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string),
        typeof(SettingsNumericEditor), new PropertyMetadata(string.Empty, OnOptionsChanged));
    /// <summary>Localized format for range and precision help.</summary>
    public static readonly DependencyProperty RangeFormatProperty = DependencyProperty.Register(nameof(RangeFormat), typeof(string),
        typeof(SettingsNumericEditor), new PropertyMetadata("{0}–{1} {2}; {3}", OnOptionsChanged));
    /// <summary>Localized invalid-input feedback.</summary>
    public static readonly DependencyProperty InvalidMessageProperty = DependencyProperty.Register(nameof(InvalidMessage), typeof(string),
        typeof(SettingsNumericEditor), new PropertyMetadata(string.Empty, OnOptionsChanged));
    /// <summary>Localized format for a value adjusted to the legal step.</summary>
    public static readonly DependencyProperty AdjustedFormatProperty = DependencyProperty.Register(nameof(AdjustedFormat), typeof(string),
        typeof(SettingsNumericEditor), new PropertyMetadata("{0} {1}", OnOptionsChanged));
    private static readonly DependencyPropertyKey HelpTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HelpText),
        typeof(string), typeof(SettingsNumericEditor), new PropertyMetadata(string.Empty));
    /// <summary>Current localized range or validation feedback.</summary>
    public static readonly DependencyProperty HelpTextProperty = HelpTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasInputErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasInputError),
        typeof(bool), typeof(SettingsNumericEditor), new PropertyMetadata(false));
    /// <summary>Whether the latest attempted commit was invalid.</summary>
    public static readonly DependencyProperty HasInputErrorProperty = HasInputErrorPropertyKey.DependencyProperty;

    private Slider? _slider;
    private TextBox? _input;
    private bool _synchronizing;
    private bool _adjusted;

    /// <summary>The value exposed to the page's existing setting binding.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Minimum permitted input.</summary>
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    /// <summary>Maximum permitted input.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Permitted increment for text input.</summary>
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    /// <summary>Whether dragging uses tick snapping.</summary>
    public bool IsSnapToTickEnabled { get => (bool)GetValue(IsSnapToTickEnabledProperty); set => SetValue(IsSnapToTickEnabledProperty, value); }
    /// <summary>Unit shown beside the input.</summary>
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>Localized range-help format.</summary>
    public string RangeFormat { get => (string)GetValue(RangeFormatProperty); set => SetValue(RangeFormatProperty, value); }
    /// <summary>Localized error text.</summary>
    public string InvalidMessage { get => (string)GetValue(InvalidMessageProperty); set => SetValue(InvalidMessageProperty, value); }
    /// <summary>Localized adjusted-value format.</summary>
    public string AdjustedFormat { get => (string)GetValue(AdjustedFormatProperty); set => SetValue(AdjustedFormatProperty, value); }
    /// <summary>Help shown by the input tooltip and accessibility description.</summary>
    public string HelpText => (string)GetValue(HelpTextProperty);
    /// <summary>Indicates invalid input without replacing the last committed setting.</summary>
    public bool HasInputError => (bool)GetValue(HasInputErrorProperty);

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (_slider is not null) _slider.ValueChanged -= OnSliderChanged;
        if (_input is not null)
        {
            _input.PreviewKeyDown -= OnInputKeyDown;
            _input.LostKeyboardFocus -= OnInputLostFocus;
            _input.TextChanged -= OnInputTextChanged;
        }
        base.OnApplyTemplate();
        _slider = GetTemplateChild("PART_Slider") as Slider;
        _input = GetTemplateChild("PART_Input") as TextBox;
        if (_slider is not null) _slider.ValueChanged += OnSliderChanged;
        if (_input is not null)
        {
            _input.PreviewKeyDown += OnInputKeyDown;
            _input.LostKeyboardFocus += OnInputLostFocus;
            _input.TextChanged += OnInputTextChanged;
        }
        Refresh();
    }

    /// <summary>Commits the current draft once; invalid drafts leave the setting unchanged.</summary>
    public bool TryCommit()
    {
        if (_input is null) return false;
        if (!SettingsNumericInputPolicy.TryResolve(_input.Text, CultureInfo.CurrentCulture, Minimum, Maximum, Step, out var value))
        {
            SetValue(HasInputErrorPropertyKey, true);
            _adjusted = false;
            RefreshHelp();
            return false;
        }
        _adjusted = !double.TryParse(_input.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || Math.Abs(value - parsed) > 1e-8d;
        SetValue(HasInputErrorPropertyKey, false);
        SetCurrentValue(ValueProperty, value);
        // Setters can normalize in domain units (for example seconds stored as milliseconds). Show what actually survived.
        GetBindingExpression(ValueProperty)?.UpdateTarget();
        Refresh(forceText: true);
        return true;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { TryCommit(); args.Handled = true; }
        else if (args.Key == Key.Escape)
        {
            SetValue(HasInputErrorPropertyKey, false);
            _adjusted = false;
            Refresh(forceText: true);
            args.Handled = true;
        }
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (!TryCommit()) Refresh(forceText: true);
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_synchronizing) return;
        SetValue(HasInputErrorPropertyKey, false);
        _adjusted = false;
        RefreshHelp();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (_synchronizing) return;
        SetValue(HasInputErrorPropertyKey, false);
        _adjusted = false;
        SetCurrentValue(ValueProperty, args.NewValue);
        GetBindingExpression(ValueProperty)?.UpdateTarget();
        Refresh(forceText: true);
    }

    private void Refresh(bool forceText = false)
    {
        _synchronizing = true;
        try
        {
            if (_slider is not null && double.IsFinite(Minimum) && double.IsFinite(Maximum) && Maximum >= Minimum)
            {
                _slider.Maximum = Maximum;
                _slider.Minimum = Minimum;
                _slider.TickFrequency = Step;
                _slider.SmallChange = Step;
                _slider.IsSnapToTickEnabled = IsSnapToTickEnabled;
                _slider.Value = Value;
            }
            if (_input is not null && (forceText || !_input.IsKeyboardFocusWithin))
                _input.Text = Format(Value);
            RefreshHelp();
        }
        finally { _synchronizing = false; }
    }

    private void RefreshHelp() => SetValue(HelpTextPropertyKey, HasInputError ? InvalidMessage :
        _adjusted ? string.Format(CultureInfo.CurrentCulture, AdjustedFormat, Format(Value), Unit) :
        string.Format(CultureInfo.CurrentCulture, RangeFormat, Format(Minimum), Format(Maximum), Unit, Format(Step)));

    private static string Format(double value) => value.ToString("0.########", CultureInfo.CurrentCulture);
    private static DependencyProperty Register(string name, double initial) =>
        DependencyProperty.Register(name, typeof(double), typeof(SettingsNumericEditor), new PropertyMetadata(initial, OnOptionsChanged));
    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SettingsNumericEditor)sender).Refresh();
    private static void OnOptionsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SettingsNumericEditor)sender).Refresh();
}
