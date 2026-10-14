// 进程与应用身份的纯比较规则，不读取进程或持有句柄。
using AFMediaBar.Classes.Models;
namespace AFMediaBar.Classes.Services.Audio;

/// <summary>区分同名程序和 PID 复用，包身份用于合并同一应用的会话。</summary>
public static class AudioApplicationIdentityPolicy
{
    public static string GetKey(AudioProcessIdentity process) =>
        (process.PackageIdentity is { Length: > 0 } package ? "package:" + package : "exe:" + process.ExecutablePath).ToUpperInvariant();
    public static bool IsSameProcess(AudioProcessIdentity expected, AudioProcessIdentity? current) =>
        current is not null && expected.ProcessId == current.ProcessId && expected.StartedAtUtcTicks == current.StartedAtUtcTicks &&
        string.Equals(expected.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PackageIdentity, current.PackageIdentity, StringComparison.OrdinalIgnoreCase);
}
