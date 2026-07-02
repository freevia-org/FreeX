using FreeW.App.Presentation.Backstage;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstagePrintPanePlannerTests
{
    [Fact]
    public void Build_ReturnsPrintActionsAndPageSummary()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginTopPt = 72,
            MarginBottomPt = 90,
            MarginLeftPt = 54,
            MarginRightPt = 54,
            ColumnCount = 2,
            ColumnSpacingPt = 36,
            Landscape = true,
        };

        var plan = BackstagePrintPanePlanner.Build("Agenda", page);

        plan.Description.Should().Contain("Print this document");
        plan.Fields.Should().Contain(row => row.Label == "Document" && row.Value == "Agenda");
        plan.Fields.Should().Contain(row => row.Label == "Paper" && row.Value == "8.5\" x 11\"");
        plan.Fields.Should().Contain(row => row.Label == "Orientation" && row.Value == "Landscape");
        plan.Fields.Should().Contain(row => row.Label == "Margins" && row.Value.Contains("Bottom 1.25\"", StringComparison.Ordinal));
        plan.Fields.Should().Contain(row => row.Label == "Columns" && row.Value == "2 (spacing 0.5\")");

        plan.Groups.Should().Contain(group =>
            group.Heading == "Print" &&
            group.Actions.Any(action => action.Kind == BackstagePrintActionKind.Print && action.Label == "Print") &&
            group.Actions.Any(action => action.Kind == BackstagePrintActionKind.PrintPreview && action.Label == "Print Preview"));
        plan.Groups.Should().Contain(group => group.Heading == "Settings");

        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PrintPreviewFidelity &&
            row.Status == BackstagePrintEvidenceStatus.FixtureReady &&
            row.FixtureScenarioIds.Contains("backstage-print-preview-fidelity"));
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PdfExportFidelity &&
            row.Status == BackstagePrintEvidenceStatus.FixtureReady &&
            row.FixtureScenarioIds.Contains("backstage-pdf-export-fidelity"));
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.NativePrint &&
            row.Status == BackstagePrintEvidenceStatus.Deferred &&
            row.FixtureScenarioIds.Count == 0);
    }

    [Fact]
    public void Build_NormalizesBlankDisplayNameAndMirrorMargins()
    {
        var page = new PageSettings { MirrorMargins = true };

        var plan = BackstagePrintPanePlanner.Build(" ", page);

        plan.Fields.Should().Contain(row => row.Label == "Document" && row.Value == "Untitled");
        plan.Fields.Should().Contain(row => row.Label == "Margins" && row.Value.EndsWith(" (mirror margins)", StringComparison.Ordinal));
    }
}
