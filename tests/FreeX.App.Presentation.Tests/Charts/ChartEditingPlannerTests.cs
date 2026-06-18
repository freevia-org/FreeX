using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartEditingPlannerTests
{
    // ---- ChartTypeChangePlanner ----------------------------------------------------------------------

    [Fact]
    public void TypeChange_SupportedChoices_AreAllAuthorable_AndCoverCommonFamilies()
    {
        var choices = ChartTypeChangePlanner.GetSupportedChoices();

        choices.Should().NotBeEmpty();
        choices.Should().OnlyContain(choice => ChartTypeSupport.IsAuthorable(choice.Type));
        choices.Select(c => c.Type).Should().Contain(new[]
        {
            ChartType.Column, ChartType.Bar, ChartType.Line, ChartType.Area,
            ChartType.Scatter, ChartType.Pie, ChartType.Doughnut, ChartType.Bubble,
            ChartType.Radar, ChartType.Stock
        });
        choices.Should().OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.DisplayName));
    }

    [Fact]
    public void TypeChange_Plan_ReturnsRequestedType_WhenDifferentAndAuthorable()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Line);

        plan.HasChange.Should().BeTrue();
        plan.AppliedType.Should().Be(ChartType.Line);
        plan.Message.Should().BeNull();
    }

    [Fact]
    public void TypeChange_Plan_IsNoOp_WhenTypeUnchanged()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Pie, ChartType.Pie);

        plan.HasChange.Should().BeFalse();
        plan.AppliedType.Should().BeNull();
        plan.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TypeChange_Plan_Rejects_DeferredAuthoringFamily()
    {
        // Map is renderable-but-not-authorable: the planner must reject converting to it.
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Map);

        plan.HasChange.Should().BeFalse();
        plan.Message.Should().Be(ChartAuthoringPlanner.DeferredAuthoringMessage);
    }

    // ---- ChartTitlesPlanner --------------------------------------------------------------------------

    [Fact]
    public void Titles_Plan_TrimsAndCollapsesWhitespace()
    {
        var input = new ChartTitlesInput("  Sales  ", "  Quarter ", "   ");
        var options = ChartTitlesPlanner.Plan(ChartType.Column, input);

        options.Title.Should().Be("Sales");
        options.XAxisTitle.Should().Be("Quarter");
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Plan_DropsAxisTitles_ForAxislessChartTypes()
    {
        var input = new ChartTitlesInput("Revenue", "Category", "Value");
        var options = ChartTitlesPlanner.Plan(ChartType.Pie, input);

        options.Title.Should().Be("Revenue");
        options.XAxisTitle.Should().BeEmpty();
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Read_ProjectsModelTitles()
    {
        var chart = new ChartModel { Title = "T", XAxisTitle = "X", YAxisTitle = "Y" };
        var input = ChartTitlesPlanner.Read(chart);

        input.ChartTitle.Should().Be("T");
        input.XAxisTitle.Should().Be("X");
        input.YAxisTitle.Should().Be("Y");
    }

    [Fact]
    public void Titles_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        // The planner output applied via the Core command must land on the model.
        var chart = new ChartModel { Type = ChartType.Column, Title = "old" };
        var options = ChartTitlesPlanner.Plan(chart.Type, new ChartTitlesInput("New Title", "Months", "Units"));

        ApplyLayout(chart, options);

        chart.Title.Should().Be("New Title");
        chart.XAxisTitle.Should().Be("Months");
        chart.YAxisTitle.Should().Be("Units");
    }

    // ---- ChartLegendPlanner --------------------------------------------------------------------------

    [Fact]
    public void Legend_PositionChoices_AreTheFourPlacements()
    {
        var positions = ChartLegendPlanner.GetPositionChoices().Select(c => c.Position).ToList();

        positions.Should().BeEquivalentTo(new[]
        {
            ChartLegendPosition.Right, ChartLegendPosition.Top,
            ChartLegendPosition.Left, ChartLegendPosition.Bottom
        });
        positions.Should().NotContain(ChartLegendPosition.None);
    }

    [Fact]
    public void Legend_Read_SurfacesNoneAsRight()
    {
        var chart = new ChartModel { ShowLegend = false, LegendPosition = ChartLegendPosition.None };
        var input = ChartLegendPlanner.Read(chart);

        input.ShowLegend.Should().BeFalse();
        input.Position.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_SetsShowAndPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Bottom));

        options.ShowLegend.Should().BeTrue();
        options.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
    }

    [Fact]
    public void Legend_Plan_KeepsPosition_EvenWhenHidden()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: false, ChartLegendPosition.Left));

        options.ShowLegend.Should().BeFalse();
        options.LegendPosition.Should().Be(ChartLegendPosition.Left);
    }

    [Fact]
    public void Legend_Plan_FallsBackToRight_ForInvalidPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.None));

        options.LegendPosition.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column, ShowLegend = true, LegendPosition = ChartLegendPosition.Right };
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Top));

        ApplyLayout(chart, options);

        chart.ShowLegend.Should().BeTrue();
        chart.LegendPosition.Should().Be(ChartLegendPosition.Top);
    }

    private static void ApplyLayout(ChartModel chart, ChartLayoutOptions options)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        if (chart.DataRange.Start.Sheet != sheet.Id)
        {
            chart.DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3));
        }

        sheet.Charts.Add(chart);
        var ctx = new InMemoryCommandContext(workbook);
        var outcome = new SetChartLayoutCommand(sheet.Id, chart.Id, options).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    private sealed class InMemoryCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
