// 隐藏宿主持有任务栏方向观察计时器；仅检查 HWND 几何，关闭时停止，不执行 UIA 探测。
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Views.Windows;

public partial class MainWindow
{
    private string? _experimentalHostSignature;
    private readonly DispatcherTimer _experimentalHostWatcher = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private void InitializeExperimentalHostWatcher()
    {
        _experimentalHostWatcher.Tick += ExperimentalHostWatcher_Tick;
        Loaded += (_, _) =>
        {
            _experimentalHostSignature = GetExperimentalHostSignature();
            _experimentalHostWatcher.Start();
        };
        Closed += (_, _) =>
        {
            _experimentalHostWatcher.Stop();
            _experimentalHostWatcher.Tick -= ExperimentalHostWatcher_Tick;
        };
    }

    private string GetExperimentalHostSignature() => string.Join("|", ResolveTaskbarTargetDeviceIds().Select(target =>
        {
            var handle = _taskBarService.GetSelectedTaskbarHandle(target, out _);
            return $"{handle}:{_taskBarService.IsTaskbarVertical(handle)}";
        }));

    private void ExperimentalHostWatcher_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || TaskbarEnvironmentRecovering || SettingsManager.Current.WindowMode != WindowMode.Taskbar) return;
        var signature = GetExperimentalHostSignature();
        if (_experimentalHostSignature == signature) return;
        _experimentalHostSignature = signature;
        RecreateTaskbarWindows();
    }

}
