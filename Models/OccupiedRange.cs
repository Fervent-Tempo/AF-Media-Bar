namespace AFMediaBar.Models;

internal readonly record struct OccupiedRange(int Left, int Right)
{
    internal int Width => Right - Left;
}
