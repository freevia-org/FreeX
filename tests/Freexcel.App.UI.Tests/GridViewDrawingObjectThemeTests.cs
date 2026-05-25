using System;
using System.IO;
using System.Windows;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void ResolveDrawingShapeColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(10, 20, 30));
        var shape = new DrawingShapeModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveDrawingShapeColors(shape, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 228));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void ResolveTextBoxColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(10, 20, 30));
        var textBox = new TextBoxModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveTextBoxColors(textBox, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 228));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void CreateObjectPlaceholderLabel_UsesObjectNameOrExcelLikeFallback()
    {
        GridView.CreateObjectPlaceholderLabel("Picture", "  Logo  ", 3).Should().Be("Logo");
        GridView.CreateObjectPlaceholderLabel("Picture", "", 1).Should().Be("Picture");
        GridView.CreateObjectPlaceholderLabel("Picture", null, 3).Should().Be("Picture 3");
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_MapsTwoCellAnchorToViewportPixels()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(3, 20, 0),
                new RowMetric(4, 20, 20),
                new RowMetric(5, 20, 40)
            ],
            [
                new ColMetric(2, 80, 0),
                new ColMetric(3, 80, 80),
                new ColMetric(4, 80, 160)
            ]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 95250, 2, 190500),
            new DrawingAnchorPoint(3, 47625, 4, 95250));

        var created = GridView.TryCreateDrawingAnchorRect(
            viewport,
            anchor,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            out var rect);

        created.Should().BeTrue();
        rect.Should().Be(new Rect(40, 38, 155, 30));
    }

    [Fact]
    public void GridView_ExposesObjectDisplayModeForExcelPlaceholderRendering()
    {
        var source =
            File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.cs")) +
            File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.RenderDispatch.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.Properties.cs"));

        source.Should().Contain("public enum GridObjectDisplayMode");
        propertiesSource.Should().Contain("ObjectDisplayModeProperty");
        source.Should().Contain("RenderObjectPlaceholders(dc)");
        source.Should().Contain("RenderCharts(dc)");
    }

    [Fact]
    public void PictureRenderer_DrawsSelectionAdornerForPictureAtActiveCell()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.UI", "GridView.DrawingObjects.Pictures.cs"));

        source.Should().Contain("DrawPictureSelectionAdorner");
        source.Should().Contain("SelectedRange?.Start != picture.Anchor");
        source.Should().Contain("dc.DrawRectangle(null, selectedPen, rect);");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
