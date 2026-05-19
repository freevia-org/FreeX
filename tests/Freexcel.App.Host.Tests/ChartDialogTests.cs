using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ChartDialogTests
{
    [Fact]
    public void ChartTypePickerPlanner_ReturnsOnlyRenderableChartTypesWithFriendlyLabels()
    {
        var options = ChartTypePickerPlanner.GetSupportedOptions();

        options.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.Line,
            ChartType.Pie,
            ChartType.Doughnut,
            ChartType.Bar,
            ChartType.StackedBar,
            ChartType.PercentStackedBar,
            ChartType.Scatter,
            ChartType.Bubble,
            ChartType.Area,
            ChartType.Radar,
            ChartType.Stock);
        options.Should().NotContain(option => !ChartTypeSupport.IsRenderable(option.Type));
        options.Single(option => option.Type == ChartType.PercentStackedColumn).DisplayName
            .Should()
            .Be("100% Stacked Column");
    }

    [Fact]
    public void ChartTypePickerPlanner_RecommendsDefaultChartTypes()
    {
        var recommendations = ChartTypePickerPlanner.GetRecommendedOptions();

        recommendations.Select(option => option.Type).Should().ContainInOrder(
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter);
        recommendations.Should().OnlyContain(option => option.IsRecommended);
    }

    [Fact]
    public void InsertChartDialog_BuildsResultForSelectedChartType()
    {
        var result = InsertChartDialog.CreateResult(ChartType.Line);

        result.ChartType.Should().Be(ChartType.Line);
        result.UseRecommendedLayout.Should().BeFalse();
    }

    [Fact]
    public void InsertChartDialog_UsesFirstRecommendationForRecommendedResult()
    {
        var result = InsertChartDialog.CreateRecommendedResult();

        result.ChartType.Should().Be(ChartType.Column);
        result.UseRecommendedLayout.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ChangeChartTypeDialog(ChartType.Bar);

            dialog.SelectedChartType.Should().Be(ChartType.Bar);
        });
        ChangeChartTypeDialog.CreateResult(ChartType.Area).ChartType.Should().Be(ChartType.Area);
    }

    [Fact]
    public void MoveChartDialog_CreatesObjectAndNewSheetResults()
    {
        MoveChartDialog.CreateObjectResult("Sheet2").Should().Be(
            new MoveChartDialogResult(MoveChartTargetKind.ObjectInSheet, "Sheet2"));
        MoveChartDialog.CreateNewSheetResult("Revenue Chart").Should().Be(
            new MoveChartDialogResult(MoveChartTargetKind.NewChartSheet, "Revenue Chart"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveChartDialog_RejectsMissingTargetName(string? targetName)
    {
        var act = () => MoveChartDialog.CreateNewSheetResult(targetName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SelectDataSourceDialog_NormalizesSourceRangeAndSwitchState()
    {
        var result = SelectDataSourceDialog.CreateResult("  A1:D12  ", true);

        result.SourceRangeText.Should().Be("A1:D12");
        result.SwitchRowColumn.Should().BeTrue();
    }
}
