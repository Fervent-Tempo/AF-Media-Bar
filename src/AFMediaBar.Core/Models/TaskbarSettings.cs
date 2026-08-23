namespace AFMediaBar.Models;

public enum TaskbarAlignment
{
    Unknown = -1,
    Left = 0,
    Center = 1
}

public readonly record struct TaskbarSettings(
    TaskbarAlignment Alignment,
    bool AutoHide);
