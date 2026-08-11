using System.Globalization;
using System.IO;
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

        wpf.Should().NotContain("\"Print failed\"")
            .And.NotContain("\"Printed presentation\"")
            .And.NotContain("\"Print cancelled\"")
            .And.NotContain("\"The portable print handoff plan was not built.\"")
            .And.Contain("PresentationNativePrintPortResult")
            .And.Contain("BuildFileFeedback(result)");
        avaloniaPorts.Should().NotContain("static string CommandText")
            .And.NotContain("\"Printing failed.\"")
            .And.NotContain("\"The command failed.\"")
            .And.Contain("BuildSystemPrintResult")
            .And.Contain("BuildFileFeedback(result)");
        avaloniaWindow.Should().NotContain(
                "$\"{LastNativePrintResult.StatusText}: {LastNativePrintResult.FailureReason}\"")
            .And.NotContain("\"Portable print submission failed.\"")
            .And.NotContain("\"Printable package was not built.\"")
            .And.Contain("BuildSystemPrintStatusText");
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
