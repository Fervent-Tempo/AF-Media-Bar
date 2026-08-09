namespace AFMediaBar.Models;

internal enum TaskbarAlignment
{
    Unknown = -1,
    Left = 0,
    Center = 1
}

internal readonly record struct TaskbarSettings(
    TaskbarAlignment Alignment,
    bool AutoHide);
