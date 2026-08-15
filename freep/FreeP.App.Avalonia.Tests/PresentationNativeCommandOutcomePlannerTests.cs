using System.Globalization;
using System.IO;
using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresentationNativeCommandOutcomePlannerTests
{
    [Fact]
    public void Command_text_preserves_existing_file_command_names()
    {
        WithInvariantCulture(() =>
        {
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.Open)
                .Should().Be("Open");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.SaveAs)
                .Should().Be("Save");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.ExportPdf)
                .Should().Be("Export to PDF");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.ExportNotesPagePdf)
                .Should().Be("Export notes pages to PDF");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.ExportImages)
                .Should().Be("Export slides as images");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.Print)
                .Should().Be("Print");
            PresentationNativeCommandOutcomePlanner.CommandText(PresentationFileCommand.ExportVideo)
                .Should().Be("Export video");
        });
    }

    [Fact]
    public void Print_outcomes_preserve_dialog_and_system_handoff_copy()
    {
        WithInvariantCulture(() =>
        {
            var dialogSuccess = PresentationNativeCommandOutcomePlanner.BuildPrintCommandResult(
                PresentationNativePrintPortResult.Success(
                    PresentationNativePrintStatusProfile.PresentationDialog));
            var dialogCancel = PresentationNativeCommandOutcomePlanner.BuildPrintCommandResult(
                PresentationNativePrintPortResult.Cancel(
                    PresentationNativePrintStatusProfile.PresentationDialog));
            var dialogFailure = PresentationNativeCommandOutcomePlanner.BuildPrintCommandResult(
                PresentationNativePrintPortResult.Failure(
                    PresentationNativePrintStatusProfile.PresentationDialog,
                    "Printer unavailable"));
            var handoffSuccess = PresentationNativeCommandOutcomePlanner.BuildSystemPrintResult(
                succeeded: true,
                cancelled: false,
                failureReason: null);
            var handoffPeriodSuccess = PresentationNativeCommandOutcomePlanner.BuildSystemPrintResult(
                succeeded: true,
                cancelled: false,
                failureReason: null,
                completedStatusHasPeriod: true);
            var handoffFailure = PresentationNativeCommandOutcomePlanner.BuildSystemPrintResult(
                succeeded: false,
                cancelled: false,
                failureReason: "Queue offline");
            var sharedSubmission = PresentationNativeCommandOutcomePlanner.BuildSystemPrintResult(
                new PrintSubmissionResult(
                    PrintSubmissionStatus.Failed,
                    "Office",
                    Message: "Shared queue offline"));

            dialogSuccess.StatusText.Should().Be("Printed presentation");
            dialogCancel.StatusText.Should().Be("Print cancelled");
            dialogFailure.StatusText.Should().Be("Print failed");
            dialogFailure.FailureReason.Should().Be("Printer unavailable");
            PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(handoffSuccess)
                .Should().Be("Linux print handoff completed");
            PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(handoffPeriodSuccess)
                .Should().Be("Linux print handoff completed.");
            PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(handoffFailure)
                .Should().Be("Linux print handoff failed: Queue offline");
            PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(sharedSubmission)
                .Should().Be("Linux print handoff failed: Shared queue offline");
        });
    }

    [Fact]
    public void Video_host_profiles_and_feedback_preserve_names_and_track_counts()
    {
        WithInvariantCulture(() =>
        {
            var wpf = PresentationNativeCommandOutcomePlanner.BuildVideoExportHostCapabilities(
                PresentationVideoExportHostProfile.WpfWindows,
                canEncodeMp4: true,
                canCaptureNarration: true,
                canCaptureCameraAndMedia: true,
                canMuxTimedCaptions: true,
                capabilityReason: "ready");
            var avalonia = PresentationNativeCommandOutcomePlanner.BuildVideoExportHostCapabilities(
                PresentationVideoExportHostProfile.AvaloniaLinux,
                canEncodeMp4: true,
                canCaptureNarration: false,
                canCaptureCameraAndMedia: false,
                canMuxTimedCaptions: true,
                capabilityReason: "ready");
            var result = PresentationNativeCommandOutcomePlanner.BuildVideoExportCommandResult(
                succeeded: true,
                cancelled: false,
                failureReason: null,
                narrationTrackCount: 2,
                cameraTrackCount: 1,
                captionTrackCount: 3);

            wpf.HostName.Should().Be("WPF Windows video export host");
            avalonia.HostName.Should().Be("Avalonia Linux video export host");
            avalonia.UnavailableReason.Should().Be(
                "Video-only ffmpeg export is available; narration and captured camera picture-in-picture are unavailable.");
            result.StatusText.Should().Be(
                "Video export completed with 2 narration track(s), 1 camera track(s), and 3 caption track(s)");
        });
    }

    [Fact]
    public void File_feedback_preserves_status_and_dialog_routing()
    {
        WithInvariantCulture(() =>
        {
            var success = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(
                PresentationFileCommandResult.Success(
                    PresentationFileCommand.Save,
                    message: "Saved Deck.pptx"));
            var unavailable = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(
                PresentationFileCommandResult.Unavailable(
                    PresentationFileCommand.Print,
                    "Print unavailable."));
            var openFailure = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Open,
                    "Could not open the presentation",
                    new IOException("Access denied")));
            var printFailure = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Print,
                    "Could not print the presentation",
                    new InvalidOperationException("Queue offline")));

            success.StatusText.Should().Be("Saved Deck.pptx");
            unavailable.StatusText.Should().Be("Print unavailable.");
            unavailable.UnavailableDialogTitle.Should()
                .Be("Could not complete the presentation command");
            unavailable.UnavailableDialogMessage.Should().Be("Print unavailable.");
            openFailure.StatusText.Should().Be("Open failed: Access denied");
            openFailure.ShowAvaloniaFileErrorDialog.Should().BeTrue();
            printFailure.StatusText.Should().Be("Print failed: Queue offline");
            printFailure.ShowAvaloniaFileErrorDialog.Should().BeFalse();
        });
    }

    [Fact]
    public void Renderer_ports_only_supply_facts_and_realize_feedback_plans()
    {
        var wpf = Read("freep", "FreeP.App.Host", "WpfPresentationFileCommandPorts.cs");
        var avaloniaPorts = Read("freep", "FreeP.App.Avalonia", "MainWindow.FileCommandPorts.cs");
        var avaloniaWindow = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var videoExportSession = Read(
            "freep", "FreeP.App.Recording", "Recording", "PresentationVideoExportSession.cs");

        wpf.Should().NotContain("\"Print failed\"")
            .And.NotContain("\"Printed presentation\"")
            .And.NotContain("\"Print cancelled\"")
            .And.NotContain("\"The portable print handoff plan was not built.\"")
            .And.Contain("PresentationNativePrintPortResult")
            .And.Contain("BuildFileFeedback(result)");
        avaloniaPorts.Should().NotContain("static string CommandText")
            .And.NotContain("\"Printing failed.\"")
            .And.NotContain("\"The command failed.\"")
            .And.NotContain("\"Export completed\"")
            .And.Contain("BuildSystemPrintResult")
            .And.Contain("_session.ExportAsync(")
            .And.Contain("return commandResult;")
            .And.NotContain("BuildVideoExportCommandResult(")
            .And.Contain("BuildFileFeedback(result)");
        videoExportSession.Should().Contain(
            "PresentationNativeCommandOutcomePlanner.BuildVideoExportCommandResult(");
        avaloniaWindow.Should().NotContain(
                "$\"{LastNativePrintResult.StatusText}: {LastNativePrintResult.FailureReason}\"")
            .And.NotContain("\"Portable print submission failed.\"")
            .And.NotContain("\"Printable package was not built.\"")
            .And.NotContain("\"Copy\"")
            .And.NotContain("\"Cut\"")
            .And.NotContain("Avalonia Windows video export host")
            .And.NotContain("Avalonia Linux video export host")
            .And.Contain("BuildPrintStatusText(")
            .And.Contain("BuildVideoExportHostCapabilities(");
    }

    private static string Read(params string[] pathParts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(pathParts));

    private static void WithInvariantCulture(Action action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
