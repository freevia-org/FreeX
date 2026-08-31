using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// round-176-chart-hyperlink (meta F1): the round-175 commit ("Shift drawing-object hyperlinks with
/// the rows and columns they point at") claimed to cover a hyperlink "on a shape, text box, picture
/// or chart", but ShiftDrawingObjectHyperlinkForShift (RowColumnShiftHelpers.AddressState.cs) was
/// only ever called from ShiftTextBoxes/ShiftDrawingShapes/ShiftPictures -- there was no ShiftCharts,
/// no ChartAddressSnapshot, and no chart-hyperlink undo restore, so a chart's own
/// <see cref="ChartModel.Hyperlink"/> ("Place in This Document") went stale on the very first row or
/// column insert/delete, immediately, with no save/reload needed to observe it.
///
/// These tests mirror FreeXHyperlinksF1_DrawingObjectHyperlinkRowColumnShiftTests (which covers the
/// shape/textbox/picture cases) but exercise the chart path specifically, plus a delete-the-target-row
/// case for all four drawing-object kinds so the "what happens when the target row itself is removed"
/// question -- deliberately never asked for the original three -- gets an explicit, verified answer:
/// the target collapses to a #REF! reference, exactly like an ordinary cell-formula reference into a
/// deleted row does (both paths reuse the identical FormulaRewriter.Rewrite call), so all four
/// object kinds behave identically to each other and to a plain cell hyperlink/formula.
/// </summary>
public sealed class Round176_ChartHyperlinkRowColumnShiftTests
{
    [Fact]
    public void InsertRows_ChartHyperlink_TargetShiftsWithTheCellItPointsTo()
    {
        var workbook = new Workbook("ChartHyperlinkInsertRows");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Hyperlink = new DrawingObjectHyperlink("A10")
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Charts.Should().ContainSingle().Subject;
        shifted.Hyperlink.Should().NotBeNull();
        shifted.Hyperlink!.Target.Should().Be("A13",
            because: "A10 (below the insert point) must follow the data to its new row, matching the shape/textbox/picture behavior");
    }

    [Fact]
    public void InsertRows_ChartHyperlink_UndoRestoresExactPreEditTarget()
    {
        var workbook = new Workbook("ChartHyperlinkUndoInsertRows");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Hyperlink = new DrawingObjectHyperlink("Sheet1!A10")
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("Sheet1!A13");

        command.Revert(ctx);

        var reverted = sheet.Charts.Should().ContainSingle().Subject;
        reverted.Hyperlink!.Target.Should().Be("Sheet1!A10",
            because: "undo must restore the chart's exact pre-insert hyperlink target, mirroring the shape/textbox/picture undo restores");
    }

    [Fact]
    public void InsertColumns_ChartHyperlink_TargetShiftsWithTheCellItPointsTo()
    {
        var workbook = new Workbook("ChartHyperlinkInsertColumns");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Hyperlink = new DrawingObjectHyperlink("J1")
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Charts.Should().ContainSingle().Subject;
        shifted.Hyperlink!.Target.Should().Be("L1",
            because: "column J (index 10, at/after the insert point) must shift right by the inserted column count");

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("J1",
            because: "undo must restore the chart's exact pre-insert hyperlink target");
    }

    // Sibling no-regression case: an external ("Existing File or Web Page") chart hyperlink must
    // never be treated as a cell reference, even when its Target text happens to look like one --
    // mirrors InsertRows_ShapeExternalHyperlink_IsNeverRewritten for the other three kinds.
    [Fact]
    public void InsertRows_ChartExternalHyperlink_IsNeverRewritten()
    {
        var workbook = new Workbook("ChartHyperlinkExternal");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Hyperlink = new DrawingObjectHyperlink("https://example.com/A10", TargetMode: "External")
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3).Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Charts.Should().ContainSingle().Subject;
        shifted.Hyperlink!.Target.Should().Be("https://example.com/A10",
            because: "an external hyperlink target must never be rewritten by a structural row/column edit");
        shifted.Hyperlink!.TargetMode.Should().Be("External");
    }

    // ── Delete-the-target-row case, all four drawing-object kinds ──────────────────────────────
    // What should a hyperlink pointing INTO the deleted rows become? Answer (chosen here, and
    // verified to already be what the original three kinds do): the same #REF! collapse an ordinary
    // cell formula/hyperlink gets when its target row is deleted, because ShiftDrawingObjectHyperlinkForShift
    // reuses the identical FormulaRewriter.Rewrite/DeleteRowsOp path for all four kinds -- there is no
    // kind-specific behavior to diverge. This keeps a drawing object's internal hyperlink consistent
    // with Excel's own #REF! convention instead of silently keeping a stale, now-wrong target or
    // silently clearing the hyperlink outright (either of which would be a worse, undiscoverable
    // surprise for the user versus the visible, standard #REF! error).

    [Fact]
    public void DeleteRows_ShapeHyperlinkTargetingDeletedRow_BecomesRefErrorAndUndoRestores()
    {
        var workbook = new Workbook("ShapeHyperlinkDeletedRow");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 30, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("A6")
        };
        sheet.DrawingShapes.Add(shape);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DrawingShapes.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Contain("#REF!",
            because: "A6 falls inside the deleted row band (5-7) and must collapse to #REF!, matching an ordinary cell reference");

        command.Revert(ctx);
        sheet.DrawingShapes.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("A6",
            because: "undo must restore the shape's exact pre-delete hyperlink target");
    }

    [Fact]
    public void DeleteRows_TextBoxHyperlinkTargetingDeletedRow_BecomesRefErrorAndUndoRestores()
    {
        var workbook = new Workbook("TextBoxHyperlinkDeletedRow");
        var sheet = workbook.AddSheet("Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 30, 1),
            Text = "Back to summary",
            Hyperlink = new DrawingObjectHyperlink("A6")
        };
        sheet.TextBoxes.Add(textBox);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.TextBoxes.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Contain("#REF!",
            because: "A6 falls inside the deleted row band (5-7) and must collapse to #REF!, matching an ordinary cell reference");

        command.Revert(ctx);
        sheet.TextBoxes.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("A6",
            because: "undo must restore the text box's exact pre-delete hyperlink target");
    }

    [Fact]
    public void DeleteRows_PictureHyperlinkTargetingDeletedRow_BecomesRefErrorAndUndoRestores()
    {
        var workbook = new Workbook("PictureHyperlinkDeletedRow");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 30, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Hyperlink = new DrawingObjectHyperlink("A6")
        };
        sheet.Pictures.Add(picture);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Pictures.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Contain("#REF!",
            because: "A6 falls inside the deleted row band (5-7) and must collapse to #REF!, matching an ordinary cell reference");

        command.Revert(ctx);
        sheet.Pictures.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("A6",
            because: "undo must restore the picture's exact pre-delete hyperlink target");
    }

    [Fact]
    public void DeleteRows_ChartHyperlinkTargetingDeletedRow_BecomesRefErrorAndUndoRestores()
    {
        var workbook = new Workbook("ChartHyperlinkDeletedRow");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 22, 2)),
            Hyperlink = new DrawingObjectHyperlink("A6")
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Contain("#REF!",
            because: "A6 falls inside the deleted row band (5-7) and must collapse to #REF!, exactly like the shape/textbox/picture cases above -- this is the behavior the round-175 commit claimed but never actually implemented for charts");

        command.Revert(ctx);
        sheet.Charts.Should().ContainSingle().Subject.Hyperlink!.Target.Should().Be("A6",
            because: "undo must restore the chart's exact pre-delete hyperlink target");
    }
}
