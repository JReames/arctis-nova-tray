using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ArctisNovaTray.Hardware;
using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;
using ArctisNovaTray.Services;
using ArctisNovaTray.Ui;

namespace ArctisNovaTray;

public static partial class Program
{
    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    public const int NoTaskbarExitCode = 3;
    public const string SingleInstanceMutexName = @"Local\ArctisNovaTray_SingleInstance_v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [STAThread]
    public static int Main(string[] args)
    {
        if (!TryParseTrayStartupFlags(args, out bool silentStartup, out bool allowHeadless))
        {
            return RunCliMode(args);
        }

        // Guard BEFORE acquiring the single-instance mutex so a non-interactive desktop or
        // sandboxed runner where Shell_TrayWnd is unreachable can never squat on the mutex.
        if (!allowHeadless && !HasInteractiveTaskbar())
        {
            EnsureConsoleAttached();
            using Stream stderrStream = Console.OpenStandardError();
            using var stderr = new StreamWriter(stderrStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            stderr.WriteLine(
                "Error: No interactive Windows taskbar (explorer.exe Shell_TrayWnd) is reachable in this desktop or sandbox. " +
                "Launch ArctisNovaTray from an interactive Windows desktop terminal, or pass --allow-headless for automated tests.");
            return NoTaskbarExitCode;
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another tray instance is already running: signal it to pop open and refresh the Status Card.
            if (!silentStartup &&
                EventWaitHandle.TryOpenExisting(TrayApplicationContext.ShowCardEventName, out EventWaitHandle? showEvent))
            {
                using (showEvent)
                {
                    _ = showEvent.Set();
                }
            }

            return 0;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            using var context = new TrayApplicationContext(
                new Nova7HardwareProbe(),
                showCardOnStartup: !silentStartup,
                startupService: new RegistryStartupRegistrationService());
            Application.Run(context);
            return 0;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static bool TryParseTrayStartupFlags(
        string[] args,
        out bool silentStartup,
        out bool allowHeadless)
    {
        silentStartup = false;
        allowHeadless = false;

        foreach (string arg in args)
        {
            if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase))
            {
                silentStartup = true;
            }
            else if (string.Equals(arg, "--allow-headless", StringComparison.OrdinalIgnoreCase))
            {
                allowHeadless = true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasInteractiveTaskbar() =>
        FindWindowW("Shell_TrayWnd", null) != IntPtr.Zero;

    private static int RunCliMode(string[] args)
    {
        // Attach to parent console BEFORE touching any System.Console property so System.Console
        // never caches a null stream handle on WinExe startup when stdout is not redirected.
        EnsureConsoleAttached();

        using Stream stdoutStream = Console.OpenStandardOutput();
        using var stdout = new StreamWriter(stdoutStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        using Stream stderrStream = Console.OpenStandardError();
        using var stderr = new StreamWriter(stderrStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--status-json", StringComparison.OrdinalIgnoreCase))
            {
                var probe = new Nova7HardwareProbe();
                HeadsetSnapshot snapshot = probe.Probe();
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (string.Equals(arg, "--export-icons", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    stderr.WriteLine("Missing <outputDir> argument for --export-icons.");
                    return 2;
                }

                string outDir = Path.GetFullPath(args[i + 1]);
                IReadOnlyList<string> written = TrayIconRenderer.ExportAllStates(outDir);
                string json = JsonSerializer.Serialize(
                    new { outputDirectory = outDir, count = written.Count, files = written },
                    JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (arg.StartsWith("--export-icons=", StringComparison.OrdinalIgnoreCase))
            {
                string outDir = Path.GetFullPath(arg["--export-icons=".Length..]);
                IReadOnlyList<string> written = TrayIconRenderer.ExportAllStates(outDir);
                string json = JsonSerializer.Serialize(
                    new { outputDirectory = outDir, count = written.Count, files = written },
                    JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (string.Equals(arg, "--enable-startup", StringComparison.OrdinalIgnoreCase))
            {
                var startupService = new RegistryStartupRegistrationService();
                startupService.SetStartupEnabled(true);
                string json = JsonSerializer.Serialize(
                    new { startupEnabled = true, command = startupService.BuildCommand() },
                    JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (string.Equals(arg, "--disable-startup", StringComparison.OrdinalIgnoreCase))
            {
                var startupService = new RegistryStartupRegistrationService();
                startupService.SetStartupEnabled(false);
                string json = JsonSerializer.Serialize(
                    new { startupEnabled = false },
                    JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (string.Equals(arg, "--startup-status", StringComparison.OrdinalIgnoreCase))
            {
                var startupService = new RegistryStartupRegistrationService();
                bool enabled = startupService.IsStartupEnabled;
                string json = JsonSerializer.Serialize(
                    new { startupEnabled = enabled, command = enabled ? startupService.BuildCommand() : null },
                    JsonOptions);
                stdout.WriteLine(json);
                return 0;
            }

            if (arg is "--help" or "-h" or "/?")
            {
                stdout.WriteLine("ArctisNovaTray - SteelSeries Arctis Nova Wireless System Tray Indicator");
                stdout.WriteLine("Usage:");
                stdout.WriteLine("  ArctisNovaTray.exe                        Start tray indicator and open Status Card");
                stdout.WriteLine("  ArctisNovaTray.exe --silent               Start tray indicator without opening Status Card");
                stdout.WriteLine("  ArctisNovaTray.exe --allow-headless       Bypass interactive taskbar guard (automated tests)");
                stdout.WriteLine("  ArctisNovaTray.exe --status-json          Probe hardware once and print JSON");
                stdout.WriteLine("  ArctisNovaTray.exe --enable-startup       Register Windows login startup (--silent)");
                stdout.WriteLine("  ArctisNovaTray.exe --disable-startup      Remove Windows login startup registration");
                stdout.WriteLine("  ArctisNovaTray.exe --startup-status       Print Windows startup registration status JSON");
                stdout.WriteLine("  ArctisNovaTray.exe --export-icons <dir>   Export 16x16, 32x32, 64x64 PNG icons");
                return 0;
            }
        }

        stderr.WriteLine($"Unknown CLI arguments: {string.Join(' ', args)}");
        return 2;
    }

    private static void EnsureConsoleAttached()
    {
        IntPtr outHandle = GetStdHandle(STD_OUTPUT_HANDLE);
        IntPtr errHandle = GetStdHandle(STD_ERROR_HANDLE);
        bool hasStdout = outHandle != IntPtr.Zero && outHandle != new IntPtr(-1);
        bool hasStderr = errHandle != IntPtr.Zero && errHandle != new IntPtr(-1);

        if (!hasStdout && !hasStderr)
        {
            _ = AttachConsole(ATTACH_PARENT_PROCESS);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);
}
