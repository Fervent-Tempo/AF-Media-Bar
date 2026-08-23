namespace AFMediaBar.Models;

public readonly record struct OccupiedRange(int Left, int Right)
{
    public int Width => Right - Left;
}
