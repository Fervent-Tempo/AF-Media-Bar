// IP Helper 的只读流量接口。调用者必须在 finally 中使用 FreeMibTable 释放返回的表。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Interop;

public static partial class NativeMethods
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    public static extern uint GetIfTable2(out nint table);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    public static extern void FreeMibTable(nint table);

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MibIfRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    // 用结构对齐计算首行位置，不假设 NumEntries 后没有填充。
    [StructLayout(LayoutKind.Sequential)]
    public struct MibIfTable2
    {
        public uint NumEntries;
        public MibIfRow2 FirstRow;
    }
}
