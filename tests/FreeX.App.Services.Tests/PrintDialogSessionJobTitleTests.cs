using FluentAssertions;
using Free.Shared.AppServices.Printing;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round 153 (shared-print-settings F1): PrintDialogSession.Submit must carry the JobTitle the
/// dialog was started with into the final PrintSelection, since CupsPrintService only emits the
/// `-t` job-title argument when selection.JobTitle is populated (see SharedCupsPrintServiceTests).
/// </summary>
public sealed class PrintDialogSessionJobTitleTests
{
    private static PrinterDiscoveryResult Discovery() => new(
        PrinterDiscoveryStatus.Available,
        [new PrinterInfo("Office", true), new PrinterInfo("PDF")],
        "Office");

    [Fact]
    public void Submit_CarriesRequestedJobTitleIntoFinalSelection()
    {
        var session = PrintDialogSession.Start(
            Discovery(),
            new PrintSelection(JobTitle: "Quarterly Report"));

        var submission = session.Submit(
            printerName: "Office",
            copiesText: "1",
            pageRangeIndex: (int)PrintPageRangeKind.All,
            firstPageText: "1",
            lastPageText: "1",
            orientationIndex: (int)PrintOrientation.Document,
            collate: true);

        submission.Succeeded.Should().BeTrue();
        submission.Selection!.JobTitle.Should().Be("Quarterly Report");
    }

    [Fact]
    public void Submit_WithNoRequestedJobTitle_LeavesJobTitleNull()
    {
        // Sibling case: a caller that never set a JobTitle (e.g. PrintSelection defaults) must
        // continue to produce a null JobTitle rather than an empty string or placeholder value.
        var session = PrintDialogSession.Start(Discovery(), new PrintSelection());

        var submission = session.Submit(
            printerName: "Office",
            copiesText: "1",
            pageRangeIndex: (int)PrintPageRangeKind.All,
            firstPageText: "1",
            lastPageText: "1",
            orientationIndex: (int)PrintOrientation.Document,
            collate: true);

        submission.Succeeded.Should().BeTrue();
        submission.Selection!.JobTitle.Should().BeNull();
    }

    [Fact]
    public void Submit_JobTitleReachesCupsCommandPlannerArguments()
    {
        // End-to-end sibling check: the JobTitle preserved by Submit must actually make it onto
        // the `lp -t` argument list built by CupsPrintCommandPlanner, closing the loop the finding
        // described (dialog -> Submit -> CupsPrintCommandPlanner.Submit).
        var session = PrintDialogSession.Start(
            Discovery(),
            new PrintSelection(JobTitle: "Board Deck"));

        var submission = session.Submit(
            printerName: "Office",
            copiesText: "1",
            pageRangeIndex: (int)PrintPageRangeKind.All,
            firstPageText: "1",
            lastPageText: "1",
            orientationIndex: (int)PrintOrientation.Document,
            collate: true);

        submission.Succeeded.Should().BeTrue();

        var invocation = CupsPrintCommandPlanner.Submit(
            "/tmp/portable-print-a1b2c3d4.pdf",
            submission.Selection!,
            "Office");

        invocation.Arguments.Should().ContainInOrder("-t", "Board Deck");
    }
}
