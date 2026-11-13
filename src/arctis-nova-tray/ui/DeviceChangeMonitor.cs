using System.Runtime.InteropServices;

namespace ArctisNovaTray.Ui;

/// <summary>
/// Listens for USB HID arrival/removal and Bluetooth PnP tree changes via WM_DEVICECHANGE
/// so tray status refreshes immediately on hotplug events without polling delay.
/// </summary>
public sealed partial class DeviceChangeMonitor : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    private IntPtr _notificationHandle;
    private bool _disposed;

    public event Action? DeviceChanged;

    public DeviceChangeMonitor()
    {
        var cp = new CreateParams
        {
            Caption = "ArctisNovaTrayDeviceSink",
            ExStyle = WS_EX_TOOLWINDOW,
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0
        };

        CreateHandle(cp);
        RegisterHidNotifications();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE)
        {
            int eventType = m.WParam.ToInt32();
            if (eventType is DBT_DEVNODES_CHANGED or DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
            {
                DeviceChanged?.Invoke();
            }
        }

        base.WndProc(ref m);
    }

    private void RegisterHidNotifications()
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DBT_DEVTYP_DEVICEINTERFACE,
            Reserved = 0,
            ClassGuid = HidInterfaceGuid,
            Name = 0
        };

        _notificationHandle = RegisterDeviceNotificationW(Handle, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notificationHandle != IntPtr.Zero)
        {
            _ = UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr RegisterDeviceNotificationW(
        IntPtr hRecipient,
        ref DevBroadcastDeviceInterface notificationFilter,
        int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterDeviceNotification(IntPtr handle);
}
