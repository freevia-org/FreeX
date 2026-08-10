using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class MergeCellsContentWarningPlannerTests
{
    [Fact]
    public void Create_PreservesRendererTextCountPreviewAndActionIdentity()
    {
        var plan = MergeCellsContentWarningPlanner.Create(
            ["Alpha", " ", "Beta", "Gamma", "Delta", "Epsilon"]);

        plan.Title.Should().Be("Merge Cells");
        plan.PrimaryMessage.Should().Be("Merging cells can discard cell contents.");
        plan.CompactGuidanceMessage.Should().Be("Choose how to handle the selected cell contents.");
        plan.DetailedGuidanceMessage.Should().Be(
            "Only the first cell is kept by default. Choose how FreeX should handle the other selected contents.");
        plan.PreviewText.Should().Be("Alpha, Beta, Gamma, Delta");
        plan.EntryCountText.Should().Be("Non-empty cells: 6");
        plan.DialogAutomationId.Should().Be(FreeXAutomationIdCatalog.MergeCellsContentWarningDialog);

        var keep = plan.Action(MergeCellsContentWarningAction.KeepFirstCell);
        keep.Label.Should().Be("Keep only first cell");
        keep.AutomationId.Should().Be(FreeXAutomationIdCatalog.MergeCellsKeepFirstButton);
        keep.IsDefault.Should().BeTrue();

        var concatenate = plan.Action(MergeCellsContentWarningAction.ConcatenateAllCells);
        concatenate.Label.Should().Be("Concatenate all cells");
        concatenate.AutomationId.Should().Be(FreeXAutomationIdCatalog.MergeCellsConcatenateButton);

        var cancel = plan.Action(MergeCellsContentWarningAction.Cancel);
        cancel.Label.Should().Be("Cancel");
        cancel.AutomationId.Should().Be(FreeXAutomationIdCatalog.MergeCellsCancelButton);
        cancel.IsCancel.Should().BeTrue();
    }

    [Fact]
    public void Create_LeavesOptionalEntryPresentationEmptyWhenThereAreNoEntries()
    {
        var plan = MergeCellsContentWarningPlanner.Create([]);

        plan.PreviewText.Should().BeEmpty();
        plan.EntryCountText.Should().BeNull();
    }
}
