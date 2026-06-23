using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class RecommendedPivotTablesDialogPlannerTests
{
    [Fact]
    public void Contract_ExposesSharedSizingAutomationAndTextKeys()
    {
        RecommendedPivotTablesDialogPlanner.TitleKey.Should().Be("MainWindow_Header_RecommendedPivotTables");
        RecommendedPivotTablesDialogPlanner.NoRecommendationsHeadingKey.Should().Be("RecommendedPivotTables_NoRecommendationsHeading");
        RecommendedPivotTablesDialogPlanner.BlankPivotTableKey.Should().Be("RecommendedPivotTables_BlankPivotTable");
        RecommendedPivotTablesDialogPlanner.DialogAutomationId.Should().Be("RecommendedPivotTablesDialog");
        RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationId.Should().Be("RecommendedPivotTablesBlankPivotTableButton");
        RecommendedPivotTablesDialogPlanner.Width.Should().Be(560);
        RecommendedPivotTablesDialogPlanner.MinHeight.Should().Be(340);
        RecommendedPivotTablesDialogPlanner.BlankPivotTableButtonWidth.Should().Be(132);
        RecommendedPivotTablesDialogPlanner.CancelButtonWidth.Should().Be(80);
    }
}
