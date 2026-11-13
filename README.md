# Arctis Nova 7 System Tray (`arctis-nova-tray`)

Lightweight, self-contained Windows 11 system tray indicator for **SteelSeries Arctis Nova 7** headsets. Built on `net9.0-windows` with pure Win32 HID and Bluetooth PnP interop (`hid.dll`, `setupapi.dll`, `cfgmgr32.dll`) and zero external NuGet dependencies.

---

## Visual Tray States

Icons render dynamically at system tray DPI (`16x16` to `64x64`) in a clean Windows 11 light-mode palette with cached `HICON` handles so background polling allocates zero GDI handles.

| Headset Base Icon | Corner Badge | Meaning |
| :--- | :--- | :--- |
| **Solid Green Headset** | *(None)* | **PC Connected** (2.4GHz wireless active), headset **Bluetooth OFF** |
| **Solid Green Headset** | **Hollow Blue Ring** | **PC Connected** (2.4GHz wireless active), headset **Bluetooth ON (Standby / Unpaired)** |
| **Solid Green Headset** | **Solid Bright Blue Badge** | **PC Connected** (2.4GHz wireless or PC Bluetooth) + **Bluetooth Connected** (phone or PC) |
| **Hollow Amber Headset** | *(None)* | **USB Dongle Plugged In**, headset **Powered OFF / Sleeping / Out of Range** |
| **Muted Slate Headset + Red Slash & Dot** | *(None)* | **Disconnected** (USB dongle unplugged and no active PC Bluetooth connection) |

### Tray Interactions & Launch Behavior
- **Immediate Visual Feedback on Launch & Re-Launch**: Launching `.\scripts\Start-ArctisNovaTray.ps1` (or `ArctisNovaTray.exe`) automatically pops open the clean Windows 11 light-mode **Status Card** in the bottom-right corner of your primary screen right after the initial hardware probe (pass `-Silent` / `--silent` to suppress the startup popup). Running it again while already running signals the active instance (`Local\ArctisNovaTray_ShowCard_v1`) to refresh and bring the Status Card to the front.
- **Pinning the Icon on Windows 11**: Windows 11 places newly registered tray icons inside the taskbar `^` overflow menu by default. Click `^` near the system clock and drag the headset icon onto the main taskbar to keep it permanently visible.
- **Hover Tooltip**: Concise summary (`< 63` chars) showing PC link state, Bluetooth state, and battery percentage / charging status (for example: `Nova 7: PC 2.4GHz | BT Connected | 84%`).
- **Left-Click**: Opens the compact light-themed Status Card near the system tray showing PC 2.4GHz link status, USB dongle presence (`VID:PID`), Bluetooth state, battery progress bar, last-updated timestamp, and a `Refresh` button.
- **Right-Click**: Opens a clean light-themed context menu with `Status summary`, `Open Status Card`, `Refresh Now`, and `Exit`.

---

## How Detection Works

1. **USB 2.4GHz Wireless Dongle (`Win32HidInterop` + `Nova7HidProtocol`)**:
   - Enumerates SteelSeries (`VID_1038`) wireless transmitter dongles across all 15 Arctis Nova 7 family PIDs (`0x22A1`, `0x227E`, `0x22A7`, `0x2298`, `0x2258`, `0x229E`, `0x22AD`, `0x22A5`, `0x22A9`, `0x2202`, `0x2206`, `0x22A4`, `0x220A`, `0x223A`, `0x227A`) on vendor control collection `UsagePage = 0xFFC0, Usage = 0x0001` (`MI_03`).
   - Opens the HID interface with `FILE_SHARE_READ | FILE_SHARE_WRITE` and non-blocking overlapped I/O (`<= 220ms` timeout, reading up to 4 frames until `0xB0` echo arrives) so it coexists peacefully alongside SteelSeries GG and Sonar.
   - Decodes the `0xB0` status report:
     - `inBuf[2] == 0x03`: Headset powered ON and linked to the 2.4GHz dongle (`0x02` = offline / powered off; ignores cached `100%` dongle battery byte when offline).
     - `inBuf[3]`: Battery level (`0-100%` direct on Gen 2 / 2026 refresh PIDs such as `0x22A1`; `0-4` steps mapped to `0-100%` on Gen 1 PIDs such as `0x2202`).
     - `inBuf[4]`: Charging indicator (`0x01`/`0x02` = charging).
     - `inBuf[7]`: Headset Bluetooth radio state on Gen 2 / 2026 refresh firmware (`0x00` = OFF, `0x03` = ON Standby, `0x02` = ON & Connected, even when Bluetooth is paired to a phone rather than the PC).
