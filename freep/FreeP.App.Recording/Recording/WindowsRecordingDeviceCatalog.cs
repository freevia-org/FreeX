using System.Runtime.InteropServices;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public interface IWindowsRecordingDeviceCatalog
{
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices();
}

public sealed class WindowsRecordingDeviceCatalog : IWindowsRecordingDeviceCatalog
{
    public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();
        }

        var devices = new List<SlideShowRecordingCaptureDeviceDescriptor>();
        AddMicrophoneDevices(devices);
        AddCameraDevices(devices);

        return devices;
    }

    private static void AddMicrophoneDevices(List<SlideShowRecordingCaptureDeviceDescriptor> devices)
    {
        var microphoneCount = WinMm.waveInGetNumDevs();
        for (uint index = 0; index < microphoneCount; index++)
        {
            var caps = new WinMm.WaveInCaps();
            var result = WinMm.waveInGetDevCaps(index, ref caps, (uint)Marshal.SizeOf<WinMm.WaveInCaps>());
            var displayName = result == WinMm.MmsysErrNoError && !string.IsNullOrWhiteSpace(caps.ProductName)
                ? caps.ProductName.Trim()
                : $"Microphone {index + 1}";

            devices.Add(new SlideShowRecordingCaptureDeviceDescriptor(
                SlideShowRecordingCaptureDeviceKind.Microphone,
                $"waveIn:{index}",
                displayName,
                IsDefault: index == 0,
                IsAvailable: true,
                "audio/wav"));
        }
    }

    private static void AddCameraDevices(List<SlideShowRecordingCaptureDeviceDescriptor> devices)
    {
        var cameraCategory = SetupApi.KsCategoryVideoCamera;
        var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
            ref cameraCategory,
            null,
            IntPtr.Zero,
            SetupApi.DigcfPresent | SetupApi.DigcfDeviceInterface);
        if (deviceInfoSet == SetupApi.InvalidHandleValue)
            return;

        try
        {
            var cameraIndex = 0;
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SetupApi.SpDeviceInterfaceData
                {
                    CbSize = (uint)Marshal.SizeOf<SetupApi.SpDeviceInterfaceData>()
                };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(
                    deviceInfoSet,
                    IntPtr.Zero,
                    ref cameraCategory,
                    index,
                    ref interfaceData))
                {
                    if (Marshal.GetLastPInvokeError() == SetupApi.ErrorNoMoreItems)
                        break;

                    continue;
                }

                var deviceInfoData = new SetupApi.SpDevinfoData
                {
                    CbSize = (uint)Marshal.SizeOf<SetupApi.SpDevinfoData>()
                };
                var devicePath = SetupApi.TryGetDevicePath(deviceInfoSet, interfaceData, ref deviceInfoData) ??
                    $"ksvideo:{index}";
                var displayName = SetupApi.TryGetDeviceRegistryString(
                        deviceInfoSet,
                        ref deviceInfoData,
                        SetupApi.SpdrpFriendlyName) ??
                    SetupApi.TryGetDeviceRegistryString(
                        deviceInfoSet,
                        ref deviceInfoData,
                        SetupApi.SpdrpDeviceDesc) ??
                    $"Camera {cameraIndex + 1}";

                devices.Add(new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    devicePath,
                    displayName,
                    IsDefault: cameraIndex == 0,
                    IsAvailable: true,
                    "video/mp4"));
                cameraIndex++;
            }
        }
        finally
        {
            _ = SetupApi.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    internal static class WinMm
    {
        internal const uint MmsysErrNoError = 0;

        [DllImport("winmm.dll")]
        internal static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll", EntryPoint = "waveInGetDevCapsW", CharSet = CharSet.Unicode)]
        internal static extern uint waveInGetDevCaps(
            uint deviceId,
            ref WaveInCaps caps,
            uint capsSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WaveInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ProductName;

            public uint Formats;
            public ushort Channels;
            public ushort Reserved;
            public uint Support;
        }
    }

    private static class SetupApi
    {
        internal const int ErrorNoMoreItems = 259;
        internal const uint DigcfPresent = 0x00000002;
        internal const uint DigcfDeviceInterface = 0x00000010;
        internal const uint SpdrpDeviceDesc = 0x00000000;
        internal const uint SpdrpFriendlyName = 0x0000000C;
        internal static readonly IntPtr InvalidHandleValue = new(-1);
        internal static readonly Guid KsCategoryVideoCamera = new("E5323777-F976-4F5B-9B55-B94699C46E44");

        internal static string? TryGetDevicePath(
            IntPtr deviceInfoSet,
            SpDeviceInterfaceData interfaceData,
            ref SpDevinfoData deviceInfoData)
        {
            _ = SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out var requiredSize,
                IntPtr.Zero);
            if (requiredSize == 0)
                return null;

            var detailData = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    out _,
                    ref deviceInfoData))
                {
                    return null;
                }

                return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(detailData);
            }
        }

        internal static string? TryGetDeviceRegistryString(
            IntPtr deviceInfoSet,
            ref SpDevinfoData deviceInfoData,
            uint property)
        {
            var buffer = new byte[1024];
            if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref deviceInfoData,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out var requiredSize) ||
                requiredSize <= 2)
            {
                return null;
            }

            var byteCount = Math.Min((int)requiredSize, buffer.Length);
            return Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0').Trim();
        }

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            string? enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SpDevinfoData deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDeviceInterfaceData
        {
            public uint CbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public UIntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDevinfoData
        {
            public uint CbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public UIntPtr Reserved;
        }
    }
}
