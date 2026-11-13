using ArctisNovaTray.Models;

namespace ArctisNovaTray.Protocol;

public enum Nova7Generation
{
    Gen2DirectPercentAndBluetooth,
    Gen1DiscreteBattery
}

public sealed record Nova7ModelSpec(
    ushort ProductId,
    string DisplayName,
    Nova7Generation Generation);

public sealed record Nova7DecodedHidReport(
    ushort ProductId,
    string ProductName,
    bool HeadsetOnline24Ghz,
    int? BatteryPercent,
    BatteryChargingState Charging,
    BluetoothState? HidBluetoothState,
    int ChatMixGame,
    int ChatMixChat,
    bool? MicMuted,
    string RawHexSummary);

public static class Nova7HidProtocol
{
    public const ushort VendorId = 0x1038;
    public const ushort ControlUsagePage = 0xFFC0;
    public const ushort ControlUsage = 0x0001;
    public const int DefaultReportLength = 65;

    public const byte ReportId = 0x00;
    public const byte StatusOpcode = 0xB0;
    public const byte LinkConnectedValue = 0x03;

    private static readonly Dictionary<ushort, Nova7ModelSpec> ModelsByPid = new()
    {
        // Group 1: Gen 2 / 2026 Firmware Refresh PIDs (Direct 0-100% battery + HID Bluetooth state in data[6]/inBuf[7])
        [0x22A1] = new(0x22A1, "Arctis Nova 7", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x227E] = new(0x227E, "Arctis Nova 7 Wireless Gen 2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x22A7] = new(0x22A7, "Arctis Nova 7P Gen 2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x2298] = new(0x2298, "Arctis Nova 7P V2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x2258] = new(0x2258, "Arctis Nova 7X Gen 2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x229E] = new(0x229E, "Arctis Nova 7X Gen 2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x22AD] = new(0x22AD, "Arctis Nova 7X V2", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x22A5] = new(0x22A5, "Arctis Nova 7X", Nova7Generation.Gen2DirectPercentAndBluetooth),
        [0x22A9] = new(0x22A9, "Arctis Nova 7 Diablo IV", Nova7Generation.Gen2DirectPercentAndBluetooth),

        // Group 2: Original / Gen 1 Discrete PIDs (0-4 battery steps -> 0..100%; HID BT byte not advertised)
        [0x2202] = new(0x2202, "Arctis Nova 7", Nova7Generation.Gen1DiscreteBattery),
        [0x2206] = new(0x2206, "Arctis Nova 7X", Nova7Generation.Gen1DiscreteBattery),
        [0x22A4] = new(0x22A4, "Arctis Nova 7X", Nova7Generation.Gen1DiscreteBattery),
        [0x220A] = new(0x220A, "Arctis Nova 7P", Nova7Generation.Gen1DiscreteBattery),
        [0x223A] = new(0x223A, "Arctis Nova 7 Diablo IV", Nova7Generation.Gen1DiscreteBattery),
        [0x227A] = new(0x227A, "Arctis Nova 7 World of Warcraft Edition", Nova7Generation.Gen1DiscreteBattery)
    };

    public static IReadOnlySet<ushort> SupportedDongleProductIds { get; } =
        ModelsByPid.Keys.ToHashSet();

    public static bool TryGetModelSpec(ushort productId, out Nova7ModelSpec spec) =>
        ModelsByPid.TryGetValue(productId, out spec!);

    public static byte[] CreateStatusRequestReport(int outputReportLength = DefaultReportLength)
    {
        int len = Math.Max(2, outputReportLength);
        byte[] buffer = new byte[len];
        buffer[0] = ReportId;
        buffer[1] = StatusOpcode;
        return buffer;
    }

    public static bool TryParseStatusReport(
        ReadOnlySpan<byte> reportBuffer,
        ushort productId,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        if (reportBuffer.Length < 8)
        {
            return false;
        }

        // Win32 HID prepends 1-byte Report ID (0x00) at index 0, placing 0xB0 at index 1.
        // Also support raw hidapi buffers starting directly with 0xB0 at index 0.
        int offset;
        if (reportBuffer[0] == ReportId && reportBuffer[1] == StatusOpcode)
        {
            offset = 1;
        }
        else if (reportBuffer[0] == StatusOpcode)
        {
            offset = 0;
        }
        else
        {
            return false;
        }

        if (reportBuffer.Length < offset + 7)
        {
            return false;
        }

        Nova7ModelSpec spec = ModelsByPid.TryGetValue(productId, out Nova7ModelSpec? known)
            ? known
            : new Nova7ModelSpec(productId, "Arctis Nova 7", Nova7Generation.Gen2DirectPercentAndBluetooth);

        // data[1] (inBuf[2]): 0x03 = Headset powered ON & connected via 2.4GHz; 0x02 (or != 0x03) = Offline.
        byte linkStateByte = reportBuffer[offset + 1];
        bool headsetOnline = linkStateByte == LinkConnectedValue;

        byte rawBatteryByte = reportBuffer[offset + 2];  // data[2] / inBuf[3]
        byte rawChargingByte = reportBuffer[offset + 3]; // data[3] / inBuf[4]
        int chatMixGame = Math.Clamp((int)reportBuffer[offset + 4], 0, 100);
        int chatMixChat = Math.Clamp((int)reportBuffer[offset + 5], 0, 100);
        byte rawBtByte = reportBuffer[offset + 6];       // data[6] / inBuf[7]

        int? batteryPercent = null;
        BatteryChargingState charging = BatteryChargingState.Disconnected;
        BluetoothState? hidBtState = null;
        bool? micMuted = null;

        if (headsetOnline)
        {
            batteryPercent = spec.Generation == Nova7Generation.Gen2DirectPercentAndBluetooth
                ? Math.Clamp((int)rawBatteryByte, 0, 100)
                : Math.Clamp((int)rawBatteryByte * 25, 0, 100);

            charging = rawChargingByte switch
            {
                0x01 or 0x02 => BatteryChargingState.Charging,
                0x00 or 0x03 => BatteryChargingState.Discharging,
                _ => BatteryChargingState.Unknown
            };

            if (spec.Generation == Nova7Generation.Gen2DirectPercentAndBluetooth)
            {
                // data[6] / inBuf[7]: 0x00 = BT Off, 0x03 = BT ON (Standby), 0x02 = BT ON & Connected
                hidBtState = rawBtByte switch
                {
                    0x00 => BluetoothState.Off,
                    0x02 => BluetoothState.Connected,
                    0x03 => BluetoothState.Standby,
                    _ => BluetoothState.Standby
                };

                if (reportBuffer.Length >= offset + 10)
                {
                    micMuted = reportBuffer[offset + 9] == 0x01;
                }
            }
        }

        int hexLen = Math.Min(12, reportBuffer.Length);
        string rawHex = string.Join(' ', reportBuffer[..hexLen].ToArray().Select(b => b.ToString("X2")));

        decoded = new Nova7DecodedHidReport(
            productId,
            spec.DisplayName,
            headsetOnline,
            batteryPercent,
            charging,
            hidBtState,
            chatMixGame,
            chatMixChat,
            micMuted,
            rawHex);

        return true;
    }
}