2. **Windows Native Bluetooth Fallback (`Win32BluetoothProbe`)**:
   - Queries `BTH\DEV_*` and `BTHLE\DEV_*` PnP device nodes in-process via `cfgmgr32.dll` (`DEVPKEY_Device_FriendlyName` and `DEVPKEY_Device_IsConnected`).
   - Detects when the headset is actively connected to the PC via Bluetooth even when the 2.4GHz USB dongle is unplugged or the 2.4GHz radio is turned off.
3. **Event-Driven + Low-Overhead Background Refresh**:
   - Polls on a background timer every `2.5s` and listens for `WM_DEVICECHANGE` USB HID and Bluetooth PnP events for instant hotplug updates.

---

## Quick Start & Helper Scripts

Requires the [.NET 9 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or .NET 9 SDK when building from source). Run from the unzipped release folder or repository root:

```powershell
# Start the tray app and pop open the Status Card (if already running, brings the Status Card to the front)
.\scripts\Start-ArctisNovaTray.ps1

# Start silently in the tray without opening the Status Card on launch
.\scripts\Start-ArctisNovaTray.ps1 -Silent

# Cleanly stop any running instance and start a fresh one
.\scripts\Start-ArctisNovaTray.ps1 -Restart

# Query live headset status from PowerShell as a structured object (or pass -RawJson for raw JSON text)
.\scripts\Get-ArctisNovaStatus.ps1
.\scripts\Get-ArctisNovaStatus.ps1 -RawJson

# Gracefully stop the running tray app (removes tray icon cleanly before exit)
.\scripts\Stop-ArctisNovaTray.ps1
```

> **Note on Non-Interactive / Sandboxed Shells**: Before acquiring the single-instance mutex, `ArctisNovaTray.exe` checks `FindWindowW("Shell_TrayWnd", null)` to verify an interactive Windows `explorer.exe` taskbar is reachable, returning exit code `3` otherwise so a headless background runner can never squat on the mutex without a visible tray icon. Automated tests in headless environments can pass `--allow-headless` (`-AllowHeadless`).

---

## CLI Diagnostic & Build Commands

CLI flags run before the single-instance check, so they work whether or not the tray indicator is already running. Because `ArctisNovaTray.exe` is compiled as `<OutputType>WinExe</OutputType>`, PowerShell callers invoking `.exe` directly should either use `.\scripts\Get-ArctisNovaStatus.ps1` or pipe through `| Out-String` so PowerShell waits synchronously for stdout:

```powershell
# Probe live hardware once via the PowerShell wrapper (parsed object or -RawJson)
.\scripts\Get-ArctisNovaStatus.ps1 -RawJson

# Or invoke the Release WinExe directly with pipeline synchronization
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --status-json | Out-String

# Export all 5 canonical icon states at 16x16, 32x32, and 64x64 PNGs
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --export-icons .\artifacts\icons | Out-String

# Build the entire solution offline in Release and Debug (0 warnings, 0 errors)
dotnet build .\ArctisNovaTray.sln -c Release
dotnet build .\ArctisNovaTray.sln -c Debug

# Run the deterministic unit and live-probe test suite
dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release
```

---

## License

Released under the [MIT License](LICENSE).
