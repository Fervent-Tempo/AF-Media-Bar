using AFMediaBar.Models;

namespace AFMediaBar.Abstractions;

public interface IMusicPlayer
{
    bool Validate(int pid);

    PlayerInfo? GetPlayerInfo();
}
