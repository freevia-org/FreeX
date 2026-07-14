using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowRecordingHostAdapterParityPlannerTests
{
    [Fact]
    public void BuildEvidence_WithWpfAndAvaloniaMicrophoneAdapters_ProjectsPairedNarrationHandoff()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildEvidence(
            new[]
            {
                MicrophoneReadiness("WPF slideshow", "WPF Windows microphone capture adapter"),
                MicrophoneReadiness("Avalonia slideshow", "Avalonia Windows microphone capture adapter")
            });

        evidence.HostRows.Should().HaveCount(2);
        evidence.HasWpfNarrationHandoff.Should().BeTrue();
        evidence.HasAvaloniaNarrationHandoff.Should().BeTrue();
        evidence.HasPairedNarrationHandoff.Should().BeTrue();
        evidence.HasAnyCameraHandoff.Should().BeFalse();
        evidence.HasPairedCameraHandoff.Should().BeFalse();
        evidence.RequiresUserPermission.Should().BeTrue();
        evidence.SharedReadyStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.NarrationAudio);
        evidence.SharedMissingStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.CameraVideo);
        evidence.SummaryText.Should().Contain("real Windows microphone narration handoff");
        evidence.RemainingWork.Should().Contain("Real camera capture");
    }

    [Fact]
    public void BuildEvidence_WithWpfAndAvaloniaCameraAdapters_ProjectsPairedCameraHandoffReadiness()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildEvidence(
            new[]
            {
                MicrophoneAndCameraReadiness("WPF slideshow", "WPF Windows recording capture adapter"),
                MicrophoneAndCameraReadiness("Avalonia slideshow", "Avalonia Windows recording capture adapter")
            });

        evidence.HostRows.Should().HaveCount(2);
        evidence.HasPairedNarrationHandoff.Should().BeTrue();
        evidence.HasWpfCameraHandoff.Should().BeTrue();
        evidence.HasAvaloniaCameraHandoff.Should().BeTrue();
        evidence.HasPairedCameraHandoff.Should().BeTrue();
        evidence.HasAnyCameraHandoff.Should().BeTrue();
        evidence.SharedReadyStreams.Should().Equal(
            SlideShowRecordingCaptureStreamKind.NarrationAudio,
            SlideShowRecordingCaptureStreamKind.CameraVideo);
        evidence.SharedMissingStreams.Should().BeEmpty();
        evidence.SummaryText.Should().Contain("camera video handoff readiness");
        evidence.RemainingWork.Should().Contain("Encoded real camera media payload capture");
        evidence.RemainingWork.Should().Contain("PowerPoint COM recording baselines");
    }

    [Fact]
    public void BuildEvidence_WithOneDeferredHost_DoesNotClaimPairedNarration()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildEvidence(
            new[]
            {
                MicrophoneReadiness("WPF slideshow", "WPF Windows microphone capture adapter"),
                SlideShowRecordingCaptureAdapterReadiness.Deferred(
                    "Avalonia slideshow",
                    "Avalonia Windows microphone capture adapter")
            });

        evidence.HasWpfNarrationHandoff.Should().BeTrue();
        evidence.HasAvaloniaNarrationHandoff.Should().BeFalse();
        evidence.HasPairedNarrationHandoff.Should().BeFalse();
        evidence.SharedReadyStreams.Should().BeEmpty();
        evidence.SharedMissingStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.CameraVideo);
        evidence.SummaryText.Should().Contain("not paired");
    }

    [Fact]
    public void BuildCameraEncodingReadinessEvidence_WithNoComDefaultRows_DoesNotClaimEncodedPayloadOrPowerPointBaseline()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildCameraEncodingReadinessEvidence(
            new[]
            {
                CameraEncodingRow(
                    "WPF slideshow",
                    "WPF Windows recording capture adapter",
                    "ppt/media/freep-recordings/wpf/slide-001-camera.mp4"),
                CameraEncodingRow(
                    "Avalonia slideshow",
                    "Avalonia Windows recording capture adapter",
                    "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4")
            });

        evidence.HostRows.Should().HaveCount(2);
        evidence.HasWpfNoComHandoff.Should().BeTrue();
        evidence.HasAvaloniaNoComHandoff.Should().BeTrue();
        evidence.HasPairedNoComHandoff.Should().BeTrue();
        evidence.HasPackageTargets.Should().BeTrue();
        evidence.HasLocalEncodedPayload.Should().BeFalse();
        evidence.HasPairedEncodedPayload.Should().BeFalse();
        evidence.ClaimsPowerPointComBaseline.Should().BeFalse();
        evidence.SummaryText.Should().Contain("deferring video encoding honestly");
        evidence.RemainingWork.Should().Contain("Actual local default no-COM real camera video encoding");
        evidence.RemainingWork.Should().Contain("PowerPoint COM recording baselines");
    }

    [Fact]
    public void BuildCameraEncodingReadinessEvidence_WithDeterministicPayloadRows_KeepsDefaultHardwareEncodingDeferred()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildCameraEncodingReadinessEvidence(
            new[]
            {
                CameraEncodingRow(
                    "WPF slideshow",
                    "WPF Windows recording capture adapter",
                    "ppt/media/freep-recordings/wpf/slide-001-camera.mp4"),
                CameraEncodingRow(
                    "Avalonia slideshow",
                    "Avalonia Windows recording capture adapter",
                    "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4"),
                CapturedCameraEncodingRow(
                    "WPF slideshow",
                    "WPF Windows recording capture adapter",
                    "ppt/media/freep-recordings/wpf/slide-002-camera.mp4",
                    payloadLengthBytes: 128),
                CapturedCameraEncodingRow(
                    "Avalonia slideshow",
                    "Avalonia Windows recording capture adapter",
                    "ppt/media/freep-recordings/avalonia/slide-002-camera.mp4",
                    payloadLengthBytes: 160)
            });

        evidence.HasPairedNoComHandoff.Should().BeTrue();
        evidence.HasWpfEncodedPayload.Should().BeTrue();
        evidence.HasAvaloniaEncodedPayload.Should().BeTrue();
        evidence.HasPairedEncodedPayload.Should().BeTrue();
        evidence.HasWpfDeterministicEncodedPayload.Should().BeTrue();
        evidence.HasAvaloniaDeterministicEncodedPayload.Should().BeTrue();
        evidence.HasPairedDeterministicEncodedPayload.Should().BeTrue();
        evidence.HasPairedDefaultNoComEncodedPayload.Should().BeFalse();
        evidence.HasLocalEncodedPayload.Should().BeTrue();
        evidence.ClaimsPowerPointComBaseline.Should().BeFalse();
        evidence.HostRows.Should().OnlyContain(row => row.HasPackageTarget);
        evidence.SummaryText.Should().Contain("deterministic injected encoded mp4 payload packaging");
        evidence.SummaryText.Should().Contain("real default camera video encoding remains deferred");
        evidence.RemainingWork.Should().Contain("Actual local default no-COM real camera video encoding");
    }

    [Fact]
    public void BuildCameraEncodingReadinessEvidence_WithActualDefaultPayloadRows_SeparatesFutureDefaultEncodingSuccess()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildCameraEncodingReadinessEvidence(
            new[]
            {
                CapturedDefaultCameraEncodingRow(
                    "WPF slideshow",
                    "WPF Windows recording capture adapter",
                    "ppt/media/freep-recordings/wpf/slide-001-camera.mp4",
                    payloadLengthBytes: 256),
                CapturedDefaultCameraEncodingRow(
                    "Avalonia slideshow",
                    "Avalonia Windows recording capture adapter",
                    "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4",
                    payloadLengthBytes: 288)
            });

        evidence.HasPairedDefaultNoComEncodedPayload.Should().BeTrue();
        evidence.HasPairedDeterministicEncodedPayload.Should().BeFalse();
        evidence.HasPairedEncodedPayload.Should().BeTrue();
        evidence.ClaimsPowerPointComBaseline.Should().BeFalse();
        evidence.SummaryText.Should().Contain("actual local default no-COM camera mp4 payload evidence");
    }

    [Fact]
    public void BuildUnavailableHardwareEvidence_WithNoDeviceRows_ProjectsPairedNoCaptureEvidence()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildUnavailableHardwareEvidence(
            new[]
            {
                NoDeviceReadiness("WPF slideshow", "WPF Windows recording capture adapter"),
                NoDeviceReadiness("Avalonia slideshow", "Avalonia Windows recording capture adapter")
            });

        evidence.HostRows.Should().HaveCount(2);
        evidence.HasWpfUnavailableHardware.Should().BeTrue();
        evidence.HasAvaloniaUnavailableHardware.Should().BeTrue();
        evidence.HasPairedUnavailableHardware.Should().BeTrue();
        evidence.ClaimsCapture.Should().BeFalse();
        evidence.ClaimsPowerPointComBaseline.Should().BeFalse();
        evidence.HostRows.Should().OnlyContain(row =>
            row.IsUnavailableHardwareEvidence &&
            row.ReadyStreams.Count == 0 &&
            row.MissingStreams.SequenceEqual(new[]
            {
                SlideShowRecordingCaptureStreamKind.NarrationAudio,
                SlideShowRecordingCaptureStreamKind.CameraVideo
            }));
        evidence.SummaryText.Should().Contain("no available microphone or camera hardware");
        evidence.RemainingWork.Should().Contain("Live capture on real microphone/camera hardware");
    }

    [Fact]
    public void BuildUnavailableHardwareEvidence_WithDeferredAdapter_DoesNotTreatMissingAdapterAsHardwareEvidence()
    {
        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildUnavailableHardwareEvidence(
            new[]
            {
                NoDeviceReadiness("WPF slideshow", "WPF Windows recording capture adapter"),
                SlideShowRecordingCaptureAdapterReadiness.Deferred(
                    "Avalonia slideshow",
                    "Avalonia Windows recording capture adapter")
            });

        evidence.HasWpfUnavailableHardware.Should().BeTrue();
        evidence.HasAvaloniaUnavailableHardware.Should().BeFalse();
        evidence.HasPairedUnavailableHardware.Should().BeFalse();
        evidence.ClaimsCapture.Should().BeFalse();
        evidence.SummaryText.Should().Contain("not paired");
    }

    private static SlideShowRecordingCaptureAdapterReadiness MicrophoneReadiness(
        string hostName,
        string adapterName) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            hostName,
            adapterName,
            new[]
            {
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav")
            },
            requiresUserPermission: true,
            unavailableReason: $"{hostName} camera capture is still deferred.");

    private static SlideShowRecordingCaptureAdapterReadiness MicrophoneAndCameraReadiness(
        string hostName,
        string adapterName) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            hostName,
            adapterName,
            new[]
            {
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav"),
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    "camera-0",
                    "Presenter camera",
                    IsDefault: true,
                    IsAvailable: true,
                    "video/mp4")
            },
            requiresUserPermission: true,
            unavailableReason: string.Empty);

    private static SlideShowRecordingCameraEncodingReadinessRow CameraEncodingRow(
        string hostName,
        string adapterName,
        string packagePath) =>
        new(
            hostName,
            adapterName,
            packagePath,
            "video/mp4",
            DeviceHandoffReached: true,
            IsCaptured: false,
            PayloadLengthBytes: 0,
            RequiresPowerPointCom: false,
            SlideShowRecordingCameraEncodingEvidenceSource.LocalDefaultNoComEngine,
            $"{adapterName}: camera device handoff reached, but local video encoding is not implemented in this no-COM adapter.");

    private static SlideShowRecordingCameraEncodingReadinessRow CapturedCameraEncodingRow(
        string hostName,
        string adapterName,
        string packagePath,
        long payloadLengthBytes) =>
        new(
            hostName,
            adapterName,
            packagePath,
            "video/mp4",
            DeviceHandoffReached: true,
            IsCaptured: true,
            payloadLengthBytes,
            RequiresPowerPointCom: false,
            SlideShowRecordingCameraEncodingEvidenceSource.DeterministicInjectedCaptureEngine,
            $"{adapterName}: deterministic encoded camera payload supplied for {packagePath}.");

    private static SlideShowRecordingCameraEncodingReadinessRow CapturedDefaultCameraEncodingRow(
        string hostName,
        string adapterName,
        string packagePath,
        long payloadLengthBytes) =>
        new(
            hostName,
            adapterName,
            packagePath,
            "video/mp4",
            DeviceHandoffReached: true,
            IsCaptured: true,
            payloadLengthBytes,
            RequiresPowerPointCom: false,
            SlideShowRecordingCameraEncodingEvidenceSource.LocalDefaultNoComEngine,
            $"{adapterName}: default no-COM camera engine encoded payload supplied for {packagePath}.");

    private static SlideShowRecordingCaptureAdapterReadiness NoDeviceReadiness(
        string hostName,
        string adapterName) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            hostName,
            adapterName,
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            requiresUserPermission: true,
            unavailableReason: "No Windows microphone or camera devices were reported by the host adapter.");
}
