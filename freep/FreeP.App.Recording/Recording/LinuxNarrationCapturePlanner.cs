using System.Globalization;
using System.Text.RegularExpressions;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public enum LinuxNarrationCaptureToolKind
{
    PipeWire,
    PulseAudio,
    FfmpegCamera
}

public sealed record LinuxNarrationCaptureTool(
    LinuxNarrationCaptureToolKind Kind,
    string ExecutablePath,
    string DisplayName);

public sealed record LinuxNarrationCaptureDiscovery(
    LinuxNarrationCaptureTool? Tool,
    IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> Devices,
    string UnavailableReason)
{
    public bool IsAvailable =>
        Tool is not null &&
        Devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);

    public static LinuxNarrationCaptureDiscovery Unavailable(string reason) =>
        new(
            Tool: null,
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            Normalize(reason, "No supported Linux narration recorder is available."));

    private static string Normalize(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed record LinuxNarrationCaptureCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string OutputPath,
    LinuxNarrationCaptureToolKind ToolKind);

public static partial class LinuxNarrationCapturePlanner
{
    public const int SampleRate = 16000;
    public const int ChannelCount = 1;

    [GeneratedRegex(
        @"^\s*(?<default>\*)?\s*(?<id>[0-9]+|[^\s:]+)\s*:\s*(?<name>.+?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PipeWireTargetPattern();

    public static IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> ParsePipeWireTargets(
        string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
            return Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();

        var devices = new List<SlideShowRecordingCaptureDeviceDescriptor>();
        foreach (var line in SplitLines(standardOutput))
        {
            var match = PipeWireTargetPattern().Match(line);
            if (!match.Success)
                continue;

            var id = match.Groups["id"].Value.Trim();
            var displayName = NormalizeDisplayName(match.Groups["name"].Value, id);
            if (devices.Any(device => string.Equals(device.DeviceId, id, StringComparison.Ordinal)))
                continue;

            devices.Add(Microphone(
                id,
                displayName,
                match.Groups["default"].Success));
        }

        return EnsureSingleDefault(devices);
    }

    public static IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> ParsePulseAudioSources(
        string standardOutput,
        string? defaultSourceName)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
            return Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>();

        var normalizedDefault = defaultSourceName?.Trim();
        var devices = new List<SlideShowRecordingCaptureDeviceDescriptor>();
        foreach (var line in SplitLines(standardOutput))
        {
            var columns = line.Split('\t');
            if (columns.Length < 2)
                columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
                continue;

            var id = columns[1].Trim();
            if (id.Length == 0 || devices.Any(device => string.Equals(device.DeviceId, id, StringComparison.Ordinal)))
                continue;

            devices.Add(Microphone(
                id,
                HumanizePulseSourceName(id),
                string.Equals(id, normalizedDefault, StringComparison.Ordinal)));
        }

        return EnsureSingleDefault(devices);
    }

    public static SlideShowRecordingCaptureDeviceDescriptor SelectMicrophone(
        IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices,
        string? preferredDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var available = devices
            .Where(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
                device.IsAvailable)
            .ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("No available Linux microphone was discovered.");

        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var preferred = available.FirstOrDefault(device =>
                string.Equals(device.DeviceId, preferredDeviceId.Trim(), StringComparison.Ordinal));
            if (preferred is not null)
                return preferred;
        }

        return available.FirstOrDefault(device => device.IsDefault) ?? available[0];
    }

    public static LinuxNarrationCaptureCommand BuildCaptureCommand(
        LinuxNarrationCaptureTool tool,
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (device.Kind != SlideShowRecordingCaptureDeviceKind.Microphone)
            throw new ArgumentException("Linux narration capture requires a microphone device.", nameof(device));

        var arguments = tool.Kind switch
        {
            LinuxNarrationCaptureToolKind.PipeWire => BuildPipeWireArguments(device, outputPath),
            LinuxNarrationCaptureToolKind.PulseAudio => BuildPulseAudioArguments(device, outputPath),
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };

        return new LinuxNarrationCaptureCommand(
            tool.ExecutablePath,
            arguments,
            outputPath,
            tool.Kind);
    }

    private static IReadOnlyList<string> BuildPipeWireArguments(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath)
    {
        var arguments = new List<string>
        {
            $"--rate={SampleRate.ToString(CultureInfo.InvariantCulture)}",
            $"--channels={ChannelCount.ToString(CultureInfo.InvariantCulture)}",
            "--channel-map=mono",
            "--format=s16"
        };
        if (!device.IsDefault && !string.IsNullOrWhiteSpace(device.DeviceId))
            arguments.Add($"--target={device.DeviceId}");
        arguments.Add(outputPath);
        return arguments;
    }

    private static IReadOnlyList<string> BuildPulseAudioArguments(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "--file-format=wav",
            "--format=s16le",
            $"--rate={SampleRate.ToString(CultureInfo.InvariantCulture)}",
            $"--channels={ChannelCount.ToString(CultureInfo.InvariantCulture)}",
            "--channel-map=mono"
        };
        if (!string.IsNullOrWhiteSpace(device.DeviceId))
            arguments.Add($"--device={device.DeviceId}");
        arguments.Add(outputPath);
        return arguments;
    }

    private static SlideShowRecordingCaptureDeviceDescriptor Microphone(
        string id,
        string displayName,
        bool isDefault) =>
        new(
            SlideShowRecordingCaptureDeviceKind.Microphone,
            id,
            displayName,
            isDefault,
            IsAvailable: true,
            "audio/wav");

    private static IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnsureSingleDefault(
        IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices)
    {
        if (devices.Count == 0 || devices.Any(device => device.IsDefault))
            return devices;

        return devices
            .Select((device, index) => index == 0 ? device with { IsDefault = true } : device)
            .ToArray();
    }

    private static IEnumerable<string> SplitLines(string value) =>
        value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeDisplayName(string value, string fallback)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal) &&
            normalized.EndsWith("]", StringComparison.Ordinal) &&
            normalized.Length > 2)
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string HumanizePulseSourceName(string sourceName)
    {
        var normalized = sourceName.Trim();
        var lastSegment = normalized.Split('.').LastOrDefault() ?? normalized;
        var words = lastSegment
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
        return words.Length == 0 ? normalized : words;
    }
}
