using System.Globalization;
using System.Windows.Data;

namespace AFMediaBar.Classes.Converters
{
    /// <summary>
    /// 通用枚举到布尔值转换器：支持任意枚举类型与 RadioButton 的 IsChecked 绑定。
    /// Generic enum to boolean converter: supports binding any enum type to RadioButton IsChecked.
    ///
    /// 用法 Usage:
    /// <![CDATA[
    /// <RadioButton IsChecked="{Binding MyEnumProperty, Converter={StaticResource GenericEnumToBooleanConverter}, ConverterParameter=EnumValue}" />
    /// ]]>
    /// </summary>
    public class GenericEnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string enumString)
            {
                return false;
            }

            if (value == null)
            {
                return false;
            }

            // 获取枚举的实际类型
            // Get the actual enum type
            var enumType = value.GetType();

            if (!enumType.IsEnum)
            {
                return false;
            }

            // 尝试解析枚举值
            // Try to parse enum value
            try
            {
                var enumValue = Enum.Parse(enumType, enumString);
                return enumValue.Equals(value);
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string enumString)
            {
                throw new ArgumentException("Converter parameter must be an enum name string");
            }

            if (!targetType.IsEnum)
            {
                throw new ArgumentException("Target type must be an enum");
            }

            return Enum.Parse(targetType, enumString);
        }
    }
}
