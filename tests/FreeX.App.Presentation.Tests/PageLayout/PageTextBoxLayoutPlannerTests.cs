using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageTextBoxLayoutPlannerTests
{
    private static readonly WorkbookTheme Theme = WorkbookTheme.Office;
    private static readonly SheetId TestSheetId = SheetId.New();

    [Fact]
    public void Build_ComputesVisibleOnPageTextBoxBoundsAndTextBounds()
    {
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 3, 4),
            Text = "Callout",
            Width = 96,
            Height = 42,
            FillColor = new CellColor(200, 220, 240),
            OutlineColor = new CellColor(20, 70, 120)
        };

        var blocks = PageTextBoxLayoutPlanner.Build(
            [textBox],
            Theme,
            pageRows: [2, 3, 4],
            pageColumns: [3, 4, 5],
            gridLeft: 40,
            gridTop: 20,
            measurement: UniformMeasurement(colWidth: 60, rowHeight: 18));

        var block = blocks.Should().ContainSingle().Subject;
        block.Id.Should().Be(textBox.Id);
        block.Bounds.Should().Be(new LayoutRect(100, 38, 96, 42));
        block.TextBounds.Should().Be(new LayoutRect(104, 42, 88, 34));
        block.Text.Should().Be("Callout");
        block.Fill.Should().Be(new PresentationRgb(200, 220, 240));
        block.FillAlpha.Should().Be(PageTextBoxLayoutPlanner.FillAlpha);
        block.Outline.Should().Be(new PresentationRgb(20, 70, 120));
        block.OutlineThickness.Should().Be(1);
        block.Font.FontFamily.Should().Be(PageContentRenderModelBuilder.PrintFontFamily);
        block.Font.FontSize.Should().Be(PageContentRenderModelBuilder.PrintFontSize);
    }

    [Fact]
    public void Build_SkipsHiddenAndOffPageTextBoxes()
    {
        var visible = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 1, 1),
            Text = "Visible"
        };
        var hidden = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 1, 1),
            Text = "Hidden",
            IsVisible = false
        };
        var offPage = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 9, 9),
            Text = "Off page"
        };

        var blocks = PageTextBoxLayoutPlanner.Build(
            [hidden, visible, offPage],
            Theme,
            pageRows: [1, 2],
            pageColumns: [1, 2],
            gridLeft: 0,
            gridTop: 0,
            measurement: UniformMeasurement(colWidth: 50, rowHeight: 20));

        blocks.Should().ContainSingle().Which.Text.Should().Be("Visible");
    }

    [Fact]
    public void Build_AppliesMinimumSizeAndCanSuppressFill()
    {
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 1, 1),
            Width = 5,
            Height = 6,
            HasFill = false,
            OutlineColor = new CellColor(1, 2, 3)
        };

        var block = PageTextBoxLayoutPlanner.Build(
            [textBox],
            Theme,
            pageRows: [1],
            pageColumns: [1],
            gridLeft: 10,
            gridTop: 20,
            measurement: UniformMeasurement(colWidth: 50, rowHeight: 20)).Single();

        block.Bounds.Width.Should().Be(PageTextBoxLayoutPlanner.MinimumWidth);
        block.Bounds.Height.Should().Be(PageTextBoxLayoutPlanner.MinimumHeight);
        block.TextBounds.Width.Should().Be(PageTextBoxLayoutPlanner.MinimumWidth - PageTextBoxLayoutPlanner.TextInset * 2);
        block.TextBounds.Height.Should().Be(PageTextBoxLayoutPlanner.MinimumHeight - PageTextBoxLayoutPlanner.TextInset * 2);
        block.Fill.Should().BeNull();
        block.Outline.Should().Be(new PresentationRgb(1, 2, 3));
    }

    [Fact]
    public void Build_PreservesInputOrderAndPrintParityIgnoresOffsetsAndRotation()
    {
        var first = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 1, 1),
            Text = "First",
            AnchorOffsetX = 13,
            AnchorOffsetY = 17,
            RotationDegrees = 45,
            FlipHorizontal = true,
            FlipVertical = true
        };
        var second = new TextBoxModel
        {
            Anchor = new CellAddress(TestSheetId, 1, 2),
            Text = "Second"
        };

        var blocks = PageTextBoxLayoutPlanner.Build(
            [first, second],
            Theme,
            pageRows: [1],
            pageColumns: [1, 2],
            gridLeft: 100,
            gridTop: 200,
            measurement: UniformMeasurement(colWidth: 50, rowHeight: 20));

        blocks.Select(block => block.Text).Should().ContainInOrder("First", "Second");
        blocks[0].Bounds.Left.Should().Be(100);
        blocks[0].Bounds.Top.Should().Be(200);
        blocks[1].Bounds.Left.Should().Be(150);
    }

    // Uniform-size PrintGridMeasurement stub: no per-row/per-column offsets, so ColumnOffset/RowOffset
    // fall back to index * width/height, matching the old fixed-size colWidth/rowHeight parameters
    // this test class used before PageTextBoxLayoutPlanner.Build started taking real sheet geometry.
    private static PrintGridMeasurement UniformMeasurement(double colWidth, double rowHeight) =>
        new(HeaderWidth: 0, HeaderHeight: 0, ColumnWidth: colWidth, RowHeight: rowHeight);
}
