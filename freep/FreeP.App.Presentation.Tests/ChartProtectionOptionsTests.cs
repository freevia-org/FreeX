using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartProtectionOptionsTests
{
    [Fact]
    public void Planner_PreservesAllFourNullableFlags()
    {
        var chart = new ChartShape
        {
            ChartObjectProtected = true,
            ChartDataProtected = false,
            ChartFormattingProtected = null,
            ChartSelectionProtected = true,
        };

        var planner = ChartProtectionOptionsPlanner.FromChart(chart);
        planner.BuildCommitPlan().Should().Be(new ChartProtectionOptions(true, false, null, true));

        planner.SetChartObject(false);
        planner.SetData(null);
        planner.SetFormatting(true);
        planner.SetSelection(false);
        planner.BuildCommitPlan().Should().Be(new ChartProtectionOptions(false, null, true, false));
    }

    [Fact]
    public void PlannerSurface_ExposesTriStateProtectionChoices()
    {
        var surface = ChartProtectionOptionsPlanner.BuildSurfacePlan();

        surface.CommandId.Should().Be(ChartProtectionOptionsPlanner.CommandId);
        surface.Title.Should().Be(ChartProtectionOptionsPlanner.DialogTitle);
        ChartProtectionOptionsPlanner.BooleanOptions.Select(option => option.Value)
            .Should().Equal(null, true, false);
    }

    [Fact]
    public void SetChartProtectionOptions_IsUndoableAndCanUnlockProtectedChart()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape
        {
            ChartObjectProtected = true,
            ChartDataProtected = true,
            ChartFormattingProtected = true,
            ChartSelectionProtected = true,
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetChartProtectionOptionsCommand(
            0,
            1,
            new ChartProtectionOptions(false, null, false, null)));

        chart.ChartObjectProtected.Should().BeFalse();
        chart.ChartDataProtected.Should().BeNull();
        chart.ChartFormattingProtected.Should().BeFalse();
        chart.ChartSelectionProtected.Should().BeNull();
        chart.RegenerateWorkbookOnSave.Should().BeTrue();

        bus.Undo();

        chart.ChartObjectProtected.Should().BeTrue();
        chart.ChartDataProtected.Should().BeTrue();
        chart.ChartFormattingProtected.Should().BeTrue();
        chart.ChartSelectionProtected.Should().BeTrue();
    }
}
