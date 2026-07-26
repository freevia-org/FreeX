using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class Chart3DViewOptionsTests
{
    [Fact]
    public void Planner_UsesWorkingCopyAndClampsCameraRanges()
    {
        var chart = new ChartShape
        {
            View3D = new Chart3DView
            {
                RotationX = 20,
                RotationY = 35,
                Perspective = 45,
                HeightPercent = 110,
                DepthPercent = 120,
                RightAngleAxes = true,
            },
            ThreeDStyle = ChartThreeDStyle.Column,
            BarGapDepthPercent = 150,
            Wireframe = true,
            WireframeSpecified = true,
        };

        var planner = Chart3DViewOptionsPlanner.FromChart(chart);
        planner.SetRotationX(200);
        planner.SetRotationY(-10);
        planner.SetPerspective(300);
        planner.SetHeightPercent(-1);
        planner.SetDepthPercent(501);
        planner.SetBarGapDepthPercent(501);
        planner.SetRightAngleAxes(false);
        planner.SetWireframe(false);

        planner.BuildCommitPlan().Should().Be(new Chart3DViewOptions(
            90, 0, 240, 0, 500, false, false, 500));
        chart.View3D!.RotationX.Should().Be(20);
        chart.Wireframe.Should().BeTrue();
    }

    [Fact]
    public void SetChart3DViewOptions_IsUndoableAndPreservesExplicitFalseTokens()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape();
        chart.ThreeDStyle = ChartThreeDStyle.Column;
        chart.BarGapDepthPercent = 150;
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        var options = new Chart3DViewOptions(25, 35, 54, 100, 125, true, false, 250);
        bus.Execute(new SetChart3DViewOptionsCommand(0, shape.Id, options));

        chart.View3D.Should().NotBeNull();
        chart.View3D!.RotationX.Should().Be(25);
        chart.View3D.RotationY.Should().Be(35);
        chart.View3D.Perspective.Should().Be(54);
        chart.View3D.HeightPercent.Should().Be(100);
        chart.View3D.DepthPercent.Should().Be(125);
        chart.View3D.RightAngleAxes.Should().BeTrue();
        chart.WireframeSpecified.Should().BeTrue();
        chart.Wireframe.Should().BeFalse();
        chart.BarGapDepthPercent.Should().Be(250);

        bus.Undo();

        chart.View3D.Should().BeNull();
        chart.WireframeSpecified.Should().BeFalse();
        chart.Wireframe.Should().BeFalse();
        chart.BarGapDepthPercent.Should().Be(150);

        bus.Redo();
        chart.View3D.Should().NotBeNull();
        chart.WireframeSpecified.Should().BeTrue();
        chart.Wireframe.Should().BeFalse();
        chart.BarGapDepthPercent.Should().Be(250);
    }

    [Fact]
    public void SurfacePlan_ExposesSharedDialogContract()
    {
        var plan = Chart3DViewOptionsPlanner.BuildSurfacePlan();

        plan.CommandId.Should().Be(Chart3DViewOptionsPlanner.CommandId);
        plan.Title.Should().Be(Chart3DViewOptionsPlanner.DialogTitle);
        plan.RotationXLabel.Should().Be("Elevation (degrees)");
        plan.DepthPercentLabel.Should().Be("Depth (%)");
        plan.BarGapDepthPercentLabel.Should().Be("Gap depth (%)");
        plan.WireframeLabel.Should().Be("Surface wireframe");
        Chart3DViewOptionsPlanner.BooleanOptions.Select(option => option.Label)
            .Should().Equal("Automatic", "On", "Off");
    }
}
