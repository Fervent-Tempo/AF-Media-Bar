// 设置预览只呈现页面提供的示例投影。
using System.Windows.Controls;

namespace AFMediaBar.Components;

/// <summary>显示当前设置对应的固定示例，不拥有媒体资源。</summary>
public partial class SettingsPreview : UserControl
{
    public SettingsPreview() => InitializeComponent();
}
