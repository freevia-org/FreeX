using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ExportPublishOptionEvidencePlannerTests
{
    [Fact]
    public void Build_SummarizesPageRangeAndUnsupportedPublishOptionRejections()
    {
        var plan = ExportPublishOptionEvidencePlanner.Build(renderedPageCount: 3);

        plan.HasCompleteRejectionEvidence.Should().BeTrue();
        plan.RejectsEmptyRenderedPageRange.Should().BeTrue();
        plan.RejectsPageRangeStartingAfterLastPage.Should().BeTrue();
        plan.RejectsPageRangeEndingAfterLastPage.Should().BeTrue();
        plan.RejectsUnsupportedPdfA.Should().BeTrue();
        plan.RejectsUnsupportedTaggedPdf.Should().BeTrue();
        plan.ClearsPdfOnlyChoicesForXps.Should().BeTrue();
        plan.StatusText.Should().Be(
            "Export publish option evidence: rendered page ranges reject empty/start-after/end-after output; PDF/A and tagged PDF are rejected for PDF; XPS clears PDF-only choices before export.");
    }

    [Fact]
    public void Build_TreatsZeroRequestedPagesAsEvidenceScenarioWithoutLosingRangeChecks()
    {
        var plan = ExportPublishOptionEvidencePlanner.Build(renderedPageCount: 0);

        plan.HasCompleteRejectionEvidence.Should().BeTrue();
        plan.StatusText.Should().Contain("rendered page ranges reject");
    }
}
