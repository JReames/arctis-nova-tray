using System.Text.Json.Serialization;

namespace ArctisNovaTray.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PcConnectionState
{
    Connected24Ghz,
    ConnectedBluetooth,
    DongleOnlyHeadsetOff,
    Disconnected
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BluetoothState
{
    Off,
    Standby,
    Connected
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BluetoothSource
{
    None,
    DongleHidReport,
    WindowsBluetooth,
    Both
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BatteryChargingState
{
    Unknown,
    Discharging,
    Charging,
    Disconnected
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HeadsetBaseVisual
{
    PcConnected,
    DongleOnlyHeadsetOff,
    Disconnected
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BluetoothBadgeVisual
{
    Off,
    Standby,
    Connected
}

public readonly record struct TrayVisualKey(
    HeadsetBaseVisual Base,
    BluetoothBadgeVisual Badge);

public sealed record HeadsetSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required PcConnectionState PcConnection { get; init; }
    public required BluetoothState Bluetooth { get; init; }
    public BluetoothSource BluetoothDetectionSource { get; init; } = BluetoothSource.None;

    public bool DonglePresent { get; init; }
    public string? DongleProductName { get; init; }
    public string? DongleVidPid { get; init; }
    public string? VerificationTier { get; init; }
    public bool HeadsetOnline24Ghz { get; init; }

    public bool WindowsBluetoothPaired { get; init; }
    public bool WindowsBluetoothConnected { get; init; }
    public string? WindowsBluetoothDeviceName { get; init; }

    public int? BatteryPercent { get; init; }
    public BatteryChargingState Charging { get; init; } = BatteryChargingState.Unknown;
    public bool? MicMuted { get; init; }

    public string? RawHidStatusHex { get; init; }
    public string? DiagnosticNote { get; init; }

    public TrayVisualKey VisualState => new(
        PcConnection switch
        {
            PcConnectionState.Connected24Ghz => HeadsetBaseVisual.PcConnected,
            PcConnectionState.ConnectedBluetooth => HeadsetBaseVisual.PcConnected,
            PcConnectionState.DongleOnlyHeadsetOff => HeadsetBaseVisual.DongleOnlyHeadsetOff,
            _ => HeadsetBaseVisual.Disconnected
        },
        Bluetooth switch
        {
            BluetoothState.Connected => BluetoothBadgeVisual.Connected,
            BluetoothState.Standby => BluetoothBadgeVisual.Standby,
            _ => BluetoothBadgeVisual.Off
        });

    public string DeviceDisplayName =>
        !string.IsNullOrWhiteSpace(DongleProductName)
            ? DongleProductName
            : !string.IsNullOrWhiteSpace(WindowsBluetoothDeviceName)
                ? WindowsBluetoothDeviceName
                : "Arctis Nova";

    public static HeadsetSnapshot CreateInitial() => new()
    {
        Timestamp = DateTimeOffset.Now,
        PcConnection = PcConnectionState.Disconnected,
        Bluetooth = BluetoothState.Off,
        DiagnosticNote = "Initializing hardware probe..."
    };
}
