using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArctisNovaTray.Hardware;

public sealed record HidDeviceCandidate(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength,
    ushort FeatureReportByteLength,
    string? ProductString);

public static partial class Win32HidInterop
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_IO_PENDING = 997;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    public static IReadOnlyList<HidDeviceCandidate> EnumerateVendorHidDevices(
        ushort vendorId,
        IReadOnlySet<ushort>? productIdFilter = null)
    {
        var results = new List<HidDeviceCandidate>();
        string vidToken = $"vid_{vendorId:x4}";

        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr deviceInfoSet = SetupDiGetClassDevsW(
            ref hidGuid,
            null,
            IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return results;
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    CbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    int err = Marshal.GetLastPInvokeError();
                    if (err == ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    index++;
                    continue;
                }

                string? devicePath = GetDeviceInterfacePath(deviceInfoSet, ref interfaceData);
                index++;

                if (string.IsNullOrWhiteSpace(devicePath) ||
                    !devicePath.Contains(vidToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Open with 0 desired access to inspect attributes & HID caps non-intrusively.
                using SafeFileHandle metaHandle = CreateFileW(
                    devicePath,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (metaHandle.IsInvalid)
                {
                    continue;
                }

                var attrs = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
                if (!HidD_GetAttributes(metaHandle, ref attrs))
                {
                    continue;
                }

                if (attrs.VendorID != vendorId)
                {
                    continue;
                }

                if (productIdFilter is not null && !productIdFilter.Contains(attrs.ProductID))
                {
                    continue;
                }

                ushort usagePage = 0;
                ushort usage = 0;
                ushort inLen = 0;
                ushort outLen = 0;
                ushort featLen = 0;

                if (HidD_GetPreparsedData(metaHandle, out IntPtr preparsed) && preparsed != IntPtr.Zero)
                {
                    try
                    {
                        if (HidP_GetCaps(preparsed, out HidpCaps caps) == HIDP_STATUS_SUCCESS)
                        {
                            usagePage = caps.UsagePage;
                            usage = caps.Usage;
                            inLen = caps.InputReportByteLength;
                            outLen = caps.OutputReportByteLength;
                            featLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        _ = HidD_FreePreparsedData(preparsed);
                    }
                }

                string? productString = TryGetProductString(metaHandle);

                results.Add(new HidDeviceCandidate(
                    devicePath,
                    attrs.VendorID,
                    attrs.ProductID,
                    attrs.VersionNumber,
                    usagePage,
                    usage,
                    inLen,
                    outLen,
                    featLen,
                    productString));
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    public static bool TryRequestReport(
        HidDeviceCandidate device,
        ReadOnlySpan<byte> requestPayload,
        byte expectedOpcode,
        int timeoutMs,
        out byte[]? responseBytes,
        out string? error)
    {
        responseBytes = null;
        error = null;

        int clampedTimeout = Math.Clamp(timeoutMs, 25, 250);
        long deadlineTick = Environment.TickCount64 + clampedTimeout;

        using SafeFileHandle handle = CreateFileW(
            device.DevicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int win32Err = Marshal.GetLastPInvokeError();
            error = $"CreateFileW failed with Win32 error {win32Err}.";
            return false;
        }

        // Flush only our own newly opened handle's input queue so we read fresh reports.
        _ = HidD_FlushQueue(handle);

        int outLength = device.OutputReportByteLength > 0
            ? device.OutputReportByteLength
            : Math.Max(65, requestPayload.Length);

        byte[] outBuffer = new byte[outLength];
        int copyLen = Math.Min(requestPayload.Length, outBuffer.Length);
        requestPayload[..copyLen].CopyTo(outBuffer);

        if (!WriteOverlapped(handle, outBuffer, clampedTimeout, out string? writeErr))
        {
            error = writeErr ?? "WriteFile failed.";
            return false;
        }

        int inLength = device.InputReportByteLength > 0 ? device.InputReportByteLength : 65;

        // Read up to 4 input reports within the 250ms budget in case SteelSeries GG
        // simultaneously requested another opcode (e.g. 0x20 or 0xA0) on UP=0xFFC0.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int remainingMs = (int)(deadlineTick - Environment.TickCount64);
            if (remainingMs <= 10)
            {
                error = $"Timed out waiting for 0x{expectedOpcode:X2} response.";
                return false;
            }

            byte[] inBuffer = new byte[inLength];
            if (!ReadOverlapped(handle, inBuffer, remainingMs, out int bytesRead, out string? readErr))
            {
                error = readErr;
                return false;
            }

            byte[] frame = bytesRead == inBuffer.Length ? inBuffer : inBuffer[..bytesRead];
            if (frame.Length >= 2 && frame[1] == expectedOpcode)
            {
                responseBytes = frame;
                return true;
            }
        }

        error = $"Did not observe 0x{expectedOpcode:X2} response within 4 HID frames.";
        return false;
    }

    private static bool WriteOverlapped(
        SafeFileHandle handle,
        byte[] buffer,
        int timeoutMs,
        out string? error)
    {
        error = null;
        IntPtr hEvent = CreateEventW(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        if (hEvent == IntPtr.Zero)
        {
            error = $"CreateEventW failed ({Marshal.GetLastPInvokeError()}).";
            return false;
        }

        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var overlapped = new Win32Overlapped
            {
                EventHandle = hEvent
            };

            bool ok = WriteFile(
                handle,
                pinned.AddrOfPinnedObject(),
                (uint)buffer.Length,
                out uint written,
                ref overlapped);

            if (!ok)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err != ERROR_IO_PENDING)
                {
                    error = $"WriteFile failed ({err}).";
                    return false;
                }

                uint wait = WaitForSingleObject(hEvent, (uint)timeoutMs);
                if (wait != WAIT_OBJECT_0)
                {
                    _ = CancelIoEx(handle, ref overlapped);
                    _ = GetOverlappedResult(handle, ref overlapped, out _, bWait: true);
                    error = "WriteFile timed out.";
                    return false;
                }

                if (!GetOverlappedResult(handle, ref overlapped, out written, bWait: false))
                {
                    error = $"GetOverlappedResult(Write) failed ({Marshal.GetLastPInvokeError()}).";
                    return false;
                }
            }

            return written > 0;
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            _ = CloseHandle(hEvent);
        }
    }

    private static bool ReadOverlapped(
        SafeFileHandle handle,
        byte[] buffer,
        int timeoutMs,
        out int bytesRead,
        out string? error)
    {
        bytesRead = 0;
        error = null;

        IntPtr hEvent = CreateEventW(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        if (hEvent == IntPtr.Zero)
        {
            error = $"CreateEventW failed ({Marshal.GetLastPInvokeError()}).";
            return false;
        }

        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var overlapped = new Win32Overlapped
            {
                EventHandle = hEvent
            };

            bool ok = ReadFile(
                handle,
                pinned.AddrOfPinnedObject(),
                (uint)buffer.Length,
                out uint transferred,
                ref overlapped);

            if (!ok)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err != ERROR_IO_PENDING)
                {
                    error = $"ReadFile failed ({err}).";
                    return false;
                }

                uint wait = WaitForSingleObject(hEvent, (uint)timeoutMs);
                if (wait != WAIT_OBJECT_0)
                {
                    _ = CancelIoEx(handle, ref overlapped);
                    _ = GetOverlappedResult(handle, ref overlapped, out _, bWait: true);
                    error = "ReadFile timed out.";
                    return false;
                }

                if (!GetOverlappedResult(handle, ref overlapped, out transferred, bWait: false))
                {
                    error = $"GetOverlappedResult(Read) failed ({Marshal.GetLastPInvokeError()}).";
                    return false;
                }
            }

            bytesRead = (int)transferred;
            return bytesRead > 0;
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            _ = CloseHandle(hEvent);
        }
    }

    private static string? GetDeviceInterfacePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
    {
        _ = SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out uint requiredSize,
            IntPtr.Zero);

        if (requiredSize < 6 || requiredSize > 4096)
        {
            return null;
        }

        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W is 8 on 64-bit Windows, 6 on 32-bit Windows.
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

            if (!SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet,
                ref interfaceData,
                detailBuffer,
                requiredSize,
                out _,
                IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(detailBuffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static string? TryGetProductString(SafeFileHandle handle)
    {
        byte[] buf = new byte[256];
        GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            if (!HidD_GetProductString(handle, pin.AddrOfPinnedObject(), (uint)buf.Length))
            {
                return null;
            }

            string? str = Marshal.PtrToStringUni(pin.AddrOfPinnedObject())?.Trim();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }
        finally
        {
            if (pin.IsAllocated)
            {
                pin.Free();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        public ushort Reserved0, Reserved1, Reserved2, Reserved3, Reserved4;
        public ushort Reserved5, Reserved6, Reserved7, Reserved8, Reserved9;
        public ushort Reserved10, Reserved11, Reserved12, Reserved13, Reserved14;
        public ushort Reserved15, Reserved16;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [LibraryImport("hid.dll")]
    private static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [LibraryImport("hid.dll")]
    private static partial int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetProductString(SafeFileHandle hidDeviceObject, IntPtr buffer, uint bufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_FlushQueue(SafeFileHandle hidDeviceObject);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Overlapped
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        ref Win32Overlapped lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        ref Win32Overlapped lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelIoEx(SafeFileHandle hFile, ref Win32Overlapped lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOverlappedResult(
        SafeFileHandle hFile,
        ref Win32Overlapped lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);
}
