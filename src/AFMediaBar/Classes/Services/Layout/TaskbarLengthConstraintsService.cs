namespace AFMediaBar.Classes.Services;

/// <summary>
/// 发布当前任务栏媒体条可用的运行时长度范围，供设置页展示而不持久化显示环境状态。
/// Publishes the taskbar media bar's current runtime length range for settings presentation
/// without persisting display-environment state.
/// </summary>
public sealed class TaskbarLengthConstraintsService
{
    private static readonly object LegacySource = new();
    private readonly Dictionary<object, (double Minimum, double Maximum)> _sources =
        new(ReferenceEqualityComparer.Instance);
    private double _minimumLengthDip = 120;
    private double _maximumLengthDip = 1200;

    /// <summary>当前悬停层和固定组件所需的最小长度。 / Current minimum required by the hover layer and fixed components.</summary>
    public double MinimumLengthDip => _minimumLengthDip;

    /// <summary>当前任务栏安全区间允许的最大长度。 / Current maximum allowed by the taskbar safe range.</summary>
    public double MaximumLengthDip => _maximumLengthDip;

    /// <summary>当前可显示宿主是否存在共同长度范围。</summary>
    public bool HasAvailableRange { get; private set; }

    /// <summary>运行时长度范围变化时触发。 / Raised when the runtime length range changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 更新可显示宿主的有效范围；非有限输入忽略，真实上限不因最小要求而抬高。
    /// Updates a feasible host range without inflating the actual maximum.
    /// </summary>
    public void Update(double minimumLengthDip, double maximumLengthDip)
        => Update(LegacySource, minimumLengthDip, maximumLengthDip);

    /// <summary>
    /// 更新一个任务栏宿主的长度范围；多显示器同时显示时对所有宿主取“最小值的最大值、最大值的最小值”。
    /// Updates one taskbar host's length range; when multiple taskbars are active the aggregate takes the largest minimum and the smallest maximum.
    /// </summary>
    /// <param name="source">宿主身份；关闭时传给 <see cref="Remove"/>。/ Host identity, later passed to <see cref="Remove"/> on close.</param>
    /// <param name="minimumLengthDip">该宿主所需最小长度。/ Minimum required by this host.</param>
    /// <param name="maximumLengthDip">该宿主可用最大长度。/ Maximum available to this host.</param>
    public void Update(object source, double minimumLengthDip, double maximumLengthDip)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(minimumLengthDip) || !double.IsFinite(maximumLengthDip))
            return;
        if (minimumLengthDip <= 0 || maximumLengthDip < minimumLengthDip)
            _sources.Remove(source);
        else
            _sources[source] = (minimumLengthDip, maximumLengthDip);
        PublishAggregate();
    }

    /// <summary>移除已关闭宿主的范围。/ Removes the range published by a closed host.</summary>
    public void Remove(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Remove(source))
            PublishAggregate();
    }

    private void PublishAggregate()
    {
        var minimum = _sources.Count == 0 ? 120 : _sources.Values.Max(value => value.Minimum);
        var maximum = _sources.Count == 0 ? 1200 : _sources.Values.Min(value => value.Maximum);
        var available = _sources.Count > 0 && maximum >= minimum;
        if (Math.Abs(_minimumLengthDip - minimum) < 0.1 &&
            Math.Abs(_maximumLengthDip - maximum) < 0.1 && HasAvailableRange == available)
            return;

        HasAvailableRange = available;
        _minimumLengthDip = minimum;
        _maximumLengthDip = maximum;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
