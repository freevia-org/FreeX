using System.Runtime.InteropServices;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Recording;

internal interface IWpfWindowsRecordingDeviceCatalog
{
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices();
}

internal sealed class WpfWindowsRecordingDeviceCatalog : IWpfWindowsRecordingDeviceCatalog
{
    public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();
        }

        var devices = new List<SlideShowRecordingCaptureDeviceDescriptor>();
        var count = WinMm.waveInGetNumDevs();
        for (uint index = 0; index < count; index++)
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
                "audio/mp4"));
        }

        return devices;
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
}
