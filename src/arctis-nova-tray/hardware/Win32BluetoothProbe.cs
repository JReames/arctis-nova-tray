using System.Runtime.InteropServices;

namespace ArctisNovaTray.Hardware;

public sealed record WindowsBluetoothHeadsetStatus(
    bool Paired,
    bool IsConnected,
    string? DeviceName,
    string? InstanceId)
{
    public static WindowsBluetoothHeadsetStatus None { get; } = new(false, false, null, null);
}

/// <summary>
/// Fast in-process Win32 cfgmgr32.dll PnP devnode query for paired/connected SteelSeries Arctis
/// Bluetooth endpoints using DEVPKEY_Device_FriendlyName and DEVPKEY_Device_IsConnected.
/// </summary>
public static partial class Win32BluetoothProbe
{
    private const int CR_SUCCESS = 0x00000000;
    private const int CR_BUFFER_SMALL = 0x0000001A;
    private const uint CM_GETIDLIST_FILTER_NONE = 0x00000000;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

    private const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
    private const uint DEVPROP_TYPE_STRING = 0x00000012;

    private static readonly DevPropKey DevPkeyDeviceFriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private static readonly DevPropKey DevPkeyName = new(
        new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);

    private static readonly DevPropKey DevPkeyDeviceIsConnected = new(
        new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);

    public static WindowsBluetoothHeadsetStatus QueryHeadsetBluetooth()
    {
        if (CM_Get_Device_ID_List_SizeW(out uint charLen, null, CM_GETIDLIST_FILTER_NONE) != CR_SUCCESS ||
            charLen <= 1)
        {
            return WindowsBluetoothHeadsetStatus.None;
        }

        char[] buffer = new char[charLen + 2];
        if (CM_Get_Device_ID_ListW(null, buffer, (uint)buffer.Length, CM_GETIDLIST_FILTER_NONE) != CR_SUCCESS)
        {
            return WindowsBluetoothHeadsetStatus.None;
        }

        WindowsBluetoothHeadsetStatus? pairedFallback = null;

        int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0')
            {
                continue;
            }

            int length = i - start;
            if (length == 0)
            {
                break;
            }

            string instanceId = new(buffer, start, length);
            start = i + 1;

            if (!instanceId.StartsWith(@"BTH\DEV_", StringComparison.OrdinalIgnoreCase) &&
                !instanceId.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CM_Locate_DevNodeW(out uint devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            {
                continue;
            }

            string? name = TryGetStringProperty(devInst, DevPkeyDeviceFriendlyName)
                ?? TryGetStringProperty(devInst, DevPkeyName);

            if (string.IsNullOrWhiteSpace(name) || !IsMatchingHeadsetName(name))
            {
                continue;
            }

            bool isConnected = TryGetBooleanProperty(devInst, DevPkeyDeviceIsConnected) ?? false;
            var candidate = new WindowsBluetoothHeadsetStatus(true, isConnected, name.Trim(), instanceId);

            if (isConnected)
            {
                return candidate;
            }

            pairedFallback ??= candidate;
        }

        return pairedFallback ?? WindowsBluetoothHeadsetStatus.None;
    }

    public static bool IsMatchingHeadsetName(string name) =>
        name.Contains("Nova 7", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Arctis", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SteelSeries", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetStringProperty(uint devInst, DevPropKey key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, IntPtr.Zero, ref size, 0);
        if (cr != CR_BUFFER_SMALL || size < 2 || size > 2048)
        {
            return null;
        }

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            cr = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, buf, ref size, 0);
            if (cr != CR_SUCCESS || propType != DEVPROP_TYPE_STRING)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buf)?.TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static bool? TryGetBooleanProperty(uint devInst, DevPropKey key)
    {
        uint size = 1;
        IntPtr buf = Marshal.AllocHGlobal(4);
        try
        {
            int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propType, buf, ref size, 0);
            if (cr != CR_SUCCESS || propType != DEVPROP_TYPE_BOOLEAN)
            {
                return null;
            }

            return Marshal.ReadByte(buf) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid fmtid, uint pid)
    {
        public Guid Fmtid = fmtid;
        public uint Pid = pid;
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_List_SizeW(
        out uint pulLen,
        string? pszFilter,
        uint ulFlags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_ListW(
        string? pszFilter,
        [Out] char[] buffer,
        uint bufferLen,
        uint ulFlags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DevPropKey propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);
}
