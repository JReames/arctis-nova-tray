using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ArctisNovaTray.Hardware;
using ArctisNovaTray.Models;
using ArctisNovaTray.Protocol;
using ArctisNovaTray.Rendering;
using ArctisNovaTray.Services;
using ArctisNovaTray.Ui;
using Microsoft.Win32;

namespace ArctisNovaTray.Tests;

internal static partial class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--generate-docs", StringComparison.OrdinalIgnoreCase))
        {
            string root = Path.GetFullPath(args[1]);
            IReadOnlyList<string> generated = DocAssetGenerator.GenerateAll(root);
            foreach (string path in generated)
            {
                Console.WriteLine($"Generated: {path}");
            }
            return 0;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--generate-ux-previews", StringComparison.OrdinalIgnoreCase))
        {
            string outDir = Path.GetFullPath(args[1]);
            IReadOnlyList<string> generated = DocAssetGenerator.GenerateUxPreviews(outDir);
            foreach (string path in generated)
            {
                Console.WriteLine($"Generated UX preview: {path}");
            }
            return 0;
        }

        Console.WriteLine("=== ArctisNovaTray Deterministic Test Suite ===");

        Run("Supported Dongle PID Catalog (22 Arctis Nova wireless PIDs, excludes 0x22A0 wired cable)", TestPidCatalog);
        Run("HID Decode: Live 0x22A1 Offline Report Ignores Cached 100% Battery", TestHidOffline22A1);
        Run("HID Decode: Gen 2 (0x22A1) Online + BT Off / Standby / Connected + Mic Mute", TestHidGen2OnlineStates);
        Run("HID Decode: Gen 1 (0x2202) Discrete Battery (0..4) & Non-0xB0 Rejection", TestHidGen1AndInvalidFrames);
        Run("HID Decode: Nova 5 / 5X (0x2232, 0x2253) & Nova 3P / 3X Wireless (0x2269, 0x226D)", TestHidNova5AndNova3PStates);
        Run("HID Decode: GameBuds (0x230A) Dual-Earbud Minimum Battery & Case Docked State", TestHidGameBudsDualEarbudStates);
        Run("HID Decode: Nova Pro Omni GameHub (0x2290) ReportId 0x01 64B Protocol", TestHidNovaProOmniGameHubStates);
        Run("Multi-Model Tooltips, Disconnected Fallback & Bluetooth Name Matching", TestMultiModelTooltipsAndBluetoothMatching);
        Run("Model Verification Tier Metadata (Hardware-Verified vs Community-Spec)", TestModelVerificationTierMetadata);
        Run("State Machine: All PC & Bluetooth Combinations Map to Exact Visual Keys", TestStateCombinationMatrix);
        Run("Tooltip Formatter: Strict <63 Char Bound Across All States & Inputs", TestTooltipBounds);
        Run("Icon Renderer: Distinctness at 16/32/64px + PNG Export", TestIconRenderingAndExport);
        Run("Application Icon: Multi-Res Win32 .ico & Embedded PE Resource", TestApplicationIconEmbeddedResource);
        Run("Icon & Card UX: Genuine Transparent Hollow Centers & Tactile States", TestHollowIconTransparencyAndCardStates);
        Run("Startup Service & Status Card Toggle: HKCU Run Lifecycle & Pixel States", TestStartupRegistrationServiceAndCardToggle);
        Run("Icon Cache: Zero GDI / USER Handle Growth Across 300 Poll Cycles", TestZeroGdiHandleLeaks);
        Run("Tray Startup Flags & Headless Taskbar Guard / ShowCard Lifecycle", TestTrayFlagsAndLifecycle);
        Run("Live Hardware Probe: Fast Non-Blocking Execution & Valid JSON", TestLiveHardwareProbe);

        Console.WriteLine();
        Console.WriteLine($"Summary: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"  [FAIL] {name}: {ex.Message}");
        }
    }

    private static void TestPidCatalog()
    {
        ushort[] expected =
        [
            0x22A1, 0x227E, 0x22A7, 0x2298, 0x2258, 0x229E, 0x22AD, 0x22A5, 0x22A9,
            0x2202, 0x2206, 0x22A4, 0x220A, 0x223A, 0x227A,
            0x2232, 0x2253, 0x2264,
            0x2269, 0x226D,
            0x230A,
            0x2290
        ];

        AssertEqual(22, Nova7HidProtocol.SupportedDongleProductIds.Count, "Supported PID count");
        foreach (ushort pid in expected)
        {
            AssertTrue(Nova7HidProtocol.SupportedDongleProductIds.Contains(pid), $"Missing PID 0x{pid:X4}");
            AssertTrue(Nova7HidProtocol.TryGetProfile(pid, out _), $"Missing profile for 0x{pid:X4}");
        }

        AssertTrue(!Nova7HidProtocol.SupportedDongleProductIds.Contains(0x22A0), "0x22A0 wired cable PID must not be in dongle set");

        byte[] req = Nova7HidProtocol.CreateStatusRequestReport(65);
        AssertEqual(65, req.Length, "Default request length");
        AssertEqual((byte)0x00, req[0], "Default Report ID");
        AssertEqual((byte)0xB0, req[1], "Status opcode");

        AssertTrue(Nova7HidProtocol.TryGetProfile(0x22A1, out HeadsetProtocolProfile nova7Profile), "0x22A1 profile");
        byte[] nova7Req = Nova7HidProtocol.CreateStatusRequestReport(nova7Profile);
        AssertEqual(65, nova7Req.Length, "0x22A1 request length");
        AssertEqual((byte)0x00, nova7Req[0], "0x22A1 Report ID");
        AssertEqual((byte)0xB0, nova7Req[1], "0x22A1 opcode");

        AssertTrue(Nova7HidProtocol.TryGetProfile(0x2290, out HeadsetProtocolProfile omniProfile), "0x2290 profile");
        AssertTrue(omniProfile.ControlUsage is null, "0x2290 ControlUsage must be null (any usage on 0xFFC0)");
        byte[] omniReq = Nova7HidProtocol.CreateStatusRequestReport(omniProfile);
        AssertEqual(64, omniReq.Length, "0x2290 request length (64B)");
        AssertEqual((byte)0x01, omniReq[0], "0x2290 Report ID (0x01)");
        AssertEqual((byte)0xB0, omniReq[1], "0x2290 opcode (0xB0)");
    }

    private static void TestHidOffline22A1()
    {
        // Exact 65-byte report observed on live 0x22A1 dongle with headset powered off:
        // 00 B0 02 64 00 64 64 00 00 01 00 ...
        byte[] report = new byte[65];
        report[0] = 0x00;
        report[1] = 0xB0;
        report[2] = 0x02; // Offline
        report[3] = 0x64; // Cached 100% from dongle; must be ignored while offline
        report[4] = 0x00;
        report[5] = 0x64;
        report[6] = 0x64;
        report[7] = 0x00;
        report[9] = 0x01;

        bool ok = Nova7HidProtocol.TryParseStatusReport(report, 0x22A1, out Nova7DecodedHidReport? decoded);
        AssertTrue(ok && decoded is not null, "Should parse offline 0xB0 report");
        AssertTrue(!decoded!.HeadsetOnline24Ghz, "HeadsetOnline24Ghz must be false for 0x02");
        AssertTrue(decoded.BatteryPercent is null, "Offline report must null out stale 0x64 battery");
        AssertEqual(BatteryChargingState.Disconnected, decoded.Charging, "Charging when offline");
        AssertTrue(decoded.HidBluetoothState is null, "Offline report should not claim HID BT state");
    }

    private static void TestHidGen2OnlineStates()
    {
        // 1. Online + BT Off (0x00) + 84% battery + Discharging
        byte[] btOff = new byte[65];
        btOff[0] = 0x00;
        btOff[1] = 0xB0;
        btOff[2] = 0x03; // Connected
        btOff[3] = 84;
        btOff[4] = 0x00; // Discharging
        btOff[7] = 0x00; // BT Off
        btOff[10] = 0x00; // Mic unmuted

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(btOff, 0x22A1, out var d1) && d1 is not null, "Parse Gen2 BT Off");
        AssertTrue(d1!.HeadsetOnline24Ghz, "Online");
        AssertEqual(84, d1.BatteryPercent, "Battery 84%");
        AssertEqual(BatteryChargingState.Discharging, d1.Charging, "Discharging");
        AssertEqual(BluetoothState.Off, d1.HidBluetoothState, "BT Off");
        AssertEqual(false, d1.MicMuted, "Mic unmuted");

        // 2. Online + BT Standby (0x03) + Charging (0x01)
        byte[] btStandby = (byte[])btOff.Clone();
        btStandby[3] = 55;
        btStandby[4] = 0x01;
        btStandby[7] = 0x03;
        btStandby[10] = 0x01;

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(btStandby, 0x22A1, out var d2) && d2 is not null, "Parse Gen2 BT Standby");
        AssertEqual(55, d2!.BatteryPercent, "Battery 55%");
        AssertEqual(BatteryChargingState.Charging, d2.Charging, "Charging");
        AssertEqual(BluetoothState.Standby, d2.HidBluetoothState, "BT Standby");
        AssertEqual(true, d2.MicMuted, "Mic muted");

        // 3. Online + BT Connected to phone/host (0x02)
        byte[] btConnected = (byte[])btOff.Clone();
        btConnected[3] = 100;
        btConnected[7] = 0x02;

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(btConnected, 0x227E, out var d3) && d3 is not null, "Parse Gen2 BT Connected");
        AssertEqual(100, d3!.BatteryPercent, "Battery 100%");
        AssertEqual(BluetoothState.Connected, d3.HidBluetoothState, "BT Connected");
    }

    private static void TestHidGen1AndInvalidFrames()
    {
        byte[] gen1 = new byte[65];
        gen1[0] = 0x00;
        gen1[1] = 0xB0;
        gen1[2] = 0x03;
        gen1[3] = 0x03; // Discrete step 3 -> 75%
        gen1[4] = 0x03; // Discharging on Gen 1

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(gen1, 0x2202, out var decoded) && decoded is not null, "Parse Gen1");
        AssertEqual(75, decoded!.BatteryPercent, "Gen1 step 3 -> 75%");
        AssertEqual(BatteryChargingState.Discharging, decoded.Charging, "Gen1 discharging");
        AssertTrue(decoded.HidBluetoothState is null, "Gen1 does not advertise HID BT state");

        // Non-0xB0 GG frame (e.g. 0x20 settings report) must be rejected
        byte[] otherFrame = new byte[65];
        otherFrame[0] = 0x00;
        otherFrame[1] = 0x20;
        AssertTrue(!Nova7HidProtocol.TryParseStatusReport(otherFrame, 0x22A1, out _), "0x20 opcode must be rejected");
        AssertTrue(!Nova7HidProtocol.TryParseStatusReport([0x00, 0xB0], 0x22A1, out _), "Truncated buffer must be rejected");
    }

    private static void TestHidNova5AndNova3PStates()
    {
        // 1. Nova 5 (0x2232) online with 78% battery, charging, ChatMix 80/100
        byte[] nova5On = new byte[65];
        nova5On[0] = 0x00;
        nova5On[1] = 0xB0;
        nova5On[2] = 0x03; // Online (!= 0x02 && != 0x00)
        nova5On[4] = 78;   // data[3] = battery 0..100
        nova5On[5] = 0x01; // data[4] = charging
        nova5On[6] = 80;   // data[5] = chatMixGame
        nova5On[7] = 100;  // data[6] = chatMixChat

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(nova5On, 0x2232, out var d5) && d5 is not null, "Parse Nova 5 online");
        AssertEqual("Arctis Nova 5", d5!.ProductName, "Nova 5 product name");
        AssertTrue(d5.HeadsetOnline24Ghz, "Nova 5 online");
        AssertEqual(78, d5.BatteryPercent, "Nova 5 battery 78%");
        AssertEqual(BatteryChargingState.Charging, d5.Charging, "Nova 5 charging");
        AssertEqual(80, d5.ChatMixGame, "Nova 5 ChatMix game");
        AssertEqual(100, d5.ChatMixChat, "Nova 5 ChatMix chat");
        AssertTrue(d5.HidBluetoothState is null, "Nova 5 HID BT null");

        // 2. Nova 5X (0x2253 & 0x2264) offline (state 0x02 and 0x00) ignores stale battery byte
        byte[] nova5Off = (byte[])nova5On.Clone();
        nova5Off[2] = 0x02;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(nova5Off, 0x2253, out var d5Off) && d5Off is not null, "Parse Nova 5X offline 0x02");
        AssertTrue(!d5Off!.HeadsetOnline24Ghz, "Nova 5X offline for 0x02");
        AssertTrue(d5Off.BatteryPercent is null, "Nova 5X offline battery null");
        AssertEqual(BatteryChargingState.Disconnected, d5Off.Charging, "Nova 5X offline Disconnected");

        nova5Off[2] = 0x00;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(nova5Off, 0x2264, out var d5Zero) && d5Zero is not null, "Parse Nova 5X offline 0x00");
        AssertTrue(!d5Zero!.HeadsetOnline24Ghz, "Nova 5X offline for 0x00");

        // 3. Nova 3P Wireless (0x2269) & Nova 3X Wireless (0x226D) minimum 5-byte raw payload
        byte[] nova3PRaw = [0xB0, 0x01, 0x00, 63, 0x00];
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(nova3PRaw, 0x2269, out var d3P) && d3P is not null, "Parse Nova 3P 5-byte raw");
        AssertEqual("Arctis Nova 3P Wireless", d3P!.ProductName, "Nova 3P product name");
        AssertTrue(d3P.HeadsetOnline24Ghz, "Nova 3P online (state 0x01)");
        AssertEqual(63, d3P.BatteryPercent, "Nova 3P battery 63%");
        AssertEqual(BatteryChargingState.Discharging, d3P.Charging, "Nova 3P discharging");
        AssertEqual(0, d3P.ChatMixGame, "Nova 3P ChatMix 0");

        byte[] nova3XWin = new byte[65];
        nova3XWin[0] = 0x00;
        nova3XWin[1] = 0xB0;
        nova3XWin[2] = 0x03;
        nova3XWin[4] = 91;
        nova3XWin[5] = 0x01;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(nova3XWin, 0x226D, out var d3X) && d3X is not null, "Parse Nova 3X 65B");
        AssertEqual("Arctis Nova 3X Wireless", d3X!.ProductName, "Nova 3X product name");
        AssertEqual(91, d3X.BatteryPercent, "Nova 3X battery 91%");
        AssertEqual(BatteryChargingState.Charging, d3X.Charging, "Nova 3X charging");
    }

    private static void TestHidGameBudsDualEarbudStates()
    {
        // 1. Both earbuds active (0x03 / 0x03) -> battery = min(left 82, right 64) = 64%
        byte[] bothActive = new byte[65];
        bothActive[0] = 0x00;
        bothActive[1] = 0xB0;
        bothActive[4] = 0x03; // data[3] = left active
        bothActive[5] = 0x03; // data[4] = right active
        bothActive[6] = 82;   // data[5] = left battery
        bothActive[7] = 64;   // data[6] = right battery

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(bothActive, 0x230A, out var gBoth) && gBoth is not null, "Parse GameBuds both active");
        AssertEqual("Arctis GameBuds", gBoth!.ProductName, "GameBuds product name");
        AssertTrue(gBoth.HeadsetOnline24Ghz, "GameBuds online when both active");
        AssertEqual(64, gBoth.BatteryPercent, "GameBuds min battery (64%)");
        AssertEqual(BatteryChargingState.Discharging, gBoth.Charging, "GameBuds discharging");

        // 2. Single earbud active (left 0x03 = 82%, right docked 0x02 = 15% ignored) -> 82%
        byte[] leftOnly = (byte[])bothActive.Clone();
        leftOnly[5] = 0x02;
        leftOnly[7] = 15;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(leftOnly, 0x230A, out var gLeft) && gLeft is not null, "Parse GameBuds left only");
        AssertTrue(gLeft!.HeadsetOnline24Ghz, "GameBuds online when left active");
        AssertEqual(82, gLeft.BatteryPercent, "GameBuds left-only battery 82%");

        // 3. Right earbud only active (left docked 0x02, right 0x03 = 51%) -> 51%
        byte[] rightOnly = (byte[])bothActive.Clone();
        rightOnly[4] = 0x02;
        rightOnly[5] = 0x03;
        rightOnly[7] = 51;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(rightOnly, 0x230A, out var gRight) && gRight is not null, "Parse GameBuds right only");
        AssertEqual(51, gRight!.BatteryPercent, "GameBuds right-only battery 51%");

        // 4. Both earbuds docked in case (0x02 / 0x02) -> offline, null battery, Disconnected
        byte[] bothDocked = (byte[])bothActive.Clone();
        bothDocked[4] = 0x02;
        bothDocked[5] = 0x02;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(bothDocked, 0x230A, out var gOff) && gOff is not null, "Parse GameBuds docked");
        AssertTrue(!gOff!.HeadsetOnline24Ghz, "GameBuds offline when both docked");
        AssertTrue(gOff.BatteryPercent is null, "GameBuds docked battery null");
        AssertEqual(BatteryChargingState.Disconnected, gOff.Charging, "GameBuds docked Disconnected");
    }

    private static void TestHidNovaProOmniGameHubStates()
    {
        // 1. Online (reply[14] == 0x08), rawBatt 99 -> 100%, charging (reply[15] == 0x02), mic muted (reply[9] == 0x01)
        byte[] omniOn = new byte[64];
        omniOn[0] = 0x01; // ReportId = 0x01
        omniOn[1] = 0xB0; // Opcode = 0xB0
        omniOn[6] = 99;   // rawBatt >= 99 -> 100%
        omniOn[9] = 0x01; // micMuted = true
        omniOn[14] = 0x08; // online
        omniOn[15] = 0x02; // charging

        AssertTrue(Nova7HidProtocol.TryParseStatusReport(omniOn, 0x2290, out var dOn) && dOn is not null, "Parse Nova Pro Omni online");
        AssertEqual("Arctis Nova Pro Omni", dOn!.ProductName, "Nova Pro Omni product name");
        AssertTrue(dOn.HeadsetOnline24Ghz, "Nova Pro Omni online");
        AssertEqual(100, dOn.BatteryPercent, "Nova Pro Omni 99 -> 100%");
        AssertEqual(BatteryChargingState.Charging, dOn.Charging, "Nova Pro Omni charging");
        AssertEqual(true, dOn.MicMuted, "Nova Pro Omni mic muted");
        AssertTrue(dOn.HidBluetoothState is null, "Nova Pro Omni HID BT null");

        // 2. Discharging at 74% + unmuted
        byte[] omniDis = (byte[])omniOn.Clone();
        omniDis[6] = 74;
        omniDis[9] = 0x00;
        omniDis[15] = 0x00;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(omniDis, 0x2290, out var dDis) && dDis is not null, "Parse Nova Pro Omni discharging");
        AssertEqual(74, dDis!.BatteryPercent, "Nova Pro Omni 74%");
        AssertEqual(BatteryChargingState.Discharging, dDis.Charging, "Nova Pro Omni discharging");
        AssertEqual(false, dDis.MicMuted, "Nova Pro Omni mic unmuted");

        // 3. Offline (reply[14] == 0x04) -> null battery, Disconnected, null mic
        byte[] omniOff = (byte[])omniOn.Clone();
        omniOff[14] = 0x04;
        AssertTrue(Nova7HidProtocol.TryParseStatusReport(omniOff, 0x2290, out var dOff) && dOff is not null, "Parse Nova Pro Omni offline");
        AssertTrue(!dOff!.HeadsetOnline24Ghz, "Nova Pro Omni offline");
        AssertTrue(dOff.BatteryPercent is null, "Nova Pro Omni offline battery null");
        AssertEqual(BatteryChargingState.Disconnected, dOff.Charging, "Nova Pro Omni offline Disconnected");
        AssertTrue(dOff.MicMuted is null, "Nova Pro Omni offline mic null");

        // 4. Reject ReportId 0x00 or truncated < 16B on 0x2290
        byte[] wrongReportId = (byte[])omniOn.Clone();
        wrongReportId[0] = 0x00;
        AssertTrue(!Nova7HidProtocol.TryParseStatusReport(wrongReportId, 0x2290, out _), "0x2290 must reject ReportId 0x00");
        AssertTrue(!Nova7HidProtocol.TryParseStatusReport(omniOn[..15], 0x2290, out _), "0x2290 must reject < 16 bytes");
    }

    private static void TestMultiModelTooltipsAndBluetoothMatching()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        // 1. Existing Nova 7 (0x22A1) tooltips stay byte-for-byte identical
        var nova7Active = new HeadsetSnapshot
        {
            Timestamp = now,
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Connected,
            DonglePresent = true,
            DongleProductName = "Arctis Nova 7",
            DongleVidPid = "1038:22A1",
            HeadsetOnline24Ghz = true,
            BatteryPercent = 86,
            Charging = BatteryChargingState.Discharging
        };
        AssertEqual("Nova 7: PC 2.4 GHz | BT Connected | 86%", TooltipFormatter.Format(nova7Active), "Nova 7 active tooltip");
        AssertEqual("Arctis Nova 7", nova7Active.DeviceDisplayName, "Nova 7 display name");

        var nova7Off = new HeadsetSnapshot
        {
            Timestamp = now,
            PcConnection = PcConnectionState.DongleOnlyHeadsetOff,
            Bluetooth = BluetoothState.Off,
            DonglePresent = true,
            DongleProductName = "Arctis Nova 7",
            DongleVidPid = "1038:22A1"
        };
        AssertEqual("Nova 7: Off or Asleep (Receiver In)", TooltipFormatter.Format(nova7Off), "Nova 7 off tooltip");

        // 2. Completely disconnected fallback uses "Arctis Nova"
        var disconnected = new HeadsetSnapshot
        {
            Timestamp = now,
            PcConnection = PcConnectionState.Disconnected,
            Bluetooth = BluetoothState.Off
        };
        AssertEqual("Arctis Nova", disconnected.DeviceDisplayName, "Disconnected DeviceDisplayName");
        AssertEqual("Arctis Nova: Disconnected (No Receiver / BT)", TooltipFormatter.Format(disconnected), "Disconnected tooltip");

        // 3. Multi-model VID:PID prefix resolution across Nova 5, 5X, 3P, 3X, Nova Pro, GameBuds
        (string VidPid, string Product, string ExpectedPrefix)[] models =
        [
            ("1038:2232", "Arctis Nova 5", "Nova 5"),
            ("1038:2253", "Arctis Nova 5X", "Nova 5X"),
            ("1038:2269", "Arctis Nova 3P Wireless", "Nova 3P"),
            ("1038:226D", "Arctis Nova 3X Wireless", "Nova 3X"),
            ("1038:2290", "Arctis Nova Pro Omni", "Nova Pro"),
            ("1038:230A", "Arctis GameBuds", "GameBuds")
        ];

        foreach (var (vidPid, product, prefix) in models)
        {
            var s = new HeadsetSnapshot
            {
                Timestamp = now,
                PcConnection = PcConnectionState.Connected24Ghz,
                Bluetooth = BluetoothState.Off,
                DonglePresent = true,
                DongleProductName = product,
                DongleVidPid = vidPid,
                HeadsetOnline24Ghz = true,
                BatteryPercent = 72,
                Charging = BatteryChargingState.Discharging
            };
            AssertEqual($"{prefix}: PC 2.4 GHz | BT Off | 72%", TooltipFormatter.Format(s), $"Tooltip for {vidPid}");
        }

        // 4. PC Bluetooth-only fallback prefix + Win32BluetoothProbe name matching
        var btGameBuds = new HeadsetSnapshot
        {
            Timestamp = now,
            PcConnection = PcConnectionState.ConnectedBluetooth,
            Bluetooth = BluetoothState.Connected,
            WindowsBluetoothConnected = true,
            WindowsBluetoothDeviceName = "SteelSeries Arctis GameBuds"
        };
        AssertEqual("GameBuds: PC BT | BT Connected", TooltipFormatter.Format(btGameBuds), "BT GameBuds tooltip");

        AssertTrue(Win32BluetoothProbe.IsMatchingHeadsetName("Arctis Nova 5X"), "BT match Nova 5X");
        AssertTrue(Win32BluetoothProbe.IsMatchingHeadsetName("Arctis Nova 3P Wireless"), "BT match Nova 3P");
        AssertTrue(Win32BluetoothProbe.IsMatchingHeadsetName("Arctis Nova Pro Wireless"), "BT match Nova Pro");
        AssertTrue(Win32BluetoothProbe.IsMatchingHeadsetName("GameBuds"), "BT match GameBuds");
        AssertTrue(!Win32BluetoothProbe.IsMatchingHeadsetName("Generic Speakers"), "BT reject unrelated device");
    }

    private static void TestModelVerificationTierMetadata()
    {
        AssertTrue(Nova7HidProtocol.TryGetProfile(0x22A1, out var p22A1), "0x22A1 profile");
        AssertEqual(ModelVerificationTier.HardwareVerified, p22A1.VerificationTier, "0x22A1 HardwareVerified");
        AssertEqual("Hardware-Verified", p22A1.VerificationTierLabel, "0x22A1 label");

        int hardwareVerifiedCount = Nova7HidProtocol.ProfilesByPid.Values.Count(p => p.VerificationTier == ModelVerificationTier.HardwareVerified);
        int communitySpecCount = Nova7HidProtocol.ProfilesByPid.Values.Count(p => p.VerificationTier == ModelVerificationTier.CommunitySpec);
        AssertEqual(1, hardwareVerifiedCount, "HardwareVerified count");
        AssertEqual(21, communitySpecCount, "CommunitySpec count");

        DateTimeOffset now = DateTimeOffset.Now;
        HeadsetSnapshot verifiedSnap = Nova7HardwareProbe.CombineStates(now, true, "Arctis Nova 7", "1038:22A1", null, WindowsBluetoothHeadsetStatus.None);
        AssertEqual("Hardware-Verified", verifiedSnap.VerificationTier, "Snapshot tier for 0x22A1");

        HeadsetSnapshot communitySnap = Nova7HardwareProbe.CombineStates(now, true, "Arctis Nova 5", "1038:2232", null, WindowsBluetoothHeadsetStatus.None);
        AssertEqual("Community-Spec", communitySnap.VerificationTier, "Snapshot tier for 0x2232");

        HeadsetSnapshot noneSnap = Nova7HardwareProbe.CombineStates(now, false, null, null, null, WindowsBluetoothHeadsetStatus.None);
        AssertTrue(noneSnap.VerificationTier is null, "Disconnected snapshot tier should be null");
    }

    private static void TestStateCombinationMatrix()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var noWinBt = WindowsBluetoothHeadsetStatus.None;
        var winBtConnected = new WindowsBluetoothHeadsetStatus(true, true, "Arctis Nova 7", @"BTH\DEV_112233445566");

        var onlineBtPhone = new Nova7DecodedHidReport(
            0x22A1, "Arctis Nova 7", true, 80, BatteryChargingState.Discharging, BluetoothState.Connected, 100, 100, false, "00 B0 03");
        var offlineReport = new Nova7DecodedHidReport(
            0x22A1, "Arctis Nova 7", false, null, BatteryChargingState.Disconnected, null, 100, 100, null, "00 B0 02");

        // 1. 2.4GHz active + phone Bluetooth active (reported by dongle!)
        HeadsetSnapshot s1 = Nova7HardwareProbe.CombineStates(now, true, "Arctis Nova 7", "1038:22A1", onlineBtPhone, noWinBt);
        AssertEqual(PcConnectionState.Connected24Ghz, s1.PcConnection, "s1 PC");
        AssertEqual(BluetoothState.Connected, s1.Bluetooth, "s1 BT");
        AssertEqual(BluetoothSource.DongleHidReport, s1.BluetoothDetectionSource, "s1 BT source");
        AssertEqual(new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected), s1.VisualState, "s1 visual");

        // 2. Dongle unplugged + PC Bluetooth connected
        HeadsetSnapshot s2 = Nova7HardwareProbe.CombineStates(now, false, null, null, null, winBtConnected);
        AssertEqual(PcConnectionState.ConnectedBluetooth, s2.PcConnection, "s2 PC");
        AssertEqual(BluetoothState.Connected, s2.Bluetooth, "s2 BT");
        AssertEqual(new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected), s2.VisualState, "s2 visual");

        // 3. Dongle plugged in + Headset OFF + no PC Bluetooth
        HeadsetSnapshot s3 = Nova7HardwareProbe.CombineStates(now, true, "Arctis Nova 7", "1038:22A1", offlineReport, noWinBt);
        AssertEqual(PcConnectionState.DongleOnlyHeadsetOff, s3.PcConnection, "s3 PC");
        AssertEqual(BluetoothState.Off, s3.Bluetooth, "s3 BT");
        AssertEqual(new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off), s3.VisualState, "s3 visual");

        // 4. Completely disconnected
        HeadsetSnapshot s4 = Nova7HardwareProbe.CombineStates(now, false, null, null, null, noWinBt);
        AssertEqual(PcConnectionState.Disconnected, s4.PcConnection, "s4 PC");
        AssertEqual(BluetoothState.Off, s4.Bluetooth, "s4 BT");
        AssertEqual(new TrayVisualKey(HeadsetBaseVisual.Disconnected, BluetoothBadgeVisual.Off), s4.VisualState, "s4 visual");
    }

    private static void TestTooltipBounds()
    {
        foreach (PcConnectionState pc in Enum.GetValues<PcConnectionState>())
        {
            foreach (BluetoothState bt in Enum.GetValues<BluetoothState>())
            {
                foreach (int? batt in new int?[] { null, 0, 42, 100, 250 })
                {
                    foreach (BatteryChargingState chg in Enum.GetValues<BatteryChargingState>())
                    {
                        var snap = new HeadsetSnapshot
                        {
                            Timestamp = DateTimeOffset.Now,
                            PcConnection = pc,
                            Bluetooth = bt,
                            BatteryPercent = batt,
                            Charging = chg,
                            DongleProductName = new string('X', 200)
                        };

                        string text = TooltipFormatter.Format(snap);
                        AssertTrue(!string.IsNullOrWhiteSpace(text), "Tooltip must be non-empty");
                        AssertTrue(text.Length < 63, $"Tooltip exceeded 62 chars ({text.Length}): {text}");
                    }
                }
            }
        }

        string longTruncated = TooltipFormatter.TruncateSafe(new string('A', 200));
        AssertEqual(TooltipFormatter.MaxTooltipLength, longTruncated.Length, "TruncateSafe exact bound");
        AssertTrue(longTruncated.Length < 63, "TruncateSafe < 63");
    }

    private static void TestIconRenderingAndExport()
    {
        foreach (int size in TrayIconRenderer.StandardExportSizes)
        {
            var seenHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, key) in TrayIconRenderer.CanonicalExportStates)
            {
                using Bitmap bmp = TrayIconRenderer.RenderBitmap(key, size);
                AssertEqual(size, bmp.Width, $"{name} width");
                AssertEqual(size, bmp.Height, $"{name} height");

                string hash = ComputePixelHash(bmp, out int nonTransparentPixels);
                AssertTrue(nonTransparentPixels > (size * size) / 8, $"{name} {size}x{size} should have visible pixels");
                AssertTrue(seenHashes.Add(hash), $"Duplicate visual rendering detected for {name} at {size}x{size}");
            }
        }

        string tempExportDir = Path.Combine(AppContext.BaseDirectory, "test-icon-export");
        if (Directory.Exists(tempExportDir))
        {
            Directory.Delete(tempExportDir, recursive: true);
        }

        try
        {
            IReadOnlyList<string> files = TrayIconRenderer.ExportAllStates(tempExportDir);
            AssertEqual(15, files.Count, "Expected 5 states * 3 sizes = 15 PNGs");
            foreach (string file in files)
            {
                AssertTrue(File.Exists(file), $"Missing exported PNG: {file}");
                AssertTrue(new FileInfo(file).Length > 100, $"Empty PNG: {file}");
            }
        }
        finally
        {
            if (Directory.Exists(tempExportDir))
            {
                Directory.Delete(tempExportDir, recursive: true);
            }
        }
    }

    private static void TestHollowIconTransparencyAndCardStates()
    {
        // 1. DongleOnlyHeadsetOff earcup centers must be genuinely transparent (A == 0) at BOTH 32x32 AND 16x16!
        using (Bitmap off32 = TrayIconRenderer.RenderBitmap(
            new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off), 32))
        {
            AssertEqual((byte)0, off32.GetPixel(6, 19).A, "32x32 left hollow earcup center alpha at (6,19)");
            AssertEqual((byte)0, off32.GetPixel(26, 19).A, "32x32 right hollow earcup center alpha at (26,19)");
        }

        using (Bitmap off16 = TrayIconRenderer.RenderBitmap(
            new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off), 16))
        {
            AssertEqual((byte)0, off16.GetPixel(2, 9).A, "16x16 left hollow earcup alpha at (2,9)");
            AssertEqual((byte)0, off16.GetPixel(3, 9).A, "16x16 left hollow earcup alpha at (3,9)");
            AssertEqual((byte)0, off16.GetPixel(12, 9).A, "16x16 right hollow earcup alpha at (12,9)");
            AssertEqual((byte)0, off16.GetPixel(13, 9).A, "16x16 right hollow earcup alpha at (13,9)");
        }

        // 2. Standby Bluetooth ring center must be genuinely transparent (A == 0) at BOTH 32x32 AND 16x16!
        using (Bitmap standby32 = TrayIconRenderer.RenderBitmap(
            new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Standby), 32))
        {
            AssertEqual((byte)0, standby32.GetPixel(23, 23).A, "32x32 Standby Bluetooth ring center alpha at (23,23)");
        }

        using (Bitmap standby16 = TrayIconRenderer.RenderBitmap(
            new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Standby), 16))
        {
            AssertEqual((byte)0, standby16.GetPixel(11, 11).A, "16x16 Standby Bluetooth ring center alpha at (11,11)");
        }

        // 3. Menu summary uses plain-English "(Charging)" while compact tooltip stays < 63 chars
        var chargingSnap = new HeadsetSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Standby,
            BatteryPercent = 42,
            Charging = BatteryChargingState.Charging
        };
        string menuSummary = TooltipFormatter.FormatMenuSummary(chargingSnap);
        AssertTrue(menuSummary.Contains("42% (Charging)", StringComparison.Ordinal), $"Expected '42% (Charging)' in menu summary, got '{menuSummary}'");

        // 4. All 5 RefreshVisualState renders at 1x must produce distinct visual frames
        var snap = new HeadsetSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Connected,
            DonglePresent = true,
            DongleVidPid = "1038:22A1",
            HeadsetOnline24Ghz = true,
            BatteryPercent = 86,
            Charging = BatteryChargingState.Discharging
        };

        var cardHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (RefreshVisualState state in Enum.GetValues<RefreshVisualState>())
        {
            using Bitmap card = StatusFlyoutForm.RenderCardBitmap(snap, scale: 1.0f, refreshState: state, referenceNow: snap.Timestamp);
            AssertEqual(StatusFlyoutForm.LogicalWidth, card.Width, $"Card width ({state})");
            AssertEqual(StatusFlyoutForm.LogicalHeight, card.Height, $"Card height ({state})");
            string hash = ComputePixelHash(card, out _);
            AssertTrue(cardHashes.Add(hash), $"RefreshVisualState.{state} should render distinctly");
        }
    }

    private static void TestZeroGdiHandleLeaks()
    {
        using var cache = new TrayIconCache();
        IntPtr currentProc = Process.GetCurrentProcess().Handle;

        // Warm up all 5 canonical states at 32x32
        foreach (var (_, key) in TrayIconRenderer.CanonicalExportStates)
        {
            _ = cache.GetIcon(key, 32);
        }

        AssertEqual(5, cache.CachedCount, "Warm cache count");

        uint gdiBefore = GetGuiResources(currentProc, 0); // GR_GDIOBJECTS
        uint userBefore = GetGuiResources(currentProc, 1); // GR_USEROBJECTS

        for (int i = 0; i < 300; i++)
        {
            foreach (var (_, key) in TrayIconRenderer.CanonicalExportStates)
            {
                Icon icon = cache.GetIcon(key, 32);
                AssertTrue(icon.Handle != IntPtr.Zero, "Cached HICON must be valid");
            }
        }

        uint gdiAfter = GetGuiResources(currentProc, 0);
        uint userAfter = GetGuiResources(currentProc, 1);

        AssertEqual(5, cache.CachedCount, "Cache count after 300 poll loops");
        AssertTrue(gdiAfter <= gdiBefore + 2, $"GDI handle leak detected: before={gdiBefore}, after={gdiAfter}");
        AssertTrue(userAfter <= userBefore + 2, $"USER/HICON handle leak detected: before={userBefore}, after={userAfter}");
    }

    private static void TestTrayFlagsAndLifecycle()
    {
        AssertTrue(ArctisNovaTray.Program.TryParseTrayStartupFlags([], out bool silent0, out bool headless0), "Empty args");
        AssertTrue(!silent0 && !headless0, "Defaults false");

        AssertTrue(ArctisNovaTray.Program.TryParseTrayStartupFlags(["--silent", "--allow-headless"], out bool silent1, out bool headless1), "Tray flags");
        AssertTrue(silent1 && headless1, "Both tray flags true");

        AssertTrue(!ArctisNovaTray.Program.TryParseTrayStartupFlags(["--status-json"], out _, out _), "CLI status-json is not tray mode");
        AssertTrue(!ArctisNovaTray.Program.TryParseTrayStartupFlags(["--enable-startup"], out _, out _), "CLI enable-startup is not tray mode");
        AssertTrue(!ArctisNovaTray.Program.TryParseTrayStartupFlags(["--disable-startup"], out _, out _), "CLI disable-startup is not tray mode");
        AssertTrue(!ArctisNovaTray.Program.TryParseTrayStartupFlags(["--startup-status"], out _, out _), "CLI startup-status is not tray mode");
        AssertTrue(!ArctisNovaTray.Program.TryParseTrayStartupFlags(["--export-icons", "out"], out _, out _), "CLI export-icons is not tray mode");

        // Only run live child-process mutex/event test when no interactive user instance is already active.
        if (Process.GetProcessesByName("ArctisNovaTray").Length > 0)
        {
            return;
        }

        string exePath = Path.Combine(AppContext.BaseDirectory, "ArctisNovaTray.exe");
        AssertTrue(File.Exists(exePath), $"Expected built ArctisNovaTray.exe beside test binary at {exePath}");

        if (!ArctisNovaTray.Program.HasInteractiveTaskbar())
        {
            using var guardProc = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardError = true
            })!;
            AssertTrue(guardProc.WaitForExit(3000), "Headless guard should exit immediately");
            AssertEqual(ArctisNovaTray.Program.NoTaskbarExitCode, guardProc.ExitCode, "Headless exit code 3");

            // Confirm mutex was NOT squatted on.
            using var checkMutex = new Mutex(false, ArctisNovaTray.Program.SingleInstanceMutexName, out bool mutexFree);
            AssertTrue(mutexFree, "Headless guard must not hold single-instance mutex");
        }

        using var daemon = Process.Start(new ProcessStartInfo(exePath, "--allow-headless --silent")
        {
            UseShellExecute = false
        })!;

        try
        {
            long deadline = Environment.TickCount64 + 3000;
            bool eventsReady = false;
            while (Environment.TickCount64 < deadline)
            {
                if (EventWaitHandle.TryOpenExisting(TrayApplicationContext.ShutdownEventName, out var s) &&
                    EventWaitHandle.TryOpenExisting(TrayApplicationContext.ShowCardEventName, out var c))
                {
                    s.Dispose();
                    c.Dispose();
                    eventsReady = true;
                    break;
                }

                Thread.Sleep(50);
            }

            AssertTrue(eventsReady && !daemon.HasExited, "Headless test daemon should initialize Shutdown and ShowCard events");

            // Second launch should signal ShowCard and exit cleanly with 0 via single-instance mutex.
            using var duplicate = Process.Start(new ProcessStartInfo(exePath, "--allow-headless")
            {
                UseShellExecute = false
            })!;
            AssertTrue(duplicate.WaitForExit(3000), "Duplicate instance should exit promptly");
            AssertEqual(0, duplicate.ExitCode, "Duplicate instance exit code");

            // Signal graceful shutdown and verify clean exit with 0 processes left behind.
            if (EventWaitHandle.TryOpenExisting(TrayApplicationContext.ShutdownEventName, out EventWaitHandle? shutdownEvt))
            {
                using (shutdownEvt)
                {
                    _ = shutdownEvt.Set();
                }
            }

            AssertTrue(daemon.WaitForExit(3000), "Daemon should exit cleanly on Shutdown event");
            AssertEqual(0, daemon.ExitCode, "Daemon graceful exit code");
        }
        finally
        {
            if (!daemon.HasExited)
            {
                daemon.Kill(entireProcessTree: true);
                daemon.WaitForExit(1000);
            }
        }
    }

    private static void TestApplicationIconEmbeddedResource()
    {
        string tempIco = Path.Combine(AppContext.BaseDirectory, $"test-app-{Guid.NewGuid():N}.ico");
        try
        {
            TrayIconRenderer.WriteApplicationIco(tempIco);
            byte[] icoBytes = File.ReadAllBytes(tempIco);
            AssertTrue(icoBytes.Length > 4096, $"Expected multi-resolution .ico > 4KB, got {icoBytes.Length}");
            AssertEqual((ushort)0, BitConverter.ToUInt16(icoBytes, 0), "ICO reserved header");
            AssertEqual((ushort)1, BitConverter.ToUInt16(icoBytes, 2), "ICO type = 1");
            AssertEqual((ushort)7, BitConverter.ToUInt16(icoBytes, 4), "ICO frame count = 7 (16..256)");

            using var loaded16 = new Icon(tempIco, new Size(16, 16));
            using var loaded32 = new Icon(tempIco, new Size(32, 32));
            using var loaded48 = new Icon(tempIco, new Size(48, 48));
            using var loaded256 = new Icon(tempIco, new Size(256, 256));
            AssertTrue(loaded16.Handle != IntPtr.Zero && loaded32.Handle != IntPtr.Zero && loaded48.Handle != IntPtr.Zero && loaded256.Handle != IntPtr.Zero, "Multi-res ICO handles valid");
        }
        finally
        {
            if (File.Exists(tempIco))
            {
                File.Delete(tempIco);
            }
        }

        string exePath = Path.Combine(AppContext.BaseDirectory, "ArctisNovaTray.exe");
        AssertTrue(File.Exists(exePath), $"Expected ArctisNovaTray.exe beside test binary at {exePath}");
        using Icon? embedded = Icon.ExtractAssociatedIcon(exePath);
        AssertTrue(embedded is not null && embedded.Handle != IntPtr.Zero, "ArctisNovaTray.exe must contain an embedded Win32 PE application icon");

        string repoIco = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "arctis-nova-tray", "assets", "arctis-nova-tray.ico"));
        if (File.Exists(repoIco))
        {
            using var repoIcon = new Icon(repoIco);
            AssertTrue(repoIcon.Handle != IntPtr.Zero, "Committed assets/arctis-nova-tray.ico should load cleanly");
        }
    }

    private static void TestStartupRegistrationServiceAndCardToggle()
    {
        string testSubKey = @"Software\ArctisNovaTray_UnitTest_" + Guid.NewGuid().ToString("N");
        string fakeExe = Path.Combine(AppContext.BaseDirectory, "ArctisNovaTray.exe");
        var service = new RegistryStartupRegistrationService(testSubKey, "ArctisNovaTray", fakeExe);

        try
        {
            AssertTrue(!service.IsStartupEnabled, "Initial isolated startup state must be false");

            service.SetStartupEnabled(true);
            AssertTrue(service.IsStartupEnabled, "Startup state should be true after enabling");

            string expectedCmd = $"\"{Path.GetFullPath(fakeExe)}\" --silent";
            AssertEqual(expectedCmd, service.BuildCommand(), "Startup command string");

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(testSubKey, writable: false);
            AssertEqual(expectedCmd, key?.GetValue("ArctisNovaTray") as string, "Registry value written");

            service.SetStartupEnabled(false);
            AssertTrue(!service.IsStartupEnabled, "Startup state should be false after disabling");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testSubKey, throwOnMissingSubKey: false);
        }

        var snap = new HeadsetSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Connected,
            DonglePresent = true,
            HeadsetOnline24Ghz = true,
            BatteryPercent = 86
        };

        using Bitmap cardOff = StatusFlyoutForm.RenderCardBitmap(snap, scale: 1.0f, startupEnabled: false, referenceNow: snap.Timestamp);
        using Bitmap cardOn = StatusFlyoutForm.RenderCardBitmap(snap, scale: 1.0f, startupEnabled: true, referenceNow: snap.Timestamp);
        AssertEqual(StatusFlyoutForm.LogicalWidth, cardOff.Width, "Card width");
        AssertEqual(StatusFlyoutForm.LogicalHeight, cardOff.Height, "Card height");
        AssertTrue(!string.Equals(ComputePixelHash(cardOff, out _), ComputePixelHash(cardOn, out _), StringComparison.Ordinal), "Startup toggle Off vs On must render distinct pixels");
    }

    private static void TestLiveHardwareProbe()
    {
        var sw = Stopwatch.StartNew();
        var probe = new Nova7HardwareProbe();
        HeadsetSnapshot live = probe.Probe();
        sw.Stop();

        AssertTrue(sw.ElapsedMilliseconds < 2000, $"Live probe took too long ({sw.ElapsedMilliseconds}ms)");
        string tooltip = TooltipFormatter.Format(live);
        AssertTrue(tooltip.Length < 63, "Live tooltip < 63 chars");

        string json = JsonSerializer.Serialize(live);
        AssertTrue(json.Contains("PcConnection", StringComparison.OrdinalIgnoreCase), "JSON contains PcConnection");
    }

    private static string ComputePixelHash(Bitmap bmp, out int visibleCount)
    {
        visibleCount = 0;
        byte[] raw = new byte[bmp.Width * bmp.Height * 4];
        int idx = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.A > 16)
                {
                    visibleCount++;
                }

                raw[idx++] = c.B;
                raw[idx++] = c.G;
                raw[idx++] = c.R;
                raw[idx++] = c.A;
            }
        }

        return Convert.ToHexString(SHA256.HashData(raw));
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
