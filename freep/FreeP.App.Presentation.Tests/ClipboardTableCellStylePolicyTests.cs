using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ClipboardTableCellStylePolicyTests
{
    [Fact]
    public void ApplyCore_MapsFillGeometryInsetsAndMergeStateWithoutTouchingBorders()
    {
        var body = new TextBody();
        var existingBorders = new TableCellBorders();
        var cell = new TableCell { TextBody = body, Borders = existingBorders };
        var style = new InCanvasRichClipboardTableCellStyle(
            FillRgb: 0x112233,
            Anchor: TableCellAnchor.Bottom,
            InsetLeftPt: 1,
            InsetRightPt: 2,
            InsetTopPt: 3,
            InsetBottomPt: 4,
            HorizontalMergeStart: true,
            HorizontalMergeContinuation: true,
            VerticalMergeStart: true,
            VerticalMergeContinuation: true,
            FillPattern: "pct20",
            FillForegroundRgb: 0x445566,
            FillBackgroundRgb: 0x778899,
            TextVerticalType: TextVerticalType.Vertical270);

        ClipboardTableCellStylePolicy.ApplyCore(cell, style);

        cell.Fill.Should().BeOfType<ShapeFill.Pattern>();
        cell.Anchor.Should().Be(TableCellAnchor.Bottom);
        body.VerticalType.Should().Be(TextVerticalType.Vertical270);
        cell.InsetLeftPt.Should().Be(1);
        cell.InsetRightPt.Should().Be(2);
        cell.InsetTopPt.Should().Be(3);
        cell.InsetBottomPt.Should().Be(4);
        cell.GridSpan.Should().Be(2);
        cell.RowSpan.Should().Be(2);
        cell.HMerge.Should().BeTrue();
        cell.VMerge.Should().BeTrue();
        cell.Borders.Should().BeSameAs(existingBorders);
    }

    [Fact]
    public void ApplyCore_UsesSolidFillAndLeavesNullTextBodySafe()
    {
        var cell = new TableCell();
        ClipboardTableCellStylePolicy.ApplyCore(
            cell,
            new InCanvasRichClipboardTableCellStyle(FillRgb: 0xABCDEF, TextVerticalType: TextVerticalType.Vertical));

        cell.Fill.Should().BeOfType<ShapeFill.Solid>();
        cell.TextBody.Should().BeNull();
        cell.GridSpan.Should().Be(1);
        cell.RowSpan.Should().Be(1);
    }

    [Fact]
    public void BothClipboardPlanners_UseSharedCoreAndRetainLocalBorderConversion()
    {
        var table = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Presentation", "ClipboardTablePlanner.cs");
        var rich = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Presentation", "ExternalRichTextClipboardPlanner.cs");

        table.Should().Contain("ClipboardTableCellStylePolicy.ApplyCore(");
        table.Should().Contain("AssignBorder(");
        rich.Should().Contain("ClipboardTableCellStylePolicy.ApplyCore(");
        rich.Should().Contain("ToCapturedOutline(");
    }
}
