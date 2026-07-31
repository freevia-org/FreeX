using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R107-drawing-object-hyperlink-sheet-identity: a drawing object's (text box/shape/picture/
/// chart) internal ('Place in This Document') <see cref="DrawingObjectHyperlink"/> target is a
/// sheet-qualified reference stored verbatim (e.g. "Sheet1!A1"), the exact same shape as the CELL
/// hyperlink fields (Sheet.Hyperlinks / Sheet.HyperlinkMetadata.Bookmark) that RenameSheetCommand's
/// O25/P113 blocks and RemoveSheetCommand's R95 block already rewrite on rename/delete. Before
/// this fix, neither command ever touched DrawingShapeModel/TextBoxModel/PictureModel/
/// ChartModel.Hyperlink, so after Rename Sheet the drawing object's hyperlink kept naming the OLD
/// sheet name, and after Delete Sheet it kept naming a sheet that no longer exists (instead of
/// becoming #REF! like every other sheet-qualified reference does) -- unlike the equivalent cell
/// hyperlink right next to it, and unlike DuplicateSheetDrawingCloner's R106 fix for the same field
/// on the Duplicate Sheet path (see R106_DuplicateSheetDrawingObjectHyperlinkRebaseTests).
/// </summary>
public sealed class R107_RenameAndDeleteSheetDrawingObjectHyperlinkRewriteTests
{
    [Fact]
    public void RenameSheet_ShapeHyperlinkOnOtherSheet_RewritesToNewNameAndUndoRestores()
    {
        var workbook = new Workbook("RenameShapeHyperlink");
        var target = workbook.AddSheet("Data");
        var host = workbook.AddSheet("Dashboard");
        host.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("Data!A1")
        });
        var ctx = new TestCommandContext(workbook);
        var command = new RenameSheetCommand(target.Id, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        host.DrawingShapes[0].Hyperlink!.Target.Should().Be("Sales!A1",
            because: "a shape's cross-sheet 'Place in This Document' hyperlink must follow the renamed sheet, matching the equivalent cell hyperlink");

        command.Revert(ctx);

        host.DrawingShapes[0].Hyperlink!.Target.Should().Be("Data!A1",
            because: "undo must restore the shape's hyperlink target to its pre-rename value");
    }

    [Fact]
    public void RenameSheet_TextBoxAndPictureAndChartHyperlinks_AllRewrite()
    {
        var workbook = new Workbook("RenameAllDrawingKinds");
        var target = workbook.AddSheet("Data");
        var host = workbook.AddSheet("Dashboard");
        host.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Text = "Go",
            Hyperlink = new DrawingObjectHyperlink("Data!B2")
        });
        host.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Hyperlink = new DrawingObjectHyperlink("Data!C3")
        });
        host.SetCell(new CellAddress(host.Id, 1, 1), new TextValue("Category"));
        host.SetCell(new CellAddress(host.Id, 1, 2), new TextValue("Value"));
        host.SetCell(new CellAddress(host.Id, 2, 1), new TextValue("A"));
        host.SetCell(new CellAddress(host.Id, 2, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(host.Id, 1, 1), new CellAddress(host.Id, 2, 2));
        host.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            Hyperlink = new DrawingObjectHyperlink("Data!D4")
        });
        var ctx = new TestCommandContext(workbook);

        new RenameSheetCommand(target.Id, "Sales").Apply(ctx).Success.Should().BeTrue();

        host.TextBoxes[0].Hyperlink!.Target.Should().Be("Sales!B2");
        host.Pictures[0].Hyperlink!.Target.Should().Be("Sales!C3");
        host.Charts[0].Hyperlink!.Target.Should().Be("Sales!D4");
    }

    // Sibling no-regression case: a hyperlink qualified with a DIFFERENT (unrelated) sheet's name
    // must not be touched by renaming a third sheet.
    [Fact]
    public void RenameSheet_ShapeHyperlinkOnUnrelatedSheet_IsNotRewritten()
    {
        var workbook = new Workbook("RenameUnrelatedShapeHyperlink");
        var renamed = workbook.AddSheet("Data");
        var unrelated = workbook.AddSheet("Other");
        var host = workbook.AddSheet("Dashboard");
        host.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("Other!A1")
        });
        var ctx = new TestCommandContext(workbook);

        new RenameSheetCommand(renamed.Id, "Sales").Apply(ctx).Success.Should().BeTrue();

        host.DrawingShapes[0].Hyperlink!.Target.Should().Be("Other!A1",
            because: "a hyperlink qualified with a different, unrenamed sheet's name must stay untouched");
    }

    // Sibling no-regression case: an external ("Existing File or Web Page") hyperlink must never be
    // rewritten, even when its target text happens to contain a sheet-name-shaped substring.
    [Fact]
    public void RenameSheet_ExternalHyperlink_IsNeverRewritten()
    {
        var workbook = new Workbook("RenameExternalHyperlink");
        var target = workbook.AddSheet("Data");
        var host = workbook.AddSheet("Dashboard");
        host.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Text = "Visit",
            Hyperlink = new DrawingObjectHyperlink("https://example.com/Data!A1", TargetMode: "External")
        });
        var ctx = new TestCommandContext(workbook);

        new RenameSheetCommand(target.Id, "Sales").Apply(ctx).Success.Should().BeTrue();

        host.TextBoxes[0].Hyperlink!.Target.Should().Be("https://example.com/Data!A1",
            because: "an external hyperlink target must never be treated as a sheet-qualified reference");
        host.TextBoxes[0].Hyperlink!.TargetMode.Should().Be("External");
    }

    [Fact]
    public void DeleteSheet_ShapeHyperlinkOnDeletedSheet_RewritesToRefErrorAndUndoRestores()
    {
        var workbook = new Workbook("DeleteShapeHyperlink");
        var toDelete = workbook.AddSheet("Data");
        var host = workbook.AddSheet("Dashboard");
        host.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(host.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Hyperlink = new DrawingObjectHyperlink("Data!A1")
        });
        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(toDelete.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        host.DrawingShapes[0].Hyperlink!.Target.Should().Contain("#REF!",
            because: "a shape's hyperlink naming the deleted sheet must become #REF!, matching the equivalent cell hyperlink");

        command.Revert(ctx);

        host.DrawingShapes[0].Hyperlink!.Target.Should().Be("Data!A1",
            because: "undo must restore the shape's hyperlink target to its pre-delete value");
    }
}
