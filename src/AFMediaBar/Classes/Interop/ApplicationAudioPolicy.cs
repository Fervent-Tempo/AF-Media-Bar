// 已知 AudioPolicyConfig ABI；接口与 HSTRING 由音频线程上的单次操作释放。
// 布局参考 EarTrumpet 2.3.0.0 (MIT)，版权声明见 docs/third-party-audio-notices.md。
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Interop;

internal sealed class ApplicationAudioPolicy : IDisposable
{
    private nint _factory;
    private bool _uninitialize;
    private static readonly Guid _modernInterface = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid _legacyInterface = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    internal ApplicationAudioPolicy()
    {
        var initialized = RoInitialize(0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        _uninitialize = initialized >= 0;
        nint name = 0;
        try
        {
            const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
            Marshal.ThrowExceptionForHR(WindowsCreateString(ClassName, (uint)ClassName.Length, out name));
            var iid = _modernInterface;
            var result = RoGetActivationFactory(name, ref iid, out _factory);
            if (result < 0)
            {
                if (_factory != 0) { Marshal.Release(_factory); _factory = 0; }
                iid = _legacyInterface;
                result = RoGetActivationFactory(name, ref iid, out _factory);
            }
            Marshal.ThrowExceptionForHR(result);
        }
        catch { Dispose(); throw; }
        finally { if (name != 0) WindowsDeleteString(name); }
    }
    internal string? Get(int processId, AudioDeviceDirection direction, int role)
    {
        var abi = Marshal.PtrToStructure<PolicyAbi>(Marshal.ReadIntPtr(_factory));
        var get = Marshal.GetDelegateForFunctionPointer<GetEndpoint>(abi.GetPersistedDefaultAudioEndpoint);
        nint value = 0;
        try
        {
            EnsureAcknowledged(get(_factory, (uint)processId, (int)direction, role, out value));
            if (value == 0) return null;
            var id = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(value, out var length), checked((int)length));
            if (string.IsNullOrEmpty(id)) return null;
            const string Prefix = @"\\?\SWD#MMDEVAPI#";
            if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) id = id[Prefix.Length..];
            var suffix = id.LastIndexOf("#{", StringComparison.Ordinal);
            return suffix < 0 ? id : id[..suffix];
        }
        finally { if (value != 0) WindowsDeleteString(value); }
    }
    internal void Set(int processId, AudioDeviceDirection direction, int role, string? endpointId)
    {
        var abi = Marshal.PtrToStructure<PolicyAbi>(Marshal.ReadIntPtr(_factory));
        var set = Marshal.GetDelegateForFunctionPointer<SetEndpoint>(abi.SetPersistedDefaultAudioEndpoint);
        nint value = 0;
        try
        {
            if (endpointId is not null)
            {
                var suffix = direction == AudioDeviceDirection.Output ? "e6327cad-dcec-4949-ae8a-991e976a79d2" : "2eef81be-33fa-4800-9670-1cd474972c3f";
                var id = @"\\?\SWD#MMDEVAPI#" + endpointId + "#{" + suffix + "}";
                Marshal.ThrowExceptionForHR(WindowsCreateString(id, (uint)id.Length, out value));
            }
            // 空 HSTRING 只清除指定进程、方向和角色，绝不调用 ClearAll。
            EnsureAcknowledged(set(_factory, (uint)processId, (int)direction, role, value));
        }
        finally { if (value != 0) WindowsDeleteString(value); }
    }
    private static void EnsureAcknowledged(int result)
    {
        // 私有路由接口只将 S_OK 视为已确认；不要套用端点音量接口的 S_FALSE 语义。
        if (result != 0) throw new COMException("Windows audio policy did not acknowledge the operation.",
            result < 0 ? result : unchecked((int)0x80004005));
    }

    public void Dispose()
    {
        if (_factory != 0) { Marshal.Release(_factory); _factory = 0; }
        if (_uninitialize) { RoUninitialize(); _uninitialize = false; }
    }
    // 两个已知 IID 布局一致；保留 IInspectable 和全部未使用槽位，不猜测未知接口。
    [StructLayout(LayoutKind.Sequential)]
    private struct PolicyAbi
    {
        public nint QueryInterface, AddRef, Release, GetIids, GetRuntimeClassName, GetTrustLevel;
        public nint AddContextVolumeChanged, RemoveContextVolumeChanged, AddRingerVibrateChanged, RemoveRingerVibrateChanged;
        public nint SetVolumeGroupGain, GetVolumeGroupGain, GetActiveVolumeGroup, GetVolumeGroups, GetVolumeContext;
        public nint SetVolumeGroupMute, GetVolumeGroupMute, SetRingerVibrate, GetRingerVibrate;
        public nint SetPreferredChatApplication, ResetPreferredChatApplication, GetPreferredChatApplication, GetChatApplications;
        public nint AddChatContextChanged, RemoveChatContextChanged;
        public nint SetPersistedDefaultAudioEndpoint, GetPersistedDefaultAudioEndpoint;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetEndpoint(nint factory, uint processId, int direction, int role, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetEndpoint(nint factory, uint processId, int direction, int role, nint value);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern nint WindowsGetStringRawBuffer(nint value, out uint length);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
}
