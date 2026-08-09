using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia.Printing;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class PrintSelectionHandoffPlannerTests
{
    [Fact]
    public void Build_PreparedPdfCarriesRangeAndOrientationOnlyIntoPdfPayload()
    {
        var requested = new PrintSelection(
            "Office",
            Copies: 2,
            PageRange: PrintPageRange.Between(2, 4),
            Orientation: PrintOrientation.Landscape);

        var plan = PrintSelectionHandoffPlanner.Build(
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

        var plan = PrintSelectionHandoffPlanner.Build(
            requested,
            PrintRangeAndOrientationHandling.PrinterSubmission);

        plan.PdfSelection.Should().Be(new PrintSelection());
        plan.SubmissionSelection.Should().Be(requested);
    }
}
