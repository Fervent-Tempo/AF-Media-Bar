// 过滤 Windows 的软件占位容器；共享占位值不能证明端点属于同一物理设备。
namespace AFMediaBar.Classes.Models;

/// <summary>音频与电量通道共用的容器身份校验。</summary>
public static class AudioDeviceIdentity
{
    private static readonly Guid _softwareContainer = new("00000000-0000-0000-ffff-ffffffffffff");
    public static Guid? PhysicalContainer(Guid? container) => container is { } id && id != Guid.Empty && id != _softwareContainer ? id : null;
}
