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
        evidence.RequiresUserPermission.Should().BeTrue();
        evidence.SharedReadyStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.NarrationAudio);
        evidence.SharedMissingStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.CameraVideo);
        evidence.SummaryText.Should().Contain("real Windows microphone narration handoff");
        evidence.RemainingWork.Should().Contain("Real camera capture");
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
}
