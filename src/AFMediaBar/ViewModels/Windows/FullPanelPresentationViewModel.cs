// 完整面板的尺寸输入写入共用配置；内容缩放由设置页独立控制。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>提交用户窗口尺寸，沿用设置服务的防抖和退出保存。</summary>
public sealed class FullPanelPresentationViewModel
{
    public void CommitSize(double widthDip, double heightDip)
    {
        if (!double.IsFinite(widthDip) || widthDip <= 0 || !double.IsFinite(heightDip) || heightDip <= 0) return;
        var current = SettingsManager.Current.TaskbarExperience;
        SettingsManager.Current.TaskbarExperience = current with
        {
            FullPanel = current.FullPanel with
            {
                WindowWidthDip = widthDip,
                WindowHeightDip = heightDip
            }
        };
    }
}
