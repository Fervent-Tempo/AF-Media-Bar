// 设置页只发出语义导航意图；实际页面作用域、跳转和滚动由设置窗口协调。
using System.Windows;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>跨设置页的相关配置入口，不直接创建页面或依赖服务容器。</summary>
public sealed class SettingsNavigationButton : Wpf.Ui.Controls.Button
{
    public SettingsNavigationButton() => SetResourceReference(StyleProperty, "AfSettingsNavigationButtonStyle");

    public static readonly DependencyProperty DestinationProperty = DependencyProperty.Register(
        nameof(Destination), typeof(SettingsPageKey), typeof(SettingsNavigationButton), new PropertyMetadata(SettingsPageKey.Interaction));
    public static readonly DependencyProperty GroupIdProperty = DependencyProperty.Register(
        nameof(GroupId), typeof(string), typeof(SettingsNavigationButton), new PropertyMetadata(string.Empty));
    public static readonly RoutedEvent NavigateEvent = EventManager.RegisterRoutedEvent(
        nameof(Navigate), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SettingsNavigationButton));
    public SettingsPageKey Destination { get => (SettingsPageKey)GetValue(DestinationProperty); set => SetValue(DestinationProperty, value); }
    public string GroupId { get => (string)GetValue(GroupIdProperty); set => SetValue(GroupIdProperty, value); }
    public event RoutedEventHandler Navigate { add => AddHandler(NavigateEvent, value); remove => RemoveHandler(NavigateEvent, value); }
    protected override void OnClick()
    {
        base.OnClick();
        RaiseEvent(new SettingsNavigationEventArgs(Destination, GroupId));
    }
}

/// <summary>保留稳定页面键与分组键的跳转请求，不携带窗口或设置对象。</summary>
public sealed class SettingsNavigationEventArgs(SettingsPageKey destination, string groupId) : RoutedEventArgs(SettingsNavigationButton.NavigateEvent)
{
    public SettingsPageKey Destination { get; } = destination;
    public string GroupId { get; } = groupId;
}
