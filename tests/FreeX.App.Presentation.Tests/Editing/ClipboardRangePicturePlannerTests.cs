using FluentAssertions;
using FreeX.App.Presentation.Editing;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class ClipboardRangePicturePlannerTests
{
    [Fact]
    public void TryBuild_ProjectsIrregularRowsIntoOneBoundedRectangularGrid()
    {
        string[][] rows = [["Alpha", "Beta"], ["Gamma"]];

        var plan = ClipboardRangePicturePlanner.TryBuild(rows);

        plan.Should().NotBeNull();
        plan!.RowCount.Should().Be(2);
        plan.ColumnCount.Should().Be(2);
        plan.PixelWidth.Should().Be(2 * ClipboardRangePicturePlanner.CellWidth);
        plan.PixelHeight.Should().Be(2 * ClipboardRangePicturePlanner.CellHeight);
        plan.TextAt(0, 1).Should().Be("Beta");
        plan.TextAt(1, 1).Should().BeEmpty();

        rows[0][0] = "mutated";
        plan.TextAt(0, 0).Should().Be("Alpha");
    }

    [Fact]
    public void TryBuild_RejectsEmptyOversizedAndUnsupportedDimensions()
    {
        ClipboardRangePicturePlanner.TryBuild(null).Should().BeNull();
        ClipboardRangePicturePlanner.TryBuild([]).Should().BeNull();
        ClipboardRangePicturePlanner.TryBuild([[]]).Should().BeNull();
        ClipboardRangePicturePlanner.TryBuild(
                Enumerable.Range(0, 45)
                    .Select(_ => Enumerable.Repeat(string.Empty, 45).ToArray())
                    .ToArray())
            .Should().BeNull("45 x 45 exceeds the shared 2,000-cell safety limit");
        ClipboardRangePicturePlanner.TryBuild(
                [Enumerable.Repeat(string.Empty, 410).ToArray()])
            .Should().BeNull("the native bitmap backends cannot safely realize a 32,800-pixel row");
    }

    [Fact]
    public void Plan_OwnsTheCrossRendererVisualContract()
    {
        ClipboardRangePicturePlanner.BackgroundColor.Should().Be(new ClipboardRangePictureColor(255, 255, 255));
        ClipboardRangePicturePlanner.GridlineColor.Should().Be(new ClipboardRangePictureColor(211, 211, 211));
        ClipboardRangePicturePlanner.TextColor.Should().Be(new ClipboardRangePictureColor(0, 0, 0));
        ClipboardRangePicturePlanner.FontSize.Should().Be(12);
        ClipboardRangePicturePlanner.TextPaddingHorizontal.Should().Be(2);
        ClipboardRangePicturePlanner.TextPaddingVertical.Should().Be(1);
    }

    [Fact]
    public void BothRenderers_ConsumeTheSharedPlanAndAttachPictureFlavor()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var skiaRenderer = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "SkiaClipboardRangePictureRenderer.cs"));

        wpf.Should().Contain("ClipboardRangePicturePlanner.TryBuild(ClipboardSerializer.Deserialize(text))")
            .And.Contain("TryRenderClipboardRangeBitmap(picturePlan)")
            .And.NotContain("const double cellWidth = 80")
            .And.NotContain("const int maxCells = 2000");
        avalonia.Should().Contain("ClipboardRangePicturePlanner.TryBuild(ClipboardSerializer.Deserialize(copiedText))")
            .And.Contain("Image = SkiaClipboardRangePictureRenderer.TryRender(picturePlan)");
        skiaRenderer.Should().Contain("ClipboardRangePicturePlan? plan")
            .And.Contain("new PlatformClipboardImage(data.ToArray(), plan.PixelWidth, plan.PixelHeight)")
            .And.NotContain("const int maxCells")
            .And.NotContain("const int cellWidth");
    }
}
