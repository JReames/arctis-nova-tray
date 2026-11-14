using ArctisNovaTray.Models;

namespace ArctisNovaTray.Rendering;

public static class TooltipFormatter
{
    /// <summary>
    /// Strict maximum character count (< 63 chars) so WinForms NotifyIcon.Text never throws.
    /// </summary>
    public const int MaxTooltipLength = 62;

    public static string Format(HeadsetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string pcPart = snapshot.PcConnection switch
        {
            PcConnectionState.Connected24Ghz => "PC 2.4 GHz",
            PcConnectionState.ConnectedBluetooth => "PC BT",
            PcConnectionState.DongleOnlyHeadsetOff => "Headset Off (Receiver)",
            _ => "Disconnected"
        };

        string btPart = snapshot.Bluetooth switch
        {
            BluetoothState.Connected => "BT Connected",
            BluetoothState.Standby => "BT Waiting",
            _ => "BT Off"
        };

        string? batteryPart = FormatBatteryCompact(snapshot);

        string raw = snapshot.PcConnection == PcConnectionState.Disconnected && snapshot.Bluetooth == BluetoothState.Off
            ? "Nova 7: Disconnected (No Receiver / BT)"
            : snapshot.PcConnection == PcConnectionState.DongleOnlyHeadsetOff && snapshot.Bluetooth == BluetoothState.Off
                ? "Nova 7: Off or Asleep (Receiver In)"
                : batteryPart is not null
                    ? $"Nova 7: {pcPart} | {btPart} | {batteryPart}"
                    : $"Nova 7: {pcPart} | {btPart}";

        return TruncateSafe(raw);
    }

    public static string FormatMenuSummary(HeadsetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.PcConnection == PcConnectionState.DongleOnlyHeadsetOff && snapshot.Bluetooth == BluetoothState.Off)
        {
            return "Headset Off or Asleep · Receiver In";
        }

        if (snapshot.PcConnection == PcConnectionState.Disconnected && snapshot.Bluetooth == BluetoothState.Off)
        {
            return "Headset Disconnected · Unplugged";
        }

        string pcPart = snapshot.PcConnection switch
        {
            PcConnectionState.Connected24Ghz => "Connected (2.4 GHz)",
            PcConnectionState.ConnectedBluetooth => "Connected (Bluetooth)",
            PcConnectionState.DongleOnlyHeadsetOff => "Headset Off",
            _ => "Disconnected"
        };

        string btPart = snapshot.Bluetooth switch
        {
            BluetoothState.Connected => "BT Connected",
            BluetoothState.Standby => "BT On (Waiting)",
            _ => "BT Off"
        };

        string? battery = FormatBatteryReadable(snapshot);
        return battery is not null
            ? $"{pcPart} · {btPart} · {battery}"
            : $"{pcPart} · {btPart}";
    }

    public static string TruncateSafe(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "Arctis Nova 7";
        }

        if (text.Length <= MaxTooltipLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, MaxTooltipLength - 3), "...");
    }

    private static string? FormatBatteryCompact(HeadsetSnapshot snapshot)
    {
        if (snapshot.BatteryPercent is not int pct)
        {
            return snapshot.Charging == BatteryChargingState.Charging ? "Charging" : null;
        }

        int clamped = Math.Clamp(pct, 0, 100);
        return snapshot.Charging == BatteryChargingState.Charging
            ? $"{clamped}%+"
            : $"{clamped}%";
    }

    private static string? FormatBatteryReadable(HeadsetSnapshot snapshot)
    {
        if (snapshot.BatteryPercent is not int pct)
        {
            return snapshot.Charging == BatteryChargingState.Charging ? "Charging" : null;
        }

        int clamped = Math.Clamp(pct, 0, 100);
        return snapshot.Charging == BatteryChargingState.Charging
            ? $"{clamped}% (Charging)"
            : $"{clamped}%";
    }
}
