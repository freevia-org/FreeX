using System.Buffers.Binary;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

internal sealed class LinuxNarrationMediaCapturePolicy : ILinuxMediaCapturePolicy
{
    private readonly LinuxNarrationCaptureTool? _tool;

    public LinuxNarrationMediaCapturePolicy(LinuxNarrationCaptureTool? tool)
    {
        _tool = tool;
    }

    public SlideShowRecordingMediaArtifactKind Kind =>
        SlideShowRecordingMediaArtifactKind.NarrationAudio;

    public string TemporaryDirectoryName => "freep-narration";

    public string ContentType =>
        SlideShowRecordingMediaArtifactPolicy.Describe(Kind).ContentType;

    public bool IsAvailable(SlideShowRecordingCaptureAdapterReadiness readiness) =>
        readiness.CanCaptureNarration;

    public SlideShowRecordingCaptureDeviceDescriptor SelectDevice(
        IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices,
        LinuxRecordingHostMetadata metadata) =>
        LinuxNarrationCapturePlanner.SelectMicrophone(
            devices,
            metadata.PreferredMicrophoneDeviceId);

    public LinuxNarrationCaptureCommand BuildCommand(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath) =>
        LinuxNarrationCapturePlanner.BuildCaptureCommand(
            _tool ?? throw new InvalidOperationException("Linux narration recorder is unavailable."),
            device,
            outputPath);

    public string BuildTemporaryFileName(SlideShowRecordingCaptureStartRequest request) =>
        $"freep-narration-slide-{request.SlideIndex + 1:D4}-{request.StartedAtUtc.UtcTicks}.wav";

    public string NormalizePackagePath(
        LinuxRecordingHostMetadata metadata,
        string suggestedFileName) =>
        SlideShowRecordingMediaArtifactPolicy.NormalizePackagePath(
            Kind,
            metadata.PackageRoot,
            suggestedFileName,
            "ppt/media/freep-recordings/avalonia");

    public bool HasValidPayload(byte[] payload) => HasNonEmptyWavePayload(payload);

    public string WrongKindMessage(string adapterName) =>
        $"{adapterName}: Linux camera capture is not available in the narration adapter.";

    public string UnavailableMessage(string adapterName, string reason) =>
        $"{adapterName}: {reason}";

    public string NotStartedMessage(string adapterName, int slideIndex, string? startFailure) =>
        startFailure ??
        $"{adapterName}: narration capture was not started for slide {slideIndex + 1}.";

    public string ExitedBeforeCompletionMessage(
        string adapterName,
        ILinuxRecordingChildProcess process) =>
        $"{adapterName}: Linux recorder exited before narration capture completed" +
        LinuxMediaCaptureMessagePolicy.ExitDetail(process) + ".";

    public string DidNotExitMessage(string adapterName) =>
        $"{adapterName}: Linux recorder did not exit after capture stopped.";

    public string ForcedStopMessage(string adapterName) =>
        $"{adapterName}: Linux recorder required forced termination and did not finalize narration audio.";

    public string FailedMessage(
        string adapterName,
        LinuxRecordingProcessStopResult stop) =>
        $"{adapterName}: Linux recorder failed" +
        LinuxMediaCaptureMessagePolicy.ExitDetail(stop) + ".";

    public string MissingOutputMessage(string adapterName) =>
        $"{adapterName}: Linux recorder did not produce a narration file.";

    public string InvalidPayloadMessage(string adapterName) =>
        $"{adapterName}: Linux recorder produced an empty or invalid WAV narration file.";

    public string CaptureFailedMessage(string adapterName, Exception exception) =>
        $"{adapterName}: Linux narration capture failed: {exception.Message}";

    public string StartFailedMessage(string adapterName, Exception exception) =>
        $"{adapterName}: could not start Linux narration capture: {exception.Message}";

    public string CapturedMessage(
        string adapterName,
        SlideShowRecordingCaptureDeviceDescriptor device,
        string packagePath) =>
        $"{adapterName}: narration captured from {device.DisplayName} to {packagePath}";

