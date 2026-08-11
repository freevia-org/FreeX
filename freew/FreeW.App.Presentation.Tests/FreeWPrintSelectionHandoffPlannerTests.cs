using Free.Shared.AppServices.Printing;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWPrintSelectionHandoffPlannerTests
{
    [Fact]
    public void Build_PreparedPdfCarriesRangeAndOrientationOnlyIntoPdfPayload()
    {
        var requested = new PrintSelection(
            "Office",
            Copies: 2,
            PageRange: PrintPageRange.Between(2, 4),
            Orientation: PrintOrientation.Landscape);

        var plan = FreeWPrintSelectionHandoffPlanner.Build(
            requested,
            PrintRangeAndOrientationHandling.PreparedPdf);

        plan.PdfSelection.Should().Be(requested);
        plan.SubmissionSelection.Should().Be(requested with
        {
            PageRange = PrintPageRange.All,
            Orientation = PrintOrientation.Document,
        });
    }

    [Fact]
    public void Build_PrinterSubmissionLeavesSelectionForNativeBackend()
    {
        var requested = new PrintSelection(
            "Office",
            PageRange: PrintPageRange.Single(3),
            Orientation: PrintOrientation.Portrait);

        var plan = FreeWPrintSelectionHandoffPlanner.Build(
            requested,
            PrintRangeAndOrientationHandling.PrinterSubmission);

        plan.PdfSelection.Should().Be(new PrintSelection());
        plan.SubmissionSelection.Should().Be(requested);
    }

    [Fact]
    public void AvaloniaDoesNotOwnACompatibilityFacade()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var facadePath = Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "PrintSelectionHandoffPlanner.cs");

        File.Exists(facadePath).Should().BeFalse();
    }
}
