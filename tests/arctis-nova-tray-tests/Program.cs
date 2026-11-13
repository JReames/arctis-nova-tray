using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ArctisNovaTray.Hardware;
using ArctisNovaTray.Models;
using ArctisNovaTray.Protocol;
using ArctisNovaTray.Rendering;
using ArctisNovaTray.Ui;

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

        Console.WriteLine("=== ArctisNovaTray Deterministic Test Suite ===");

        Run("Supported Dongle PID Catalog (15 Nova 7 models, excludes 0x22A0 wired cable)", TestPidCatalog);
        Run("HID Decode: Live 0x22A1 Offline Report Ignores Cached 100% Battery", TestHidOffline22A1);
        Run("HID Decode: Gen 2 (0x22A1) Online + BT Off / Standby / Connected + Mic Mute", TestHidGen2OnlineStates);
        Run("HID Decode: Gen 1 (0x2202) Discrete Battery (0..4) & Non-0xB0 Rejection", TestHidGen1AndInvalidFrames);
        Run("State Machine: All PC & Bluetooth Combinations Map to Exact Visual Keys", TestStateCombinationMatrix);
        Run("Tooltip Formatter: Strict <63 Char Bound Across All States & Inputs", TestTooltipBounds);
        Run("Icon Renderer: Distinctness at 16/32/64px + PNG Export", TestIconRenderingAndExport);
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
            0x2202, 0x2206, 0x22A4, 0x220A, 0x223A, 0x227A
        ];

        AssertEqual(15, Nova7HidProtocol.SupportedDongleProductIds.Count, "Supported PID count");
        foreach (ushort pid in expected)
        {
            AssertTrue(Nova7HidProtocol.SupportedDongleProductIds.Contains(pid), $"Missing PID 0x{pid:X4}");
        }

        AssertTrue(!Nova7HidProtocol.SupportedDongleProductIds.Contains(0x22A0), "0x22A0 wired cable PID must not be in dongle set");

        byte[] req = Nova7HidProtocol.CreateStatusRequestReport(65);
        AssertEqual(65, req.Length, "Request length");
        AssertEqual((byte)0x00, req[0], "Report ID");
        AssertEqual((byte)0xB0, req[1], "Status opcode");
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
