# Arctis Nova 7 System Tray

![Arctis Nova 7 System Tray overview](docs/images/hero-banner.png)

**[Download v1.1.0](https://github.com/JReames/arctis-nova-tray/releases/latest)** · **[Visual Guide](docs/VISUAL-GUIDE.md)** · **[MIT License](LICENSE)** · **Windows 11 (.NET 9, zero external packages)**

---

## Why I Built This

If you use a SteelSeries Arctis Nova 7 with its USB-C wireless receiver, Windows has one annoying habit: as long as that receiver is plugged in, Windows claims your headphones are "connected" even when the headset is turned off on your desk. You only find out otherwise when audio vanishes into thin air, or when you realize the next morning that you forgot to power the headset off.

On top of that, there is no native taskbar indicator showing whether your headset is simultaneously paired to your phone over Bluetooth, or how much battery you have left, without opening the full SteelSeries GG window.

**Arctis Nova 7 System Tray** puts a tiny, crisp indicator right next to your Windows 11 clock:
- Shows whether your headset is **actually powered on** or just sitting off while the USB receiver stays plugged in, with adaptive dark and light taskbar contrast.
- Shows **Bluetooth status** in the corner of the icon, including when your headset is connected to your **phone** at the same time as your PC.
- Shows **live battery percentage, charging state, and hardware mic-mute status** on hover or with a single left-click.
- Runs alongside **SteelSeries GG and Sonar** without locking audio or USB devices, and writes nothing to `%APPDATA%`, the registry, or Windows Startup folders.

---

## Get Running in 30 Seconds

> **Prerequisite**: [.NET 9 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (already installed on most developer machines with .NET 9).

1. **Download & unzip**: Grab **`ArctisNovaTray-v1.1.0-win-x64.zip`** from the [Releases page](https://github.com/JReames/arctis-nova-tray/releases/latest) and extract it anywhere you keep utilities.
2. **Launch it**: Double-click **`ArctisNovaTray.exe`** (or run `.\Start-ArctisNovaTray.ps1`). The light-mode Status Card pops open in the bottom-right corner right after the first hardware check so you know it is running.
3. **Pin the icon once**: Click the **`^`** hidden-icons arrow near your Windows 11 clock and drag the headset icon onto your taskbar so it stays visible all day.

Left-click the tray icon anytime to open the Status Card (`Tab`, `F5`, and `Esc` work from the keyboard), hover for a quick text summary (`Nova 7: PC 2.4GHz | BT Connected | 86%`), or right-click to check status, refresh, or exit.

---

## At-a-Glance Visual Guide

Two visual layers tell the whole story on both dark and light Windows 11 taskbars: the **headset color** shows your PC connection, and the **bottom-right badge** shows Bluetooth.

![Tray Icon Visual Cheat Sheet](docs/images/visual-guide.png)

| Icon | State | What It Means |
| :---: | :--- | :--- |
| ![PC + Bluetooth Connected](docs/images/icons/pc-connected-bt-connected.png) | **PC + Bluetooth Connected** | Headset is awake on 2.4 GHz (or PC Bluetooth) and connected to your phone or PC over Bluetooth. |
| ![PC Connected, Bluetooth Waiting](docs/images/icons/pc-connected-bt-on.png) | **PC Connected, Bluetooth Waiting** | Headset is awake on 2.4 GHz. Bluetooth on the headset is switched on, waiting to connect. |
| ![PC Connected Only](docs/images/icons/pc-connected-bt-off.png) | **PC Connected Only** | Headset is awake on 2.4 GHz wireless, and Bluetooth on the headset is turned off. |
| ![Headset Off, Receiver Plugged In](docs/images/icons/dongle-only-headset-off.png) | **Headset Off, Receiver Plugged In** | USB-C receiver is plugged into your PC, **but the headset is powered off or asleep**. |
| ![Disconnected](docs/images/icons/disconnected.png) | **Disconnected** | USB-C receiver is unplugged and the headset is not connected to this PC over Bluetooth. |

Want the full tour of the popup Status Card, taskbar pinning tips, and everyday scenarios? Read the **[Visual Guide](docs/VISUAL-GUIDE.md)**.

---

## PowerShell Helpers & Options

If you prefer launching or scripting from a terminal, the included PowerShell scripts work from both the unzipped release folder and a git checkout:

```powershell
# Launch the tray app and pop open the Status Card (or bring it to the front if already running)
.\Start-ArctisNovaTray.ps1

# Launch quietly in the tray without opening the Status Card on startup (handy for login scripts)
.\scripts\Start-ArctisNovaTray.ps1 -Silent

# Cleanly stop a running instance and start a fresh one
.\scripts\Start-ArctisNovaTray.ps1 -Restart

# Check your headset status right in PowerShell (returns a structured object, or JSON with -RawJson)
.\scripts\Get-ArctisNovaStatus.ps1
.\scripts\Get-ArctisNovaStatus.ps1 -RawJson

# Stop the running tray app cleanly (removes the tray icon before exiting)
.\scripts\Stop-ArctisNovaTray.ps1
```

---

## How It Works Under the Hood

This app is plain C# (`net9.0-windows` WinForms + GDI+) talking directly to Windows (`hid.dll`, `setupapi.dll`, `cfgmgr32.dll`, `kernel32.dll`, `user32.dll`) with **zero NuGet dependencies**.

<details>
<summary><strong>USB HID protocol, Bluetooth fallback, CLI flags, and building from source</strong></summary>

### 1. USB-C 2.4GHz Receiver Probing (`Win32HidInterop` + `Nova7HidProtocol`)
- Scans SteelSeries (`VID_1038`) wireless receivers across all **15 Arctis Nova 7 family PIDs** (`0x22A1`, `0x227E`, `0x22A7`, `0x2298`, `0x2258`, `0x229E`, `0x22AD`, `0x22A5`, `0x22A9`, `0x2202`, `0x2206`, `0x22A4`, `0x220A`, `0x223A`, `0x227A`) on vendor control collection `UsagePage = 0xFFC0, Usage = 0x0001` (`MI_03`).
- Opens the HID path with `FILE_SHARE_READ | FILE_SHARE_WRITE` and short overlapped I/O timeouts (`<= 220ms`, reading up to 4 frames until the `0xB0` response arrives) so it never blocks or fights with SteelSeries GG.
- Sends the 65-byte `[0x00, 0xB0, ...]` status request and reads:
  - `inBuf[2]`: `0x03` when the headset is powered on and linked over 2.4GHz; `0x02` when powered off or asleep (suppressing cached `100%` receiver bytes when offline).
  - `inBuf[3]`: Battery level (`0-100%` direct on Gen 2 / 2026 refresh hardware such as `0x22A1`; `0-4` steps mapped to `0-100%` on original Nova 7 PIDs such as `0x2202`).
  - `inBuf[4]`: Charging flag (`0x01` / `0x02` = charging).
  - `inBuf[7]`: Headset Bluetooth radio state on Gen 2 / 2026 refresh firmware (`0x00` = OFF, `0x03` = ON Waiting, `0x02` = ON & Connected, even when paired to a phone).
  - `inBuf[10]`: Hardware boom-mic mute flag on Gen 2 / 2026 refresh firmware (`0x01` = muted, shown as an inline `MIC MUTED` pill in the Status Card).

### 2. Native Windows Bluetooth Fallback (`Win32BluetoothProbe`)
- Queries `BTH\DEV_*` and `BTHLE\DEV_*` PnP device nodes in-process through `cfgmgr32.dll` (`DEVPKEY_Device_FriendlyName` and `DEVPKEY_Device_IsConnected`).
- Tracks direct PC Bluetooth connections even when the 2.4GHz USB-C receiver is unplugged.

### 3. Low-Overhead Refresh & Safety Guards
- Polls in the background every `2.5s` and reacts immediately to `WM_DEVICECHANGE` USB and Bluetooth hotplug events.
- Caches rendered `HICON` handles per visual state and destroys raw `Bitmap.GetHicon()` handles immediately so background polling leaks zero GDI objects.
- Checks `FindWindowW("Shell_TrayWnd", null)` before acquiring `Local\ArctisNovaTray_SingleInstance_v1` (returning exit code `3` if no interactive desktop taskbar exists, unless `--allow-headless` is passed for automated tests).

### 4. CLI Diagnostic Flags & Building from Source

```powershell
# Build Debug and Release completely offline (0 warnings, 0 errors)
dotnet build .\ArctisNovaTray.sln -c Debug
dotnet build .\ArctisNovaTray.sln -c Release

# Run the 11-case unit, pixel-contrast, and live-hardware test suite
dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release

# Probe live hardware JSON directly from the WinExe (pipe to Out-String so PowerShell waits)
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --status-json | Out-String

# Export all 5 canonical tray icon states at 16x16, 32x32, and 64x64 PNGs
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --export-icons .\artifacts\icons | Out-String

# Regenerate documentation diagrams and inline 48x48 icons in docs/images/
dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release -- --generate-docs .
```

</details>

---

## License

Released under the [MIT License](LICENSE).
