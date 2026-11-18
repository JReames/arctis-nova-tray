# Arctis Nova 7 System Tray

![Arctis Nova 7 System Tray overview](docs/images/hero-banner.png)

**[Download v1.2.0](https://github.com/JReames/arctis-nova-tray/releases/latest)** · **[Website & Visual Overview](https://jreames.github.io/arctis-nova-tray/)** · **[Visual Guide](docs/VISUAL-GUIDE.md)** · **[MIT License](LICENSE)** · **Windows 11 (.NET 9, zero external packages)**

---

## Why I Built This (Works With or Without SteelSeries GG)

If you use a SteelSeries Arctis Nova 7 with its USB-C wireless receiver on Windows 11, Windows has one annoying habit: as long as that receiver stays plugged in, Windows claims your headphones are "connected" even when the headset is turned off on your desk. You only find out otherwise when audio vanishes into thin air, or when you realize the next morning that you forgot to power the headset off.

On top of that, Windows gives you no native taskbar indicator showing how much battery percentage you have left, or whether your headset is simultaneously paired to your phone over Bluetooth, without installing and opening the full SteelSeries GG suite.

**Arctis Nova 7 System Tray** puts a tiny, crisp status indicator right next to your Windows 11 clock:
- **Works with or without SteelSeries GG**: Talks directly to the USB-C wireless receiver through the built-in Windows `HidUsb` (`input.inf`) driver on clean PCs without GG installed, and shares the receiver peacefully alongside SteelSeries GG and Sonar when GG is running.
- Shows whether your headset is **actually powered on** over 2.4 GHz wireless or sitting off while the USB-C receiver stays plugged in, with adaptive dark and light taskbar contrast.
- Shows **Bluetooth status** in the corner of the icon, including when your headset is connected to your **phone** at the same time as your PC.
- Shows **live battery percentage, charging state, and hardware mic-mute status** on hover or with a single left-click.
- Includes an **opt-in Start with Windows setting** right inside the Status Card and right-click tray menu (starts quietly with `--silent` when enabled), and writes nothing to `%APPDATA%` or the registry unless you turn `Start with Windows` on (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ArctisNovaTray`).

---

## Get Running in 30 Seconds

> **Prerequisite**: [.NET 9 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (already installed on most developer machines with .NET 9).

1. **Download & unzip**: Grab **`ArctisNovaTray-v1.2.0-win-x64.zip`** from the [Releases page](https://github.com/JReames/arctis-nova-tray/releases/latest) and extract it anywhere you keep utilities.
2. **Launch it**: Double-click **`ArctisNovaTray.exe`** (or run `.\Start-ArctisNovaTray.ps1`). The light-mode Status Card pops open in the bottom-right corner right after the first hardware check so you know it is running.
3. **Pin the icon once**: Click the **`^`** hidden-icons arrow near your Windows 11 clock and drag the headset icon onto your taskbar so it stays visible all day.

Left-click the tray icon anytime to open the Status Card (`Tab`, `F5`, and `Esc` work from the keyboard) and toggle **Start with Windows** if you want it to launch automatically when you sign in. Hover for a quick text summary (`Nova 7: PC 2.4 GHz | BT Connected | 86%`), or right-click to check status, refresh, toggle startup, or exit.

---

## At-a-Glance Windows 11 Taskbar Visual Guide

Two visual layers tell the whole story on both dark and light Windows 11 taskbars: the **headset color** shows your PC connection, and the **bottom-right badge** shows Bluetooth.

![Tray Icon Visual Cheat Sheet](docs/images/visual-guide.png)

| Icon | State | What It Means |
| :---: | :--- | :--- |
| ![PC + Bluetooth Connected](docs/images/icons/pc-connected-bt-connected.png) | **PC + Bluetooth Connected** | Headset is awake on 2.4 GHz (or PC Bluetooth) and connected to your phone or PC over Bluetooth. |
| ![PC Connected, Bluetooth Waiting](docs/images/icons/pc-connected-bt-on.png) | **PC Connected, Bluetooth Waiting** | Headset is awake on 2.4 GHz. Bluetooth on the headset is switched on, waiting to connect. |
| ![PC Connected Only](docs/images/icons/pc-connected-bt-off.png) | **PC Connected Only** | Headset is awake on 2.4 GHz wireless, and Bluetooth on the headset is turned off. |
| ![Headset Off, Receiver Plugged In](docs/images/icons/dongle-only-headset-off.png) | **Headset Off, Receiver Plugged In** | USB-C receiver is plugged into your PC, **but the headset is powered off or asleep**. |
| ![Disconnected](docs/images/icons/disconnected.png) | **Disconnected** | USB-C receiver is unplugged and the headset is not connected to this PC over Bluetooth. |

Want the full tour of the popup Status Card, taskbar pinning tips, and everyday scenarios? Visit the **[Website & Visual Overview](https://jreames.github.io/arctis-nova-tray/)** or read the **[Visual Guide](docs/VISUAL-GUIDE.md)**.

---

## Supported SteelSeries Arctis Nova 7 Models

Supports all **15 wireless USB-C receiver PIDs** across the SteelSeries Arctis Nova 7 lineup (`VID_1038`), plus direct Windows Bluetooth connections when used without the USB-C receiver:

| Firmware Group | Supported Headset Models & USB PIDs | Reported Status |
| :--- | :--- | :--- |
| **Gen 2 & 2026 Refresh** | **Arctis Nova 7** (`0x22A1`), **Nova 7 Gen 2** (`0x227E`), **Nova 7P Gen 2** (`0x22A7`, `0x2298`), **Nova 7X Gen 2** (`0x2258`, `0x229E`, `0x22AD`), **Nova 7X** (`0x22A5`), **Diablo IV Edition** (`0x22A9`) | 2.4 GHz awake/off, 0-100% battery, charging, phone or PC Bluetooth state, and hardware mic mute |
| **Original Gen 1** | **Arctis Nova 7** (`0x2202`), **Nova 7P** (`0x220A`), **Nova 7X** (`0x2206`, `0x22A4`), **Diablo IV Edition** (`0x223A`), **World of Warcraft Edition** (`0x227A`) | 2.4 GHz awake/off, 0-100% battery (25% steps), charging, and PC Bluetooth fallback |

---

## Common Questions

**Does this work without SteelSeries GG installed or running?**
Yes. It queries the SteelSeries USB-C wireless receiver firmware directly over the built-in Windows `HidUsb` (`input.inf`) driver using shared Win32 HID handles (`FILE_SHARE_READ | FILE_SHARE_WRITE`). You do not need SteelSeries GG, Engine, or Sonar installed, and if you do run GG and Sonar, both apps work side by side without locking USB or audio devices.

**How do I tell if my Arctis Nova 7 is turned off when the USB-C receiver stays plugged in?**
When the headset powers off or goes to sleep, the receiver's `0xB0` status report flips its link byte from `0x03` to `0x02`. The tray icon changes immediately from solid green to a **hollow amber headset**, the tooltip updates to `Nova 7: Off or Asleep (Receiver In)`, and the Status Card hides stale cached battery numbers behind `Turn headset on to read`.

**How do I see battery percentage and phone Bluetooth status on the Windows 11 taskbar?**
Hover over the taskbar icon for an instant summary (`Nova 7: PC 2.4 GHz | BT Connected | 86%`), or left-click to open the Status Card with a color-coded battery bar. On Gen 2 and 2026 refresh hardware, the USB-C receiver reports the headset's Bluetooth radio directly, so the blue corner badge lights up even when your headset is connected to your phone.

**Can it start automatically when I sign in to Windows?**
Yes. Flip on **Start with Windows** inside the left-click Status Card (or in the right-click tray menu). That registers `ArctisNovaTray.exe --silent` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ArctisNovaTray` so it starts quietly in your tray at login. Turning the switch off removes that entry cleanly.

---

## PowerShell Helpers & Options

If you prefer launching or scripting from a terminal, the included PowerShell scripts work from both the unzipped release folder and a git checkout:

```powershell
# Launch the tray app and pop open the Status Card (or bring it to the front if already running)
.\Start-ArctisNovaTray.ps1

# Launch quietly in the tray without opening the Status Card on startup (used by Start with Windows)
.\scripts\Start-ArctisNovaTray.ps1 -Silent

# Enable or disable Windows login startup registration from PowerShell
.\scripts\Start-ArctisNovaTray.ps1 -EnableStartup
.\scripts\Start-ArctisNovaTray.ps1 -DisableStartup

# Cleanly stop a running instance and start a fresh one
.\scripts\Start-ArctisNovaTray.ps1 -Restart

# Check your headset status right in PowerShell (returns a structured object, or JSON with -RawJson)
.\scripts\Get-ArctisNovaStatus.ps1
.\scripts\Get-ArctisNovaStatus.ps1 -RawJson

# Stop the running tray app cleanly (pass -DisableStartup to also remove login auto-start)
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

# Run the 13-case unit, pixel-contrast, startup, and live-hardware test suite
dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release

# Probe live hardware JSON directly from the WinExe (pipe to Out-String so PowerShell waits)
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --status-json | Out-String

# Manage or inspect Windows login startup registration from the CLI
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --startup-status | Out-String
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --enable-startup | Out-String
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --disable-startup | Out-String

# Export all 5 canonical tray icon states at 16x16, 32x32, and 64x64 PNGs
& .\src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe --export-icons .\artifacts\icons | Out-String

# Regenerate documentation diagrams and inline 48x48 icons in docs/images/
dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release -- --generate-docs .
```

</details>

---

## License

Released under the [MIT License](LICENSE).
