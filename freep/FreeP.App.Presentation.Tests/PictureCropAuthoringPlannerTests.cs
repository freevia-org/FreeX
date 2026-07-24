namespace FreeP.App.Compositor.Tests;

public sealed class PictureCropAuthoringPlannerTests
{
    [Fact]
    public void TryPlan_AcceptsVisibleCropAndPreservesFractions()
    {
        PictureCropAuthoringPlanner.TryPlan(0.1, 0.2, 0.3, 0.05, out var values).Should().BeTrue();
        values.Should().Be(new PictureCropValues(0.1, 0.2, 0.3, 0.05));
    }

    [Theory]
    [InlineData(-0.01, 0, 0, 0)]
    [InlineData(0.6, 0, 0.4, 0)]
    [InlineData(0, 0.75, 0, 0.25)]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0, 0)]
    public void TryPlan_RejectsInvalidOrEmptySource(double left, double top, double right, double bottom)
    {
        PictureCropAuthoringPlanner.TryPlan(left, top, right, bottom, out _).Should().BeFalse();
    }

    [Fact]
    public void Presets_ExposeResetAndInset()
    {
        PictureCropAuthoringPlanner.Reset().IsDefault.Should().BeTrue();
        PictureCropAuthoringPlanner.Inset().Should().Be(new PictureCropValues(0.1, 0.1, 0.1, 0.1));
    }

    [Fact]
    public void Build_ExposesCropEdgesAtCurrentSourceFractions()
    {
        var picture = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = new PictureFormat
            {
                CropLeft = 0.1,
                CropTop = 0.2,
                CropRight = 0.3,
                CropBottom = 0.05,
            },
        };

        var plan = PictureCropAuthoringPlanner.Build(picture, new LayoutRect(10, 20, 100, 80));

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Select(handle => handle.Name).Should().Equal(
            PictureCropAuthoringPlanner.LeftHandleName,
            PictureCropAuthoringPlanner.TopHandleName,
            PictureCropAuthoringPlanner.RightHandleName,
            PictureCropAuthoringPlanner.BottomHandleName);
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(20, 60));
        plan.Handles[1].PositionDip.Should().Be(new LayoutPoint(60, 36));
        plan.Handles[2].PositionDip.Should().Be(new LayoutPoint(80, 60));
        plan.Handles[3].PositionDip.Should().Be(new LayoutPoint(60, 96));
    }

    [Fact]
    public void BuildMutationPlan_ChangesOneEdgeAndPreservesOpposingCrop()
    {
        var picture = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = new PictureFormat { CropTop = 0.2, CropRight = 0.3 },
        };

        var mutation = PictureCropAuthoringPlanner.BuildMutationPlan(
            picture,
            new LayoutRect(10, 20, 100, 80),
            PictureCropAuthoringPlanner.LeftHandleName,
            new LayoutPoint(50, 60));

        mutation.ShouldApply.Should().BeTrue();
        mutation.Values.Should().Be(new PictureCropValues(0.4, 0.2, 0.3, 0));

        var constrained = PictureCropAuthoringPlanner.BuildMutationPlan(
            picture,
            new LayoutRect(10, 20, 100, 80),
            PictureCropAuthoringPlanner.LeftHandleName,
            new LayoutPoint(200, 60));
        constrained.Values!.Value.Left.Should().BeLessThan(0.7);
        constrained.Values.Value.Right.Should().Be(0.3);
    }

    [Fact]
    public void Build_DisablesHandlesForNonPictureShapes()
    {
        var shape = new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape };

        PictureCropAuthoringPlanner.Build(shape, new LayoutRect(0, 0, 100, 100))
            .CanEdit.Should().BeFalse();
    }
}
