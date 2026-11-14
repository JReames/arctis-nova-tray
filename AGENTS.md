# Arctis Nova 7 System Tray (`arctis-nova-tray`)

## 1. Context and Scope
- **Repository Root**: Root directory containing `ArctisNovaTray.sln`
- **Deliverable**: Standalone Windows 11 system tray application (`ArctisNovaTray.exe`, targeting `net9.0-windows`) in clean Light Mode that displays real-time PC 2.4GHz wireless connection status, USB dongle presence, headset Bluetooth state, and battery level for SteelSeries Arctis Nova 7 headsets.

## 2. Boundaries and Sources of Truth
- **Sources of Truth**:
  - HID vendor protocol constants and byte decoding: `src/arctis-nova-tray/protocol/Nova7HidProtocol.cs`
  - Win32 HID and Windows Bluetooth PnP probing: `src/arctis-nova-tray/hardware/`
  - Dark- and light-taskbar adaptive DPI-aware GDI+ icon rendering and `HICON` lifecycle: `src/arctis-nova-tray/rendering/`
  - WCAG AA/AAA light-mode status flyout card (`436x392` logical) and tray context menu: `src/arctis-nova-tray/ui/`
  - Human-facing usage, visual cheat sheet, and CLI documentation: `README.md` and `docs/VISUAL-GUIDE.md`

## 3. Behavior-Changing Local Rules
1. **Zero External Package Downloads**: Both `.csproj` files must contain zero `<PackageReference>` entries and build completely offline against standard `.NET 9` Windows Desktop SDK targeting packs (`net9.0-windows`). Never add third-party NuGet package dependencies without explicit approval.
2. **System Hygiene**: Never write files, shortcuts, or registry keys to system directories, `%APPDATA%`, `%LOCALAPPDATA%`, or Windows Startup folders. Keep any optional local runtime diagnostics inside `./logs/` (gitignored).
3. **Peaceful Hardware Coexistence**: Always open vendor HID interfaces with `FILE_SHARE_READ | FILE_SHARE_WRITE` and overlapped I/O read timeouts `<= 250ms` (`CancelIoEx` on timeout) so SteelSeries GG and audio endpoints are never blocked, locked out, or stalled on the UI thread.
4. **Zero GDI Leaks and Tooltip Guardrails**:
   - Always destroy raw `Bitmap.GetHicon()` handles immediately after cloning into a managed `Icon`, and cache `Icon` instances per `(HeadsetBaseVisual, BluetoothBadgeVisual, SizePx)` in `TrayIconCache`.
   - Always format tray hover text through `TooltipFormatter.Format`, enforcing `< 63` characters (`<= 62` chars) so `NotifyIcon.Text` never throws.
5. **Interactive Taskbar Guard and Non-Squatting Discipline**:
   - `Program.Main` checks `FindWindowW("Shell_TrayWnd", null)` before acquiring `Local\ArctisNovaTray_SingleInstance_v1` (exiting with code `3` when no interactive taskbar is reachable unless `--allow-headless` is passed).
   - Automated or sandboxed verification sessions that launch `ArctisNovaTray.exe` with `--allow-headless` (`-AllowHeadless`) must run `.\scripts\Stop-ArctisNovaTray.ps1` before finishing so no headless process holds `Local\ArctisNovaTray_SingleInstance_v1`.
6. **Naming Discipline**: Keep all directory names strictly lowercase kebab-case (`src/arctis-nova-tray/`, `tests/arctis-nova-tray-tests/`, `scripts/`, `docs/images/icons/`) and keep PowerShell scripts in approved `Verb-Noun.ps1` form (`Start-ArctisNovaTray.ps1`, `Stop-ArctisNovaTray.ps1`, `Get-ArctisNovaStatus.ps1`).

## 4. Required Verification
Run from the repository root:

- **Offline Build in Release and Debug (0 warnings, 0 errors)**:
  ```powershell
  dotnet build .\ArctisNovaTray.sln -c Release
  dotnet build .\ArctisNovaTray.sln -c Debug
  ```
