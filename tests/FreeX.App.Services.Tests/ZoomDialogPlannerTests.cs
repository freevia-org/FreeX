using FluentAssertions;
using FreeX.App.Services;
using Free.Shared.Shell;

namespace FreeX.App.Services.Tests;

public sealed class ZoomDialogPlannerTests
{
    [Fact]
    public void Presets_ExposeExcelZoomDialogChoicesInDisplayOrder()
    {
        ZoomDialogPlanner.Presets.Should().Equal(400, 200, 100, 75, 50, 25);
    }

    [Fact]
    public void VisualContract_LivesInSharedShellMetrics()
    {
        ZoomDialogPlanner.ValidationFallbackResourceKey.Should().Be("Zoom_EnterAValidZoomPercent");
        ZoomDialogVisualMetrics.WindowWidth.Should().Be(300);
        ZoomDialogVisualMetrics.WindowHeight.Should().Be(240);
        ZoomDialogVisualMetrics.OuterPadding.Should().Be(12);
        ZoomDialogVisualMetrics.MagnificationGroupPadding.Should().Be(8);
        ZoomDialogVisualMetrics.MagnificationGroupBottomMargin.Should().Be(12);
        ZoomDialogVisualMetrics.PresetColumnWidth.Should().Be(96);
        ZoomDialogVisualMetrics.CustomPercentBoxWidth.Should().Be(72);
        ZoomDialogVisualMetrics.CustomPercentBoxHeight.Should().Be(24);
        ZoomDialogVisualMetrics.ActionButtonWidth.Should().Be(72);
        ZoomDialogVisualMetrics.CustomPercentBoxAutomationId.Should().Be("ZoomCustomPercentBox");
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

    [Fact]
    public void Renderers_UsePlannerOwnedValidationFallbackKey()
    {
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "ZoomDialog.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("ZoomDialogPlanner.ValidationFallbackResourceKey");
        avalonia.Should().Contain("ZoomDialogPlanner.ValidationFallbackResourceKey");
        paired.Should().NotContain("\"Zoom_EnterAValidZoomPercent\"");
    }
}
