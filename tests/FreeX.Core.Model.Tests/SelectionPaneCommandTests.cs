using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SelectionPaneCommandTests
{
    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_TogglesShapeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), IsVisible = true };
        sheet.DrawingShapes.Add(shape);

        var command = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Shape,
            shape.Id,
            isVisible: false);

        command.Apply(ctx).Success.Should().BeTrue();
        shape.IsVisible.Should().BeFalse();

        command.Revert(ctx);

        shape.IsVisible.Should().BeTrue();
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void SetSelectionPaneObjectVisibilityCommand_TogglesEveryVisualObjectKind(SelectionPaneObjectKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var id = AddObject(sheet, kind, isVisible: true);

        var command = new SetSelectionPaneObjectVisibilityCommand(sheet.Id, kind, id, isVisible: false);

        command.Apply(ctx).Success.Should().BeTrue();

        GetVisibility(sheet, kind, id).Should().BeFalse();
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_MovesPictureWithinMixedSupportedStackAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var middle = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        sheet.DrawingShapes.Add(back);
        sheet.Pictures.Add(middle);
        sheet.TextBoxes.Add(front);

        var command = new MoveSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, middle.Id, forward: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, front.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, middle.Id));

        command.Revert(ctx);

        sheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_MovesTextBoxWithinMixedSupportedStack()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var middle = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        var front = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        sheet.DrawingShapes.Add(back);
        sheet.TextBoxes.Add(middle);
        sheet.Pictures.Add(front);
        sheet.DrawingObjectZOrder.AddRange(
        [
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, middle.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, front.Id)
        ]);

        var command = new MoveSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.TextBox, middle.Id, forward: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, middle.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, front.Id));
    }

    [Fact]
    public void RenameSelectionPaneObjectCommand_RenamesTextBoxAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Notes",
            Name = "Text Box 1"
        };
        sheet.TextBoxes.Add(textBox);

        var command = new RenameSelectionPaneObjectCommand(
            sheet.Id,
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            "  Quarter Notes  ");

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.Name.Should().Be("Quarter Notes");

        command.Revert(ctx);

        textBox.Name.Should().Be("Text Box 1");
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    public void RenameSelectionPaneObjectCommand_RenamesEveryVisualObjectKind(SelectionPaneObjectKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var id = AddObject(sheet, kind, isVisible: true);

        var command = new RenameSelectionPaneObjectCommand(sheet.Id, kind, id, "Dashboard Object");

        command.Apply(ctx).Success.Should().BeTrue();

        GetName(sheet, kind, id).Should().Be("Dashboard Object");
    }

    [Fact]
    public void SetSelectionPaneObjectVisibilityCommand_RejectsMissingObject()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id,
            SelectionPaneObjectKind.Chart,
            Guid.NewGuid(),
            isVisible: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
    }

    private static Guid AddObject(Sheet sheet, SelectionPaneObjectKind kind, bool isVisible)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Chart:
                var chart = new ChartModel
                {
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
                    IsVisible = isVisible
                };
                sheet.Charts.Add(chart);
                return chart.Id;
            case SelectionPaneObjectKind.Picture:
                var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), IsVisible = isVisible };
                sheet.Pictures.Add(picture);
                return picture.Id;
            case SelectionPaneObjectKind.TextBox:
                var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), IsVisible = isVisible };
                sheet.TextBoxes.Add(textBox);
                return textBox.Id;
            case SelectionPaneObjectKind.Shape:
                var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), IsVisible = isVisible };
                sheet.DrawingShapes.Add(shape);
                return shape.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static bool GetVisibility(Sheet sheet, SelectionPaneObjectKind kind, Guid id) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart => sheet.Charts.Single(item => item.Id == id).IsVisible,
            SelectionPaneObjectKind.Picture => sheet.Pictures.Single(item => item.Id == id).IsVisible,
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).IsVisible,
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).IsVisible,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static string? GetName(Sheet sheet, SelectionPaneObjectKind kind, Guid id) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart => sheet.Charts.Single(item => item.Id == id).Name,
            SelectionPaneObjectKind.Picture => sheet.Pictures.Single(item => item.Id == id).Name,
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).Name,
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).Name,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
