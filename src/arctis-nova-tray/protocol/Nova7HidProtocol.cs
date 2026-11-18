using System.Globalization;
using ArctisNovaTray.Models;

namespace ArctisNovaTray.Protocol;

public enum Nova7Generation
{
    Gen2DirectPercentAndBluetooth,
    Gen1DiscreteBattery
}

public enum NovaProtocolFamily
{
    Nova7Gen2DirectPercentAndBluetooth,
    Nova7Gen1DiscreteBattery,
    Nova5DirectPercent,
    Nova3PWirelessDirectPercent,
    GameBudsDualEarbud,
    NovaProOmniGameHub
}

public enum ModelVerificationTier
{
    HardwareVerified,
    CommunitySpec
}

public sealed record HeadsetProtocolProfile(
    ushort ProductId,
    string DisplayName,
    string ShortLabel,
    NovaProtocolFamily ProtocolFamily,
    ModelVerificationTier VerificationTier,
    ushort ControlUsagePage = Nova7HidProtocol.ControlUsagePage,
    ushort? ControlUsage = Nova7HidProtocol.ControlUsage,
    byte ReportId = Nova7HidProtocol.ReportId,
    byte StatusOpcode = Nova7HidProtocol.StatusOpcode,
    int DefaultReportLength = Nova7HidProtocol.DefaultReportLength)
{
    public string VerificationTierLabel =>
        VerificationTier == ModelVerificationTier.HardwareVerified
            ? "Hardware-Verified"
            : "Community-Spec";
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

    private static readonly Dictionary<ushort, HeadsetProtocolProfile> ProfilesMap = new()
    {
        // 1. Nova 7 Gen 2 / 2026 Firmware Refresh (9 PIDs: Direct 0-100% battery + HID Bluetooth state + Mic Mute)
        [0x22A1] = new(0x22A1, "Arctis Nova 7", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.HardwareVerified),
        [0x227E] = new(0x227E, "Arctis Nova 7 Wireless Gen 2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x22A7] = new(0x22A7, "Arctis Nova 7P Gen 2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x2298] = new(0x2298, "Arctis Nova 7P V2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x2258] = new(0x2258, "Arctis Nova 7X Gen 2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x229E] = new(0x229E, "Arctis Nova 7X Gen 2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x22AD] = new(0x22AD, "Arctis Nova 7X V2", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x22A5] = new(0x22A5, "Arctis Nova 7X", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),
        [0x22A9] = new(0x22A9, "Arctis Nova 7 Diablo IV", "Nova 7", NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth, ModelVerificationTier.CommunitySpec),

        // 2. Nova 7 Original / Gen 1 Discrete Battery (6 PIDs: 0-4 battery steps -> 0..100%)
        [0x2202] = new(0x2202, "Arctis Nova 7", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),
        [0x2206] = new(0x2206, "Arctis Nova 7X", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),
        [0x22A4] = new(0x22A4, "Arctis Nova 7X", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),
        [0x220A] = new(0x220A, "Arctis Nova 7P", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),
        [0x223A] = new(0x223A, "Arctis Nova 7 Diablo IV", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),
        [0x227A] = new(0x227A, "Arctis Nova 7 World of Warcraft Edition", "Nova 7", NovaProtocolFamily.Nova7Gen1DiscreteBattery, ModelVerificationTier.CommunitySpec),

        // 3. Nova 5 / 5X Wireless (3 PIDs: Sapd/HeadsetControl steelseries_arctis_nova_5.hpp + #518)
        [0x2232] = new(0x2232, "Arctis Nova 5", "Nova 5", NovaProtocolFamily.Nova5DirectPercent, ModelVerificationTier.CommunitySpec),
        [0x2253] = new(0x2253, "Arctis Nova 5X", "Nova 5X", NovaProtocolFamily.Nova5DirectPercent, ModelVerificationTier.CommunitySpec),
        [0x2264] = new(0x2264, "Arctis Nova 5X", "Nova 5X", NovaProtocolFamily.Nova5DirectPercent, ModelVerificationTier.CommunitySpec),

        // 4. Nova 3P / 3X Wireless (2 PIDs: Sapd/HeadsetControl steelseries_arctis_nova_3p_wireless.hpp)
        [0x2269] = new(0x2269, "Arctis Nova 3P Wireless", "Nova 3P", NovaProtocolFamily.Nova3PWirelessDirectPercent, ModelVerificationTier.CommunitySpec),
        [0x226D] = new(0x226D, "Arctis Nova 3X Wireless", "Nova 3X", NovaProtocolFamily.Nova3PWirelessDirectPercent, ModelVerificationTier.CommunitySpec),

        // 5. Arctis GameBuds Dual-Earbud Wireless (1 PID: Sapd/HeadsetControl steelseries_arctis_gamebuds.hpp)
        [0x230A] = new(0x230A, "Arctis GameBuds", "GameBuds", NovaProtocolFamily.GameBudsDualEarbud, ModelVerificationTier.CommunitySpec),

        // 6. Arctis Nova Pro Omni GameHub Base Station (1 PID: ReportId = 0x01, 64B Output/Input; never touch Feature reports)
        [0x2290] = new(
            0x2290,
            "Arctis Nova Pro Omni",
            "Nova Pro",
            NovaProtocolFamily.NovaProOmniGameHub,
            ModelVerificationTier.CommunitySpec,
            ControlUsagePage: ControlUsagePage,
            ControlUsage: null,
            ReportId: 0x01,
            StatusOpcode: StatusOpcode,
            DefaultReportLength: 64)
    };

    public static IReadOnlyDictionary<ushort, HeadsetProtocolProfile> ProfilesByPid => ProfilesMap;

    public static IReadOnlySet<ushort> SupportedDongleProductIds { get; } =
        ProfilesMap.Keys.ToHashSet();

    public static bool TryGetProfile(ushort productId, out HeadsetProtocolProfile profile) =>
        ProfilesMap.TryGetValue(productId, out profile!);

    public static HeadsetProtocolProfile GetProfileOrDefault(ushort productId) =>
        ProfilesMap.TryGetValue(productId, out HeadsetProtocolProfile? profile)
            ? profile
            : new HeadsetProtocolProfile(
                productId,
                "Arctis Nova",
                "Arctis Nova",
                NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth,
                ModelVerificationTier.CommunitySpec);

    public static bool TryGetProfileByVidPid(string? dongleVidPid, out HeadsetProtocolProfile profile)
    {
        profile = null!;
        if (string.IsNullOrWhiteSpace(dongleVidPid))
        {
            return false;
        }

        int colonIdx = dongleVidPid.IndexOf(':');
        ReadOnlySpan<char> pidSpan = colonIdx >= 0
            ? dongleVidPid.AsSpan(colonIdx + 1).Trim()
            : dongleVidPid.AsSpan().Trim();

        if (pidSpan.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            pidSpan = pidSpan[2..];
        }

        return ushort.TryParse(pidSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort pid) &&
               ProfilesMap.TryGetValue(pid, out profile!);
    }

    public static bool TryGetModelSpec(ushort productId, out Nova7ModelSpec spec)
    {
        if (ProfilesMap.TryGetValue(productId, out HeadsetProtocolProfile? profile))
        {
            Nova7Generation gen = profile.ProtocolFamily == NovaProtocolFamily.Nova7Gen1DiscreteBattery
                ? Nova7Generation.Gen1DiscreteBattery
                : Nova7Generation.Gen2DirectPercentAndBluetooth;
            spec = new Nova7ModelSpec(profile.ProductId, profile.DisplayName, gen);
            return true;
        }

        spec = null!;
        return false;
    }

    public static byte[] CreateStatusRequestReport(int outputReportLength = DefaultReportLength)
    {
        int len = Math.Max(2, outputReportLength);
        byte[] buffer = new byte[len];
        buffer[0] = ReportId;
        buffer[1] = StatusOpcode;
        return buffer;
    }

    public static byte[] CreateStatusRequestReport(HeadsetProtocolProfile profile, int outputReportLength = 0)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int len = outputReportLength > 0
            ? Math.Max(2, outputReportLength)
            : Math.Max(2, profile.DefaultReportLength);
        byte[] buffer = new byte[len];
        buffer[0] = profile.ReportId;
        buffer[1] = profile.StatusOpcode;
        return buffer;
    }

    public static bool TryParseStatusReport(
        ReadOnlySpan<byte> reportBuffer,
        ushort productId,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        HeadsetProtocolProfile profile = GetProfileOrDefault(productId);

        if (profile.ProtocolFamily == NovaProtocolFamily.NovaProOmniGameHub)
        {
            return TryParseNovaProOmniReport(reportBuffer, profile, out decoded);
        }

        if (reportBuffer.Length < 5)
        {
            return false;
        }

        // Win32 HID prepends 1-byte Report ID (0x00) at index 0, placing 0xB0 at index 1.
        // Also support raw hidapi buffers starting directly with 0xB0 at index 0.
        int offset;
        if (reportBuffer[0] == profile.ReportId && reportBuffer[1] == profile.StatusOpcode)
        {
            offset = 1;
        }
        else if (reportBuffer[0] == profile.StatusOpcode)
        {
            offset = 0;
        }
        else
        {
            return false;
        }

        int payloadLength = reportBuffer.Length - offset;

        return profile.ProtocolFamily switch
        {
            NovaProtocolFamily.Nova7Gen2DirectPercentAndBluetooth =>
                TryParseNova7Report(reportBuffer, offset, payloadLength, profile, directPercentAndBt: true, out decoded),
            NovaProtocolFamily.Nova7Gen1DiscreteBattery =>
                TryParseNova7Report(reportBuffer, offset, payloadLength, profile, directPercentAndBt: false, out decoded),
            NovaProtocolFamily.Nova5DirectPercent =>
                TryParseNova5Or3PReport(reportBuffer, offset, payloadLength, profile, includeChatMix: true, out decoded),
            NovaProtocolFamily.Nova3PWirelessDirectPercent =>
                TryParseNova5Or3PReport(reportBuffer, offset, payloadLength, profile, includeChatMix: false, out decoded),
            NovaProtocolFamily.GameBudsDualEarbud =>
                TryParseGameBudsReport(reportBuffer, offset, payloadLength, profile, out decoded),
            _ => false
        };
    }

    private static bool TryParseNova7Report(
        ReadOnlySpan<byte> reportBuffer,
        int offset,
        int payloadLength,
        HeadsetProtocolProfile profile,
        bool directPercentAndBt,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        if (payloadLength < 7)
        {
            return false;
        }

        bool headsetOnline = reportBuffer[offset + 1] == LinkConnectedValue;
        byte rawBatteryByte = reportBuffer[offset + 2];
        byte rawChargingByte = reportBuffer[offset + 3];
        int chatMixGame = Math.Clamp((int)reportBuffer[offset + 4], 0, 100);
        int chatMixChat = Math.Clamp((int)reportBuffer[offset + 5], 0, 100);
        byte rawBtByte = reportBuffer[offset + 6];

        int? batteryPercent = null;
        BatteryChargingState charging = BatteryChargingState.Disconnected;
        BluetoothState? hidBtState = null;
        bool? micMuted = null;

        if (headsetOnline)
        {
            batteryPercent = directPercentAndBt
                ? Math.Clamp((int)rawBatteryByte, 0, 100)
                : Math.Clamp((int)rawBatteryByte * 25, 0, 100);

            charging = rawChargingByte switch
            {
                0x01 or 0x02 => BatteryChargingState.Charging,
                0x00 or 0x03 => BatteryChargingState.Discharging,
                _ => BatteryChargingState.Unknown
            };

            if (directPercentAndBt)
            {
                hidBtState = rawBtByte switch
                {
                    0x00 => BluetoothState.Off,
                    0x02 => BluetoothState.Connected,
                    0x03 => BluetoothState.Standby,
                    _ => BluetoothState.Standby
                };

                if (payloadLength >= 10)
                {
                    micMuted = reportBuffer[offset + 9] == 0x01;
                }
            }
        }

        decoded = new Nova7DecodedHidReport(
            profile.ProductId,
            profile.DisplayName,
            headsetOnline,
            batteryPercent,
            charging,
            hidBtState,
            chatMixGame,
            chatMixChat,
            micMuted,
            FormatRawHex(reportBuffer));
        return true;
    }

    private static bool TryParseNova5Or3PReport(
        ReadOnlySpan<byte> reportBuffer,
        int offset,
        int payloadLength,
        HeadsetProtocolProfile profile,
        bool includeChatMix,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        if (payloadLength < 5)
        {
            return false;
        }

        byte stateByte = reportBuffer[offset + 1];
        bool headsetOnline = stateByte != 0x02 && stateByte != 0x00;

        int chatMixGame = includeChatMix && payloadLength >= 7
            ? Math.Clamp((int)reportBuffer[offset + 5], 0, 100)
            : 0;
        int chatMixChat = includeChatMix && payloadLength >= 7
            ? Math.Clamp((int)reportBuffer[offset + 6], 0, 100)
            : 0;

        int? batteryPercent = null;
        BatteryChargingState charging = BatteryChargingState.Disconnected;

        if (headsetOnline)
        {
            batteryPercent = Math.Clamp((int)reportBuffer[offset + 3], 0, 100);
            charging = reportBuffer[offset + 4] == 0x01
                ? BatteryChargingState.Charging
                : BatteryChargingState.Discharging;
        }

        decoded = new Nova7DecodedHidReport(
            profile.ProductId,
            profile.DisplayName,
            headsetOnline,
            batteryPercent,
            charging,
            HidBluetoothState: null,
            chatMixGame,
            chatMixChat,
            MicMuted: null,
            FormatRawHex(reportBuffer));
        return true;
    }

    private static bool TryParseGameBudsReport(
        ReadOnlySpan<byte> reportBuffer,
        int offset,
        int payloadLength,
        HeadsetProtocolProfile profile,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        if (payloadLength < 7)
        {
            return false;
        }

        bool leftActive = reportBuffer[offset + 3] == 0x03;
        bool rightActive = reportBuffer[offset + 4] == 0x03;
        bool headsetOnline = leftActive || rightActive;

        int? batteryPercent = null;
        BatteryChargingState charging = BatteryChargingState.Disconnected;

        if (headsetOnline)
        {
            int leftBattery = Math.Clamp((int)reportBuffer[offset + 5], 0, 100);
            int rightBattery = Math.Clamp((int)reportBuffer[offset + 6], 0, 100);

            batteryPercent = (leftActive && rightActive)
                ? Math.Min(leftBattery, rightBattery)
                : (leftActive ? leftBattery : rightBattery);
            charging = BatteryChargingState.Discharging;
        }

        decoded = new Nova7DecodedHidReport(
            profile.ProductId,
            profile.DisplayName,
            headsetOnline,
            batteryPercent,
            charging,
            HidBluetoothState: null,
            ChatMixGame: 0,
            ChatMixChat: 0,
            MicMuted: null,
            FormatRawHex(reportBuffer));
        return true;
    }

    private static bool TryParseNovaProOmniReport(
        ReadOnlySpan<byte> reportBuffer,
        HeadsetProtocolProfile profile,
        out Nova7DecodedHidReport? decoded)
    {
        decoded = null;
        if (reportBuffer.Length < 16 ||
            reportBuffer[0] != profile.ReportId ||
            reportBuffer[1] != profile.StatusOpcode)
        {
            return false;
        }

        bool headsetOnline = reportBuffer[14] == 0x08;
        int? batteryPercent = null;
        BatteryChargingState charging = BatteryChargingState.Disconnected;
        bool? micMuted = null;

        if (headsetOnline)
        {
            byte rawBattery = reportBuffer[6];
            batteryPercent = rawBattery >= 99 ? 100 : Math.Clamp((int)rawBattery, 0, 100);
            charging = reportBuffer[15] == 0x02
                ? BatteryChargingState.Charging
                : BatteryChargingState.Discharging;
            micMuted = reportBuffer[9] == 0x01;
        }

        decoded = new Nova7DecodedHidReport(
            profile.ProductId,
            profile.DisplayName,
            headsetOnline,
            batteryPercent,
            charging,
            HidBluetoothState: null,
            ChatMixGame: 0,
            ChatMixChat: 0,
            micMuted,
            FormatRawHex(reportBuffer));
        return true;
    }

    private static string FormatRawHex(ReadOnlySpan<byte> reportBuffer)
    {
        int hexLen = Math.Min(16, reportBuffer.Length);
        return string.Join(' ', reportBuffer[..hexLen].ToArray().Select(b => b.ToString("X2")));
    }
}
