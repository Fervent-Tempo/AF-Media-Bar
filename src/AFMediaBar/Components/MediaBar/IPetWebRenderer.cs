// 呈现层浏览器边界；每个 PetView 拥有一个实例，测试替换浏览器而不替换生命周期逻辑。
using System.Windows;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

internal sealed record PetWebState(
    double GazeX, double GazeY, bool Hovered, long BlinkSequence, long PressSequence,
    string BodyColor, string EyeColor, bool HighContrast, bool IdleAnimationsEnabled, MotionMode Motion, bool Active,
    string Animation = "auto", double Beat = 0, string Outfit = "original", string Accent = "#0078D4");

internal interface IPetWebRenderer : IDisposable
{
    FrameworkElement View { get; }
    event EventHandler? Ready;
    event EventHandler? Failed;
    Task InitializeAsync();
    void Present(PetWebState state);
    void SetActive(bool active);
}
