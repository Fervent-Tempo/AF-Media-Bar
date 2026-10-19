using System.ComponentModel;
using System.Windows.Input;

namespace AFMediaBar.Components;

/// <summary>
/// 分组标签条上的一个分组标签。名称直接来自页面里的分组标题，因此跳转目标与可见标题不会各说各话。
/// One tab in the group strip. Its name comes straight from the group header in the page, so the jump target
/// and the visible header cannot disagree.
/// </summary>
public sealed class SettingsGroupItem : INotifyPropertyChanged
{
    private bool _isActive;

    /// <summary>创建分组标签。/ Creates a group tab.</summary>
    /// <param name="index">分组在页面滚动内容中的序号。/ Group index within the page's scroll content.</param>
    /// <param name="name">分组标题，同时用作标签文案。/ Group header, also used as the tab label.</param>
    /// <param name="jumpCommand">点击标签时执行的跳转命令。/ Jump command executed when the tab is clicked.</param>
    public SettingsGroupItem(int index, string name, ICommand jumpCommand)
    {
        Index = index;
        Name = name;
        JumpCommand = jumpCommand;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>分组序号，与页面滚动内容里分组容器的声明顺序一致。/ Group index, matching declaration order in the page's scroll content.</summary>
    public int Index { get; }

    /// <summary>分组标题，也是标签文案。/ Group header, and the tab label.</summary>
    public string Name { get; }

    /// <summary>点击标签时的跳转命令。/ Jump command for this tab.</summary>
    public ICommand JumpCommand { get; }

    /// <summary>是否当前激活；同一时刻只有一个标签为真。/ Whether this tab is active; exactly one tab is active at a time.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}
