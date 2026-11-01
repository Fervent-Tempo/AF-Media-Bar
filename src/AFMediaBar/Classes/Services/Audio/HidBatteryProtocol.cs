// 仅处理已知电量查询帧，不包含设备配置写入；报文依据及 MIT 声明见 docs/device-battery.md。
namespace AFMediaBar.Classes.Services.Audio;

/// <summary>首批已知 HID 电量协议。</summary>
internal enum HidBatteryKind { Logitech, Mchose, Rog, RazerMouse, RazerHeadset, AtkMouse, AtkHeadset, Inphic9, Inphic10 }

/// <summary>解析后的硬件读数；未知充电状态不推断成未充电。</summary>
internal sealed record HidBatteryValue(int Percent, bool? Charging);

/// <summary>无平台依赖的报文构造与校验，便于回放真实协议样本。</summary>
internal static class HidBatteryProtocol
{
    internal static byte[] Query(HidBatteryKind kind, byte sequence = 1, byte field = 0x21)
    {
        byte[] frame;
        switch (kind)
        {
            case HidBatteryKind.Mchose:
                frame = new byte[20]; frame[0] = 0x13; frame[1] = 0x4A; frame[2] = 1; frame[19] = Sum(frame.AsSpan(0, 19)); return frame;
            case HidBatteryKind.Rog:
                frame = new byte[64]; frame[0] = 2; frame[1] = 0x12; frame[2] = 1; return frame;
            case HidBatteryKind.RazerMouse:
                frame = new byte[91]; frame[2] = sequence; frame[6] = 2; frame[7] = 7; frame[8] = field;
                frame[89] = Xor(frame.AsSpan(3, 86)); return frame;
            case HidBatteryKind.RazerHeadset:
                frame = new byte[64]; frame[0] = 2; frame[1] = 0x80; frame[2] = 9; frame[5] = 0x50;
                frame[6] = 0x41; frame[7] = 8; frame[9] = 4; frame[10] = field; frame[12] = 1; frame[13] = 1; return frame;
            case HidBatteryKind.AtkMouse:
                frame = new byte[17]; frame[0] = 8; frame[1] = 4; frame[16] = 0x49; return frame;
            case HidBatteryKind.AtkHeadset:
                frame = new byte[64]; frame[0] = 0x55; frame[1] = 0x65; frame[2] = 1; return frame;
            case HidBatteryKind.Inphic9:
                frame = new byte[33]; frame[0] = 0x0C; frame[1] = 1; frame[2] = field; frame[4] = sequence;
                if (field == 0xF0) { frame[5] = 1; frame[6] = 1; frame[7] = 0xFF; } return frame;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
    internal static HidBatteryValue? Parse(HidBatteryKind kind, ReadOnlySpan<byte> frame, byte sequence = 1, byte field = 0x21)
    {
        int percent;
        bool? charging;
        switch (kind)
        {
            case HidBatteryKind.Mchose:
                if (frame.Length != 20 || frame[0] != 0x13 || frame[1] != 0x4A || frame[2] != 1 || frame[3] != 0 || frame[4] != 2 || frame[19] != Sum(frame[..19])) return null;
                percent = frame[5]; charging = frame[6] switch { 0x10 => true, 1 => false, _ => null }; break;
            case HidBatteryKind.Rog:
                if (frame.Length != 64 || frame[0] != 2 || frame[1] != 0x12 || frame[2] != 1) return null;
                percent = frame[6]; charging = null; break;
            case HidBatteryKind.RazerMouse:
                if (frame.Length != 91 || frame[0] != 0 || frame[1] != 2 || frame[2] != sequence || frame[6] < 2 || frame[7] != 7 || frame[8] != field || frame[89] != Xor(frame.Slice(3, 86))) return null;
                percent = field == 0x80 ? RazerPercent(frame[10]) : frame[10]; charging = null; break;
            case HidBatteryKind.RazerHeadset:
                if (frame.Length != 64 || frame[0] != 2 || frame[2] != 0x50 || frame[3] != 0x49 || frame[12] != field || frame[13] != 1 || frame[14] != 1) return null;
                percent = frame[15]; charging = null; break;
            case HidBatteryKind.AtkMouse:
                if (frame.Length != 17 || frame[0] != 8 || frame[1] != 4 || Sum(frame[1..]) != 0x4D) return null;
                percent = frame[6]; charging = frame[7] switch { 0 => false, 1 => true, _ => null }; break;
            case HidBatteryKind.AtkHeadset:
                if (frame.Length != 64 || frame[0] != 0x55 || frame[1] != 0x65 || frame[3] is not (2 or 3 or 4)) return null;
                percent = frame[2]; charging = frame[3] == 3; break;
            case HidBatteryKind.Inphic9:
                if (frame.Length != 33 || frame[0] != 0x0C || frame[1] != 1 || frame[2] != 0x20 || frame[4] != sequence || frame[6] == 0 || frame[0x15] == 0) return null;
                percent = frame[0x12]; charging = frame[0x13] == 2; break;
            case HidBatteryKind.Inphic10:
                if (frame.Length != 5 || frame[0] != 3 || frame[1] is not (0x95 or 0x90 or 0x93 or 0x99 or 0x98 or 0x73) || frame[2] != 0x40 || frame[3] != sequence) return null;
                percent = frame[4]; charging = null; break;
            default: return null;
        }
        return percent is >= 0 and <= 100 ? new(percent, charging) : null;
    }
    internal static int RazerPercent(byte raw) => (int)Math.Round(raw * 100d / 255d);
    internal static byte Sum(ReadOnlySpan<byte> bytes) { var sum = 0; foreach (var value in bytes) sum += value; return unchecked((byte)sum); }
    private static byte Xor(ReadOnlySpan<byte> bytes) { byte sum = 0; foreach (var value in bytes) sum ^= value; return sum; }
    internal static bool MatchesLogitech(ReadOnlySpan<byte> frame, byte slot, byte feature, byte function) =>
        frame.Length == 20 && frame[0] == 0x11 && frame[1] == slot && frame[2] == feature && frame[3] == function;
    internal static HidBatteryValue? ParseLogitech(ReadOnlySpan<byte> frame, byte slot, byte feature, bool unified)
    {
        if (!MatchesLogitech(frame, slot, feature, unified ? (byte)0x1E : (byte)0x0E) || frame[4] > 100) return null;
        var status = frame[6];
        return new(frame[4], status switch { 0 or 3 => false, 1 or 2 or 4 => true, _ => null });
    }
}
