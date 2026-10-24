// 完整面板的尺寸输入写入共用配置；窗口仅负责测量和鼠标捕获。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>提交用户缩放偏好，沿用设置服务的防抖和退出保存。</summary>
public sealed class FullPanelPresentationViewModel
{
    public void CommitScale(int percent)
    {
        var current = SettingsManager.Current.TaskbarExperience;
        SettingsManager.Current.TaskbarExperience = current with
        {
            FullPanel = current.FullPanel with
            {
                ScalePercent = Math.Clamp(percent, TaskbarFullPanelSettings.MinimumScalePercent, TaskbarFullPanelSettings.MaximumScalePercent)
            }
        };
    }
}