    private static bool HasNonEmptyWavePayload(byte[] payload)
    {
        if (payload.Length < 45 ||
            !payload.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !payload.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var offset = 12;
        while (offset + 8 <= payload.Length)
        {
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(offset + 4, 4));
            var dataOffset = offset + 8;
            if (payload.AsSpan(offset, 4).SequenceEqual("data"u8))
                return chunkSize > 0 && dataOffset + chunkSize <= payload.Length;

            var paddedSize = checked((long)chunkSize + (chunkSize & 1));
            var nextOffset = dataOffset + paddedSize;
            if (nextOffset > payload.Length || nextOffset > int.MaxValue)
                return false;
            offset = (int)nextOffset;
        }

        return false;
    }

}

internal sealed class LinuxCameraMediaCapturePolicy : ILinuxMediaCapturePolicy
{
    private readonly LinuxCameraCaptureTool? _tool;

    public LinuxCameraMediaCapturePolicy(LinuxCameraCaptureTool? tool)
    {
        _tool = tool;
    }

    public SlideShowRecordingMediaArtifactKind Kind =>
        SlideShowRecordingMediaArtifactKind.CameraVideo;

    public string TemporaryDirectoryName => "freep-camera";

    public string ContentType =>
        SlideShowRecordingMediaArtifactPolicy.Describe(Kind).ContentType;

    public bool IsAvailable(SlideShowRecordingCaptureAdapterReadiness readiness) =>
        readiness.CanCaptureCamera;

    public SlideShowRecordingCaptureDeviceDescriptor SelectDevice(
        IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices,
        LinuxRecordingHostMetadata metadata) =>
        devices.First(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);

    public LinuxNarrationCaptureCommand BuildCommand(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath) =>
        LinuxCameraCapturePlanner.BuildCaptureCommand(
            _tool ?? throw new InvalidOperationException("Linux camera recorder is unavailable."),
            device,
            outputPath);

    public string BuildTemporaryFileName(SlideShowRecordingCaptureStartRequest request) =>
        $"freep-camera-slide-{request.SlideIndex + 1:D4}-{request.StartedAtUtc.UtcTicks}.mp4";

    public string NormalizePackagePath(
        LinuxRecordingHostMetadata metadata,
        string suggestedFileName) =>
        SlideShowRecordingMediaArtifactPolicy.NormalizePackagePath(
            Kind,
            metadata.PackageRoot,
            suggestedFileName,
            "ppt/media/freep-recordings/avalonia");

    public bool HasValidPayload(byte[] payload) =>
        LinuxVideoExportAdapter.HasNonEmptyMp4Payload(payload);

    public string WrongKindMessage(string adapterName) =>
        $"{adapterName}: Linux camera adapter received a narration request.";

    public string UnavailableMessage(string adapterName, string reason) =>
        $"{adapterName}: {reason}";

    public string NotStartedMessage(string adapterName, int slideIndex, string? startFailure) =>
        startFailure ??
        $"{adapterName}: camera capture was not started for slide {slideIndex + 1}.";

    public string ExitedBeforeCompletionMessage(
        string adapterName,
        ILinuxRecordingChildProcess process) =>
        $"{adapterName}: Linux camera recorder exited before capture completed" +
        LinuxMediaCaptureMessagePolicy.ExitDetail(process) + ".";

    public string DidNotExitMessage(string adapterName) =>
        $"{adapterName}: Linux camera recorder did not exit after capture stopped.";

    public string ForcedStopMessage(string adapterName) =>
        $"{adapterName}: Linux camera recorder required forced termination and did not finalize the MP4.";

    public string FailedMessage(
        string adapterName,
        LinuxRecordingProcessStopResult stop) =>
        $"{adapterName}: Linux camera recorder failed" +
        LinuxMediaCaptureMessagePolicy.ExitDetail(stop) + ".";

    public string MissingOutputMessage(string adapterName) =>
        $"{adapterName}: Linux camera recorder did not produce an MP4 file.";

    public string InvalidPayloadMessage(string adapterName) =>
        $"{adapterName}: Linux camera recorder produced an empty or invalid MP4 payload.";

    public string CaptureFailedMessage(string adapterName, Exception exception) =>
        $"{adapterName}: Linux camera capture failed: {exception.Message}";

    public string StartFailedMessage(string adapterName, Exception exception) =>
        $"{adapterName}: could not start Linux camera capture: {exception.Message}";

    public string CapturedMessage(
        string adapterName,
        SlideShowRecordingCaptureDeviceDescriptor device,
        string packagePath) =>
        $"{adapterName}: camera captured from {device.DisplayName} to {packagePath}";

}
