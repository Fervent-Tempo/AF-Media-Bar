// 设置列表与示例预览共用组件图标，不引用编辑器或运行服务。
using System.Globalization;
using System.Windows.Data;
using AFMediaBar.Classes.Settings;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components;

/// <summary>将稳定组件标识映射到实际使用的 Fluent 图标。</summary>
public sealed class ComponentSymbolConverter : IValueConverter
{
    public static SymbolRegular Resolve(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.Artwork => SymbolRegular.Image24,
        TaskbarRestComponent.MediaText => SymbolRegular.TextQuote24,
        TaskbarRestComponent.Spectrum => SymbolRegular.DataBarVertical24,
        TaskbarRestComponent.OutputDevice => SymbolRegular.Speaker224,
        TaskbarRestComponent.InputDevice => SymbolRegular.Mic24,
        TaskbarRestComponent.Volume => SymbolRegular.Speaker124,
        TaskbarRestComponent.BluetoothBattery => SymbolRegular.Bluetooth24,
        TaskbarRestComponent.PreviousTrack => SymbolRegular.Previous24,
        TaskbarRestComponent.PlayPause => SymbolRegular.Play24,
        TaskbarRestComponent.NextTrack => SymbolRegular.Next24,
        _ => SymbolRegular.Gauge24
    };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is TaskbarRestComponent component ? Resolve(component) : SymbolRegular.Layer24;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
