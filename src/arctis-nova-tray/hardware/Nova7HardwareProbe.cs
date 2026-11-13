using ArctisNovaTray.Models;
using ArctisNovaTray.Protocol;

namespace ArctisNovaTray.Hardware;

public sealed class Nova7HardwareProbe : IHeadsetProbe
{
    private const int HidQueryTimeoutMs = 220;

    public HeadsetSnapshot Probe()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        IReadOnlyList<HidDeviceCandidate> novaHidDevices = Win32HidInterop.EnumerateVendorHidDevices(
            Nova7HidProtocol.VendorId,
            Nova7HidProtocol.SupportedDongleProductIds);

        HidDeviceCandidate? controlDevice = novaHidDevices.FirstOrDefault(
            d => d.UsagePage == Nova7HidProtocol.ControlUsagePage && d.Usage == Nova7HidProtocol.ControlUsage);

        HidDeviceCandidate? anyDongleInterface = controlDevice ?? novaHidDevices.FirstOrDefault();
        bool donglePresent = anyDongleInterface is not null;

        Nova7DecodedHidReport? decodedReport = null;
        string? diagnosticNote = null;

        if (controlDevice is not null)
        {
            byte[] request = Nova7HidProtocol.CreateStatusRequestReport(
                controlDevice.OutputReportByteLength > 0
                    ? controlDevice.OutputReportByteLength
                    : Nova7HidProtocol.DefaultReportLength);

            if (Win32HidInterop.TryRequestReport(
                controlDevice,
                request,
                Nova7HidProtocol.StatusOpcode,
                HidQueryTimeoutMs,
                out byte[]? responseBytes,
                out string? hidError) &&
                responseBytes is not null)
            {
                if (!Nova7HidProtocol.TryParseStatusReport(responseBytes, controlDevice.ProductId, out decodedReport))
                {
                    diagnosticNote = "Received unexpected HID report format on 0xFFC0.";
                }
            }
            else
            {
                diagnosticNote = hidError;
            }
        }
        else if (donglePresent)
        {
            diagnosticNote = "Nova 7 dongle detected, but control HID collection (0xFFC0:0x0001) was not found.";
        }

        WindowsBluetoothHeadsetStatus winBt = Win32BluetoothProbe.QueryHeadsetBluetooth();

        string? dongleProductName = decodedReport?.ProductName
            ?? anyDongleInterface?.ProductString
            ?? (anyDongleInterface is not null && Nova7HidProtocol.TryGetModelSpec(anyDongleInterface.ProductId, out Nova7ModelSpec spec)
                ? spec.DisplayName
                : null);

        string? dongleVidPid = anyDongleInterface is not null
            ? $"{anyDongleInterface.VendorId:X4}:{anyDongleInterface.ProductId:X4}"
            : null;

        return CombineStates(
            now,
            donglePresent,
            dongleProductName,
            dongleVidPid,
            decodedReport,
            winBt,
            diagnosticNote);
    }

    public static HeadsetSnapshot CombineStates(
        DateTimeOffset timestamp,
        bool donglePresent,
        string? dongleProductName,
        string? dongleVidPid,
        Nova7DecodedHidReport? hidReport,
        WindowsBluetoothHeadsetStatus winBt,
        string? diagnosticNote = null)
    {
        bool online24Ghz = donglePresent && hidReport is { HeadsetOnline24Ghz: true };

        PcConnectionState pcState;
        BluetoothState btState;
        BluetoothSource btSource;

        if (online24Ghz)
        {
            pcState = PcConnectionState.Connected24Ghz;

            if (winBt.IsConnected)
            {
                btState = BluetoothState.Connected;
                btSource = hidReport?.HidBluetoothState == BluetoothState.Connected
                    ? BluetoothSource.Both
                    : BluetoothSource.WindowsBluetooth;
            }
            else if (hidReport?.HidBluetoothState is BluetoothState hidBt)
            {
                btState = hidBt;
                btSource = BluetoothSource.DongleHidReport;
            }
            else
            {
                btState = BluetoothState.Off;
                btSource = BluetoothSource.None;
            }
        }
        else if (winBt.IsConnected)
        {
            pcState = PcConnectionState.ConnectedBluetooth;
            btState = BluetoothState.Connected;
            btSource = BluetoothSource.WindowsBluetooth;
        }
        else if (donglePresent)
        {
            pcState = PcConnectionState.DongleOnlyHeadsetOff;
            btState = BluetoothState.Off;
            btSource = BluetoothSource.None;
        }
        else
        {
            pcState = PcConnectionState.Disconnected;
            btState = BluetoothState.Off;
            btSource = BluetoothSource.None;
        }

        return new HeadsetSnapshot
        {
            Timestamp = timestamp,
            PcConnection = pcState,
            Bluetooth = btState,
            BluetoothDetectionSource = btSource,
            DonglePresent = donglePresent,
            DongleProductName = dongleProductName,
            DongleVidPid = dongleVidPid,
            HeadsetOnline24Ghz = online24Ghz,
            WindowsBluetoothPaired = winBt.Paired,
            WindowsBluetoothConnected = winBt.IsConnected,
            WindowsBluetoothDeviceName = winBt.DeviceName,
            BatteryPercent = online24Ghz ? hidReport?.BatteryPercent : null,
            Charging = online24Ghz ? (hidReport?.Charging ?? BatteryChargingState.Unknown) : BatteryChargingState.Disconnected,
            MicMuted = online24Ghz ? hidReport?.MicMuted : null,
            RawHidStatusHex = hidReport?.RawHexSummary,
            DiagnosticNote = diagnosticNote
        };
    }
}
