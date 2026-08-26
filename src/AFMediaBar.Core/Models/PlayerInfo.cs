namespace AFMediaBar.Models;

public readonly record struct PlayerInfo
{
    public required string Identity { get; init; }
    public required string Title    { get; init; }
    public required string Artists  { get; init; }
    public required string Album    { get; init; }
    public required string Cover    { get; init; }
    public required double Schedule { get; init; }
    public required double Duration { get; init; }
    public required string Url      { get; init; }

    public required bool Pause { get; init; }

    public override int GetHashCode()
        => HashCode.Combine(Identity, Pause);
}