- **Deterministic Unit & Hardware Probe Tests**:
  ```powershell
  dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release
  ```
- **Live Hardware Status Probe**:
  ```powershell
  .\scripts\Get-ArctisNovaStatus.ps1
  .\scripts\Get-ArctisNovaStatus.ps1 -RawJson
  ```
  *(Note: Because `ArctisNovaTray.exe` uses `<OutputType>WinExe</OutputType>`, prefer `.\scripts\Get-ArctisNovaStatus.ps1` or pipe `& .\ArctisNovaTray.exe --status-json | Out-String` so PowerShell waits synchronously for stdout.)*
- **Visual Icon & Documentation Asset Export (required after `rendering/` or `ui/` visual changes)**:
  ```powershell
  dotnet run --project .\src\arctis-nova-tray\ArctisNovaTray.csproj -c Release -- --export-icons .\artifacts\icons
  dotnet run --project .\tests\arctis-nova-tray-tests\ArctisNovaTray.Tests.csproj -c Release -- --generate-docs .
  ```
- **Live Tray Lifecycle & Single-Instance Check**:
  ```powershell
  # From an interactive Windows desktop terminal (pops open the light-mode Status Card on launch and re-launch):
  .\scripts\Start-ArctisNovaTray.ps1
  .\scripts\Start-ArctisNovaTray.ps1 -Restart

  # From a headless/sandboxed test runner (always stop afterward so mutex is released):
  .\scripts\Start-ArctisNovaTray.ps1 -AllowHeadless -Silent
  .\scripts\Stop-ArctisNovaTray.ps1
  ```

## 5. Routing and Project Map
- `ArctisNovaTray.sln`: Solution building both the WinForms tray executable and the zero-NuGet test runner.
- `README.md`: Overview, quick-start steps, visual cheat sheet, PowerShell helpers, and technical reference.
- `docs/VISUAL-GUIDE.md` & `docs/images/`: Annotated Status Card tour, taskbar pinning guide, everyday scenarios, and generated high-DPI diagrams/icons.
- `LICENSE`: MIT license.
- `scripts/Start-ArctisNovaTray.ps1`: Detached launcher supporting both packaged release layouts and source checkouts (`-Restart`, `-Silent`, `-AllowHeadless`, `-Build`), bringing the light-mode Status Card to the front when already running.
- `scripts/Stop-ArctisNovaTray.ps1`: Graceful tray shutdown helper (signals `Local\ArctisNovaTray_Shutdown_v1` before process cleanup so no ghost tray icon remains).
- `scripts/Get-ArctisNovaStatus.ps1`: Synchronous PowerShell status probe wrapper returning a parsed status object by default or raw JSON with `-RawJson`.
- `src/arctis-nova-tray/`:
  - `Program.cs`: CLI flag handling (`--status-json`, `--export-icons`, `--help`), taskbar guard, single-instance `Mutex`, and WinForms bootstrap.
  - `models/`: `HeadsetSnapshot`, visual state enums, and `IHeadsetProbe`.
  - `protocol/`: `Nova7HidProtocol` VID/PID table, status request framing, and HID report byte parser.
  - `hardware/`: `Win32HidInterop`, `Win32BluetoothProbe`, and `Nova7HardwareProbe`.
  - `rendering/`: `TrayIconRenderer`, `TrayIconCache`, and `TooltipFormatter`.
  - `ui/`: `TrayApplicationContext`, `StatusFlyoutForm` (Light Mode), and `DeviceChangeMonitor` (`WM_DEVICECHANGE`).
- `tests/arctis-nova-tray-tests/`:
  - `Program.cs`: Deterministic test runner covering HID report parsing across all Nova 7 states, tooltip bounds, icon rendering, PNG export, tray lifecycle guards, and live probe execution.
  - `DocAssetGenerator.cs`: High-DPI documentation diagram and icon exporter (`--generate-docs <repoRoot>`), keeping documentation rendering out of the shipped tray executable.

## Last Updated
2026-11-13
