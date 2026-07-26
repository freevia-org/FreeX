using FluentAssertions;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SubtotalParityFixtureTests
{
    [Fact]
    public void CreateState_UsesTheSharedWorkbookRangeAndAllDialogDefaults()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();

        var state = SubtotalParityFixture.CreateState(sheet);

        state.SelectedRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 4)));
        state.Columns.Select(column => column.Header).Should().Equal("Region", "Product", "Units", "Price");
        state.Columns.Where(column => column.IsSelected).Select(column => column.Offset).Should().Equal(2u, 3u);
        state.GroupColumnOffset.Should().Be(0);
        state.FunctionText.Should().Be(SubtotalDialogPlanner.DefaultFunctionText);
        state.ReplaceCurrentSubtotals.Should().BeTrue();
        state.PageBreakBetweenGroups.Should().BeFalse();
        state.SummaryBelowData.Should().BeTrue();
    }

    [Fact]
    public void CreatePlan_RoundTripsTheSharedRequestShape()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();

        var plan = SubtotalParityFixture.CreateState(sheet).CreatePlan();

        var options = plan.ToInputOptions();
        options.GroupColumnOffset.Should().Be(0);
        options.SubtotalColumnOffsets.Should().Equal(2u, 3u);
        options.FunctionNumber.Should().Be(9);
        options.ReplaceExisting.Should().BeTrue();
        options.PageBreakBetweenGroups.Should().BeFalse();
        options.SummaryBelowData.Should().BeTrue();
    }

    [Fact]
    public void ApplySheetState_SeedsTheOutlineDirectionConsumedByTheDialog()
    {
        var sheet = new Sheet(SheetId.New(), "Data") { OutlineSummaryBelow = false };

        SubtotalParityFixture.ApplySheetState(sheet);

        sheet.OutlineSummaryBelow.Should().BeTrue();
    }
}
