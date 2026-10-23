// 控件独占前景、配色与材质画刷；卸载、宿主挂起和最终释放时结束所有颜色时钟。
using System.Windows.Media;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private readonly Dictionary<string, ColorTransitionBrush> _colorTransitions = [];
    private bool _adaptiveColorPresented;

    private Brush TransitionColor(string key, Brush target, TimeSpan duration, SolidColorBrush? ownedBrush = null, bool immediate = false)
    {
        if (!_colorTransitions.TryGetValue(key, out var transition) || ownedBrush is not null && !transition.Owns(ownedBrush))
        {
            transition?.Finish();
            _colorTransitions[key] = transition = new ColorTransitionBrush(ownedBrush);
        }
        if (immediate) transition.Finish();
        if (ownedBrush is not null && target is SolidColorBrush solid) return transition.Apply(solid.Color, duration);
        return transition.Apply(target, duration);
    }

    private void FinishColorTransitions()
    {
        _adaptiveColorPresented = false;
        foreach (var transition in _colorTransitions.Values) transition.Finish();
        _lyrics.FinishColorTransition();
    }
}
