// 设置页只展示本地化动画名称，保存稳定标识而不保存显示文字。
namespace AFMediaBar.ViewModels.Pages;

/// <summary>一个可选动画及当前语言下的名称。</summary>
public sealed record PetAnimationOption(string Id, string DisplayName);
