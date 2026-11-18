---
name: Hardware Verification Report
about: Share --status-json output for a Community-Spec SteelSeries Arctis Nova wireless PID so it can be promoted to Hardware-Verified
title: "[Hardware Verification] Arctis Nova <Model> (1038:<PID>)"
labels: hardware-verification
---

## Headset & Receiver Details

- **Headset model name** (for example, Arctis Nova 5, Nova 5X, Nova 3P Wireless, Nova Pro Omni, or Arctis GameBuds):
- **USB Receiver VID:PID** (shown in `dongleVidPid`, such as `1038:2232`):
- **Windows 11 version**:
- **Was SteelSeries GG running during this check?** (Yes / No / Tested both):

## 1. JSON Output With Headset Powered ON

Turn your headset (or earbuds) on so audio works over 2.4 GHz wireless, then run:

```powershell
& .\ArctisNovaTray.exe --status-json | Out-String
```

*(Or `.\scripts\Get-ArctisNovaStatus.ps1 -RawJson` if using the helper scripts.)*

Paste the JSON output below:

```json

```

## 2. JSON Output With Headset Powered OFF (Receiver Still Plugged In)

Power the headset off (or place both GameBuds into their charging case and close the lid) while leaving the USB receiver plugged into your PC, wait 5 seconds, and run the same command again:

```json

```

## 3. Observed Tray Behavior Checklist

- [ ] Tray icon turns **solid green** when the headset is powered on over 2.4 GHz wireless
- [ ] Tray icon switches to **hollow amber** when the headset is powered off with the USB receiver still plugged in
- [ ] Battery percentage in the Status Card matches the approximate charge level of the headset
- [ ] Charging state updates when a USB charging cable is plugged into the headset (if applicable)
- [ ] Bluetooth corner badge or PC Bluetooth fallback behaves as expected (note any differences below)

## Notes

*(Optional: mention firmware version from SteelSeries GG if known, or anything unexpected you noticed.)*
