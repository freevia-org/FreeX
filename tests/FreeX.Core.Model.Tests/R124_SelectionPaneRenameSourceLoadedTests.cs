using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R124-selection-pane-rename-1-1: <see cref="RenameSelectionPaneObjectCommand"/> must clear the
/// target object's IsSourceLoaded gate whenever it renames a Picture/TextBox/Shape, mirroring what
/// DrawingShapeFormatCommands/TextBoxCommands already do for format edits. Without this, the
/// save-pipeline invariant documented on
/// XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames ("no edit that clears
/// IsSourceLoaded also renames the object") is violated: the writer's name-based classification
/// keys sourceLoadedNames off the object's CURRENT (renamed) name while
/// XlsxWorksheetDrawingPartMerger.MergeDrawingPart copies the anchor bearing the OLD name through
/// verbatim because supersededSourceNames never contains it -- so a rename of a picture/textbox/
/// shape that was loaded from an existing workbook is silently discarded on save. These tests run
/// through the real command entry point (RenameSelectionPaneObjectCommand.Apply/Revert, exactly
/// what the Selection Pane UI invokes) and assert on the exact model flag
/// (target.IsSourceLoaded == false) that the writer/merger pair reads to decide whether to
/// regenerate a fresh anchor or copy the pristine source anchor verbatim. A full XlsxFileAdapter
/// save/reload round trip lives in FreeX.Core.IO, outside this chain's scoped project
/// (FreeX.Core.Commands + FreeX.Core.Model.Tests); the writer/merger source cited above was read to
/// confirm IsSourceLoaded is exactly the gate consumed downstream.
/// </summary>
public sealed class R124_SelectionPaneRenameSourceLoadedTests
{
    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    public void R124_Rename_ClearsIsSourceLoaded_SoWriterRegeneratesFreshAnchor(SelectionPaneObjectKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var id = AddSourceLoadedObject(sheet, kind);

        GetIsSourceLoaded(sheet, kind, id).Should().BeTrue(
            "the fixture simulates an object that was loaded verbatim from an existing .xlsx source package");

        var command = new RenameSelectionPaneObjectCommand(sheet.Id, kind, id, "CompanyLogo");
        command.Apply(ctx).Success.Should().BeTrue();

        GetName(sheet, kind, id).Should().Be("CompanyLogo");
        GetIsSourceLoaded(sheet, kind, id).Should().BeFalse(
            "renaming a source-loaded object must clear IsSourceLoaded or the writer/merger pair " +
            "will copy the ORIGINAL anchor (with the OLD name) through verbatim on save, silently " +
            "discarding the rename");
    }

    [Fact]
    public void R124_Rename_ChartIsUnaffected_NoIsSourceLoadedGateExists()
    {
        // Sibling/no-regression coverage: Chart has no IsSourceLoaded flag at all (per the model
        // and per XlsxWorksheetChartWriter always fully regenerating chart anchors from the model),
        // so the SelectionPaneObjectRef wiring for Chart passes null accessors. Renaming a chart
        // must keep working exactly as before -- it must not throw, and the (nonexistent) gate
        // must read back as false either way.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Name = "Chart 1"
        };
        sheet.Charts.Add(chart);

        var command = new RenameSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.Chart, chart.Id, "Revenue Chart");

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.Name.Should().Be("Revenue Chart");

        command.Revert(ctx);
        chart.Name.Should().Be("Chart 1");
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    public void R124_Rename_Undo_RestoresBothNameAndIsSourceLoaded(SelectionPaneObjectKind kind)
    {
        // No-regression sibling: undo must restore the PRIOR IsSourceLoaded value, not
        // unconditionally flip it back to true (which would be wrong for an object that was
        // already IsSourceLoaded == false before the rename, e.g. one freshly pasted or already
        // format-edited this session) and not leave it cleared (which would defeat undo's
        // round-trip guarantee for a genuinely source-loaded object).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var id = AddSourceLoadedObject(sheet, kind);

        var command = new RenameSelectionPaneObjectCommand(sheet.Id, kind, id, "CompanyLogo");
        command.Apply(ctx).Success.Should().BeTrue();
        GetIsSourceLoaded(sheet, kind, id).Should().BeFalse();

        command.Revert(ctx);

        GetIsSourceLoaded(sheet, kind, id).Should().BeTrue(
            "undo must restore the object to its pre-rename IsSourceLoaded state, not leave it cleared");
        GetName(sheet, kind, id).Should().NotBe("CompanyLogo");
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    public void R124_Rename_ObjectAlreadyNotSourceLoaded_UndoDoesNotResurrectTheFlag(SelectionPaneObjectKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var id = AddObject(sheet, kind); // IsSourceLoaded starts false (freshly inserted this session)

        GetIsSourceLoaded(sheet, kind, id).Should().BeFalse();

        var command = new RenameSelectionPaneObjectCommand(sheet.Id, kind, id, "Renamed Once");
        command.Apply(ctx).Success.Should().BeTrue();
        GetIsSourceLoaded(sheet, kind, id).Should().BeFalse();

        command.Revert(ctx);

        GetIsSourceLoaded(sheet, kind, id).Should().BeFalse(
            "undoing a rename of an object that was never source-loaded must not incorrectly mark it source-loaded");
    }

    private static Guid AddSourceLoadedObject(Sheet sheet, SelectionPaneObjectKind kind)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel
                {
                    Anchor = new CellAddress(sheet.Id, 1, 1),
                    Name = "Picture 1",
                    IsSourceLoaded = true
                };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel
                {
                    Anchor = new CellAddress(sheet.Id, 1, 1),
                    Name = "TextBox 1",
                    IsSourceLoaded = true
                };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel
                {
                    Anchor = new CellAddress(sheet.Id, 1, 1),
                    Name = "Shape 1",
                    IsSourceLoaded = true
                };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static Guid AddObject(Sheet sheet, SelectionPaneObjectKind kind)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Name = "Picture 1" };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Name = "TextBox 1" };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Name = "Shape 1" };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static string? GetName(Sheet sheet, SelectionPaneObjectKind kind, Guid id) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => sheet.Pictures.Single(item => item.Id == id).Name,
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).Name,
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).Name,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static bool GetIsSourceLoaded(Sheet sheet, SelectionPaneObjectKind kind, Guid id) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => sheet.Pictures.Single(item => item.Id == id).IsSourceLoaded,
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).IsSourceLoaded,
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).IsSourceLoaded,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
