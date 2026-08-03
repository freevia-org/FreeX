using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class ZoomDialogPlannerTests
{
    [Fact]
    public void Presets_ExposeExcelZoomDialogChoicesInDisplayOrder()
    {
        ZoomDialogPlanner.Presets.Should().Equal(400, 200, 100, 75, 50, 25);
    }

    [Fact]
    public void SizeContract_MatchesWpfVisualEvidenceTarget()
    {
        ZoomDialogPlanner.Width.Should().Be(300);
        ZoomDialogPlanner.Height.Should().Be(240);
        ZoomDialogPlanner.OuterPadding.Should().Be(12);
        ZoomDialogPlanner.MagnificationGroupPadding.Should().Be(8);
        ZoomDialogPlanner.MagnificationGroupBottomMargin.Should().Be(12);
        ZoomDialogPlanner.PresetColumnWidth.Should().Be(96);
        ZoomDialogPlanner.CustomPercentBoxWidth.Should().Be(72);
        ZoomDialogPlanner.CustomPercentBoxHeight.Should().Be(24);
        ZoomDialogPlanner.ActionButtonWidth.Should().Be(72);
    }

    [Fact]
    public void TryCreateResult_AcceptsWholePercentWithinRange()
    {
        ZoomDialogPlanner.TryCreateResult("125%", out var result, out var error).Should().BeTrue();

        result.Should().Be(new ZoomDialogSelection(125));
        error.Should().BeNull();
    }

    [Fact]
    public void TryCreateResult_RejectsOutOfRangePercentWithResourceKey()
    {
        ZoomDialogPlanner.TryCreateResult("401", out _, out var error).Should().BeFalse();

        error.Should().Be(new ZoomDialogValidationError(
            "Zoom_MustBeBetween10And400",
            "Zoom must be between 10% and 400%."));
    }

    [Fact]
    public void TryCreateResult_RejectsFractionalPercentWithResourceKey()
    {
        ZoomDialogPlanner.TryCreateResult("125.5", out _, out var error).Should().BeFalse();

        error.Should().Be(new ZoomDialogValidationError(
            "Zoom_MustBeWholePercentBetween10And400",
            "Zoom must be a whole percent between 10% and 400%."));
    }

    [Fact]
    public void CreateFitSelectionResult_PreservesCurrentPercentAndMarksIntent()
    {
        ZoomDialogPlanner.CreateFitSelectionResult(125)
            .Should()
            .Be(new ZoomDialogSelection(125, FitSelection: true));
    }
}
