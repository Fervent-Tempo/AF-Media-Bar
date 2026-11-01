// 原生 HID 目录与可取消传输；会话独占句柄，挂起 I/O 完成后才释放 OVERLAPPED 和缓冲区。
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AFMediaBar.Classes.Interop;

/// <summary>HID 集合描述，只包含元数据，不持有句柄。</summary>
public sealed record BatteryHidEndpoint(string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    int InputLength, int OutputLength, int FeatureLength, string Name, string Serial, Guid? ContainerId, bool IsBluetooth)
{
    public string PhysicalId => !string.IsNullOrWhiteSpace(Serial) ? $"{VendorId:X4}:{ProductId:X4}:{Serial}" : ContainerId?.ToString("D") ?? Path.ToUpperInvariant();
}

/// <summary>后台使用的共享 HID 句柄；调用方串行访问，Dispose 前结束所有传输。</summary>
public sealed class BatteryHidTransport : IDisposable
{
    private const uint GetFeatureCode = 0x000B0192;
    private const uint SetFeatureCode = 0x000B0191;
    private const uint SetOutputCode = 0x000B0195;
    private readonly SafeFileHandle _handle;
    private readonly BatteryHidEndpoint _endpoint;
    public BatteryHidTransport(BatteryHidEndpoint endpoint, uint access = 0xC0000000)
    {
        _endpoint = endpoint;
        _handle = CreateFile(endpoint.Path, access, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (_handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error); }
    }
    public Task<byte[]?> ReadAsync(int milliseconds, CancellationToken token) => TransferAsync(new byte[_endpoint.InputLength], 0, milliseconds, token);
    public async Task WriteAsync(byte[] report, bool control, CancellationToken token) =>
        _ = await TransferAsync(report, control ? SetOutputCode : 1, 700, token).ConfigureAwait(false) ?? throw new TimeoutException("HID 写入超时。");
    public async Task SetFeatureAsync(byte[] report, CancellationToken token) =>
        _ = await TransferAsync(report, SetFeatureCode, 700, token).ConfigureAwait(false) ?? throw new TimeoutException("HID Feature 写入超时。");
    public Task<byte[]?> GetFeatureAsync(byte reportId, int length, CancellationToken token)
    {
        var report = new byte[length]; report[0] = reportId;
        return TransferAsync(report, GetFeatureCode, 700, token);
    }
    private Task<byte[]?> TransferAsync(byte[] report, uint operation, int milliseconds, CancellationToken token) =>
        Task.Run(() => Transfer(report, operation, milliseconds, token), token);
    private byte[]? Transfer(byte[] report, uint operation, int milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (report.Length is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(report));
        using var completed = new ManualResetEvent(false);
        var buffer = Marshal.AllocHGlobal(report.Length);
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<OverlappedData>());
        try
        {
            Marshal.Copy(report, 0, buffer, report.Length);
            Marshal.StructureToPtr(new OverlappedData { Event = completed.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            uint transferred;
            var ok = operation == 0 ? ReadFile(_handle, buffer, (uint)report.Length, out transferred, overlapped)
                : operation == 1 ? WriteFile(_handle, buffer, (uint)report.Length, out transferred, overlapped)
                : DeviceIoControl(_handle, operation, buffer, (uint)report.Length, buffer, (uint)report.Length, out transferred, overlapped);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 997) throw new Win32Exception(error);
                var signaled = WaitHandle.WaitAny([completed, token.WaitHandle], milliseconds);
                if (signaled != 0)
                {
                    CancelIoEx(_handle, overlapped);
                    // CancelIoEx 只是请求取消；驱动完成前不能释放或复用内存。
                    GetOverlappedResult(_handle, overlapped, out _, true);
                    token.ThrowIfCancellationRequested();
                    return null;
                }
                if (!GetOverlappedResult(_handle, overlapped, out transferred, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            token.ThrowIfCancellationRequested();
            if (operation is 1 or SetFeatureCode or SetOutputCode) return report;
            // Feature IOCTL 的部分驱动不返回长度；其报告按描述符长度分配。
            var length = operation == GetFeatureCode && transferred == 0 ? report.Length : Math.Min((int)transferred, report.Length);
            if (length <= 0) return null;
            var result = new byte[length]; Marshal.Copy(buffer, result, 0, length); return result;
        }
        finally { Marshal.FreeHGlobal(overlapped); Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() => _handle.Dispose();

    public static IReadOnlyList<BatteryHidEndpoint> Enumerate(CancellationToken token)
    {
        var result = new List<BatteryHidEndpoint>();
        var guid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;
                var info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, ref info);
                if (size is < 8 or > 65536) continue;
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, ref info)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (string.IsNullOrEmpty(path)) continue;
                    using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || !HidD_GetPreparsedData(handle, out var preparsed)) continue;
                    Caps caps;
                    try { if (HidP_GetCaps(preparsed, out caps) != 0x110000) continue; }
                    finally { HidD_FreePreparsedData(preparsed); }
                    var name = new StringBuilder(256); HidD_GetProductString(handle, name, 512);
                    var serial = new StringBuilder(256); HidD_GetSerialNumberString(handle, serial, 512);
                    var key = new PropertyKey { Format = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
                    var buffer = new byte[16];
                    Guid? container = null;
                    if (SetupDiGetDeviceProperty(set, ref info, ref key, out var type, buffer, 16, out var count, 0) && type == 13 && count == 16)
                    { var id = new Guid(buffer); if (id != Guid.Empty) container = id; }
                    var instance = new StringBuilder(512);
                    SetupDiGetDeviceInstanceId(set, ref info, instance, instance.Capacity, out _);
                    var isBluetooth = path.Contains("BTH", StringComparison.OrdinalIgnoreCase) || instance.ToString().Contains("BTH", StringComparison.OrdinalIgnoreCase);
                    result.Add(new(path, attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage,
                        caps.InputLength, caps.OutputLength, caps.FeatureLength, name.ToString(), serial.ToString(), container, isBluetooth));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct OverlappedData { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid Class; public uint Instance; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size; public ushort VendorId, ProductId, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint size, out uint count, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint size, out uint count, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint count, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out uint count, [MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder value, int size);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, StringBuilder value, int size);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, ref DeviceInfo info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] value, int size, out uint required, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder value, int size, out uint required);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
