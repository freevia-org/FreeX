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

    // freep-picture-crop-clamp-crash: a legally-authored srcRect can crop 100% from one edge
    // (CropLeft/Top/Right/Bottom = 1.0). Dragging the OPPOSITE handle must clamp gracefully
    // instead of throwing, because Math.Clamp throws ArgumentException when min > max and
    // "1 - 1.0 - MinimumVisibleFraction" is negative. PowerPoint stops the dragged edge at its
    // minimum (0, fully open) rather than inverting the crop rectangle.
    [Theory]
    [InlineData(nameof(PictureCropAuthoringPlanner.RightHandleName))]
    [InlineData(nameof(PictureCropAuthoringPlanner.LeftHandleName))]
    [InlineData(nameof(PictureCropAuthoringPlanner.TopHandleName))]
    [InlineData(nameof(PictureCropAuthoringPlanner.BottomHandleName))]
    public void BuildMutationPlan_DoesNotThrow_WhenOppositeEdgeIsFullyCropped(string handleNameProperty)
    {
        var handleName = handleNameProperty switch
        {
            nameof(PictureCropAuthoringPlanner.RightHandleName) => PictureCropAuthoringPlanner.RightHandleName,
            nameof(PictureCropAuthoringPlanner.LeftHandleName) => PictureCropAuthoringPlanner.LeftHandleName,
            nameof(PictureCropAuthoringPlanner.TopHandleName) => PictureCropAuthoringPlanner.TopHandleName,
            nameof(PictureCropAuthoringPlanner.BottomHandleName) => PictureCropAuthoringPlanner.BottomHandleName,
            _ => throw new ArgumentOutOfRangeException(nameof(handleNameProperty)),
        };

        // The opposite edge of whichever handle we're dragging is pinned at 100% cropped,
        // exactly as a real srcRect l="100000" (or t/r/b) would load.
        var format = handleName switch
        {
            PictureCropAuthoringPlanner.RightHandleName => new PictureFormat { CropLeft = 1.0 },
            PictureCropAuthoringPlanner.LeftHandleName => new PictureFormat { CropRight = 1.0 },
            PictureCropAuthoringPlanner.BottomHandleName => new PictureFormat { CropTop = 1.0 },
            PictureCropAuthoringPlanner.TopHandleName => new PictureFormat { CropBottom = 1.0 },
            _ => new PictureFormat(),
        };

        var picture = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = format,
        };

        var bounds = new LayoutRect(10, 20, 100, 80);

        // Drag the handle inward, an entirely ordinary pointer-move mid-gesture.
        var act = () => PictureCropAuthoringPlanner.BuildMutationPlan(
            picture,
            bounds,
            handleName,
            new LayoutPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));

        var mutation = act.Should().NotThrow().Subject;

        // The dragged edge must clamp to its minimum (0 = fully open) rather than invert past
        // the opposite edge.
        var draggedFraction = handleName switch
        {
            PictureCropAuthoringPlanner.LeftHandleName => mutation.Values!.Value.Left,
            PictureCropAuthoringPlanner.TopHandleName => mutation.Values!.Value.Top,
            PictureCropAuthoringPlanner.RightHandleName => mutation.Values!.Value.Right,
            PictureCropAuthoringPlanner.BottomHandleName => mutation.Values!.Value.Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(handleName)),
        };
        draggedFraction.Should().Be(0);
    }

    [Fact]
    public void BuildMutationPlan_RightHandle_StillTracksPointer_WhenOppositeEdgeIsNotFullyCropped()
    {
        // Sibling coverage: the clamp-floor fix must not disturb ordinary in-range dragging.
        var picture = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = new PictureFormat { CropLeft = 0.1 },
        };

        var bounds = new LayoutRect(0, 0, 100, 100);

        var mutation = PictureCropAuthoringPlanner.BuildMutationPlan(
            picture,
            bounds,
            PictureCropAuthoringPlanner.RightHandleName,
            new LayoutPoint(70, 50));

        mutation.ShouldApply.Should().BeTrue();
        mutation.Values!.Value.Right.Should().BeApproximately(0.3, 1e-9);
        mutation.Values.Value.Left.Should().Be(0.1);
    }
}
