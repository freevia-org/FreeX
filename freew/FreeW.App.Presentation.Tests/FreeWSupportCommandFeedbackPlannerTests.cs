using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWSupportCommandFeedbackPlannerTests
{
    [Fact]
    public void ExternalUriFeedback_IsSilentOnSuccessAndSharedOnFailure()
    {
        FreeWSupportCommandFeedbackPlanner.PlanExternalUriLaunch(
                ExternalUriLaunchResult.Launched,
                "Help",
                "https://example.test")
            .Should().BeNull();

        var failed = FreeWSupportCommandFeedbackPlanner.PlanExternalUriLaunch(
            ExternalUriLaunchResult.LaunchFailed,
            "Help",
            "https://example.test");
        failed.Should().NotBeNull();
        failed!.Tone.Should().Be(FreeWCommandFeedbackTone.Warning);
        failed.Message.Should().Contain("https://example.test");
    }

    [Theory]
    [InlineData(PlatformClipboardWriteStatus.Success, FreeWCommandFeedbackTone.Information)]
    [InlineData(PlatformClipboardWriteStatus.Unavailable, FreeWCommandFeedbackTone.Warning)]
    [InlineData(PlatformClipboardWriteStatus.Unsupported, FreeWCommandFeedbackTone.Warning)]
    [InlineData(PlatformClipboardWriteStatus.Failed, FreeWCommandFeedbackTone.Warning)]
    public void DiagnosticsCopyFeedback_MapsTransportOutcomeToSemanticTone(
        PlatformClipboardWriteStatus status,
        FreeWCommandFeedbackTone tone)
    {
        var plan = FreeWSupportCommandFeedbackPlanner.PlanDiagnosticsCopy(new(status, "detail"));

        plan.Title.Should().Be(FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle);
        plan.Tone.Should().Be(tone);
        plan.Message.Should().NotBeNullOrWhiteSpace();
    }
}
