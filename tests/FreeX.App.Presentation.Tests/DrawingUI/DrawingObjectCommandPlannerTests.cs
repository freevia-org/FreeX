using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectCommandPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture)]
    [InlineData(DrawingObjectTargetKind.Shape)]
    [InlineData(DrawingObjectTargetKind.TextBox)]
    public void BuildMoveCommand_RepositionsRequestedDrawingObject(DrawingObjectTargetKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);
        var anchor = new CellAddress(sheet.Id, 8, 3);

        var command = DrawingObjectCommandPlanner.BuildMoveCommand(sheet.Id, kind, id, anchor);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetAnchor(sheet, kind, id).Should().Be(anchor);
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture)]
    [InlineData(DrawingObjectTargetKind.Shape)]
    [InlineData(DrawingObjectTargetKind.TextBox)]
    public void BuildResizeCommand_ResizesRequestedDrawingObject(DrawingObjectTargetKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);

        var command = DrawingObjectCommandPlanner.BuildResizeCommand(
            sheet.Id,
            kind,
            id,
            width: 222,
            height: 111,
            flipHorizontal: true,
            flipVertical: false);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetSize(sheet, kind, id).Should().Be((222, 111, true, false));
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture)]
    [InlineData(DrawingObjectTargetKind.Shape)]
    [InlineData(DrawingObjectTargetKind.TextBox)]
    public void BuildResizeWithAnchorCommand_MovesAndResizesInSingleComposite(DrawingObjectTargetKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);
        var anchor = new CellAddress(sheet.Id, 10, 5);

        var command = DrawingObjectCommandPlanner.BuildResizeWithAnchorCommand(
            sheet.Id,
            kind,
            id,
            anchor,
            width: 260,
            height: 130,
            flipHorizontal: false,
            flipVertical: true);
        command.Should().BeOfType<CompositeWorkbookCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetAnchor(sheet, kind, id).Should().Be(anchor);
        GetSize(sheet, kind, id).Should().Be((260, 130, false, true));
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture)]
    [InlineData(DrawingObjectTargetKind.Shape)]
    [InlineData(DrawingObjectTargetKind.TextBox)]
    public void BuildRotateCommand_RotatesRequestedDrawingObject(DrawingObjectTargetKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);

        var command = DrawingObjectCommandPlanner.BuildRotateCommand(sheet.Id, kind, id, 450);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetRotation(sheet, kind, id).Should().Be(90);
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture)]
    [InlineData(DrawingObjectTargetKind.Shape)]
    [InlineData(DrawingObjectTargetKind.TextBox)]
    public void BuildAltTextCommand_SetsAltTextForRequestedDrawingObject(DrawingObjectTargetKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);

        var command = DrawingObjectCommandPlanner.BuildAltTextCommand(sheet.Id, kind, id, "  Object text  ");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetAltText(sheet, kind, id).Should().Be("Object text");
    }

    [Fact]
    public void BuildZOrderCommand_RoutesSelectionPaneKind()
    {
        var workbook = new Workbook("z");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var second = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.DrawingShapes.AddRange([first, second]);

        var target = new DrawingObjectZOrderTarget(SelectionPaneObjectKind.Shape, first.Id, first.Anchor);
        var command = DrawingObjectCommandPlanner.BuildZOrderCommand(sheet.Id, target, forward: true);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        DrawingObjectZOrder.GetNormalizedOrder(sheet).Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, second.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, first.Id));
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture, DrawingObjectTargetKind.Picture)]
    [InlineData(SelectionPaneObjectKind.Shape, DrawingObjectTargetKind.Shape)]
    [InlineData(SelectionPaneObjectKind.TextBox, DrawingObjectTargetKind.TextBox)]
    public void ToDrawingObjectTargetKind_MapsSelectionPaneDrawingKinds(
        SelectionPaneObjectKind selectionKind,
        DrawingObjectTargetKind expected) =>
        DrawingObjectCommandPlanner.ToDrawingObjectTargetKind(selectionKind).Should().Be(expected);

    [Fact]
    public void ToDrawingObjectTargetKind_ReturnsNullForNonDrawingSelectionKind() =>
        DrawingObjectCommandPlanner.ToDrawingObjectTargetKind(SelectionPaneObjectKind.Chart).Should().BeNull();

    private static (Workbook Workbook, Sheet Sheet, Guid Id) CreateWorkbook(DrawingObjectTargetKind kind)
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 2);

        return kind switch
        {
            DrawingObjectTargetKind.Picture => AddPicture(workbook, sheet, anchor),
            DrawingObjectTargetKind.Shape => AddShape(workbook, sheet, anchor),
            DrawingObjectTargetKind.TextBox => AddTextBox(workbook, sheet, anchor),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static (Workbook Workbook, Sheet Sheet, Guid Id) AddPicture(Workbook workbook, Sheet sheet, CellAddress anchor)
    {
        var picture = new PictureModel { Anchor = anchor, Width = 120, Height = 80 };
        sheet.Pictures.Add(picture);
        return (workbook, sheet, picture.Id);
    }

    private static (Workbook Workbook, Sheet Sheet, Guid Id) AddShape(Workbook workbook, Sheet sheet, CellAddress anchor)
    {
        var shape = new DrawingShapeModel { Anchor = anchor, Width = 120, Height = 80 };
        sheet.DrawingShapes.Add(shape);
        return (workbook, sheet, shape.Id);
    }

    private static (Workbook Workbook, Sheet Sheet, Guid Id) AddTextBox(Workbook workbook, Sheet sheet, CellAddress anchor)
    {
        var textBox = new TextBoxModel { Anchor = anchor, Width = 120, Height = 80, Text = "Text" };
        sheet.TextBoxes.Add(textBox);
        return (workbook, sheet, textBox.Id);
    }

    private static CellAddress GetAnchor(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => sheet.Pictures.Single(item => item.Id == id).Anchor,
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).Anchor,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).Anchor,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static (double Width, double Height, bool FlipHorizontal, bool FlipVertical) GetSize(
        Sheet sheet,
        DrawingObjectTargetKind kind,
        Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => ToSize(sheet.Pictures.Single(item => item.Id == id)),
            DrawingObjectTargetKind.Shape => ToSize(sheet.DrawingShapes.Single(item => item.Id == id)),
            DrawingObjectTargetKind.TextBox => ToSize(sheet.TextBoxes.Single(item => item.Id == id)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static (double Width, double Height, bool FlipHorizontal, bool FlipVertical) ToSize(PictureModel picture) =>
        (picture.Width, picture.Height, picture.FlipHorizontal, picture.FlipVertical);

    private static (double Width, double Height, bool FlipHorizontal, bool FlipVertical) ToSize(DrawingShapeModel shape) =>
        (shape.Width, shape.Height, shape.FlipHorizontal, shape.FlipVertical);

    private static (double Width, double Height, bool FlipHorizontal, bool FlipVertical) ToSize(TextBoxModel textBox) =>
        (textBox.Width, textBox.Height, textBox.FlipHorizontal, textBox.FlipVertical);

    private static double GetRotation(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => sheet.Pictures.Single(item => item.Id == id).RotationDegrees,
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).RotationDegrees,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).RotationDegrees,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static string? GetAltText(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => sheet.Pictures.Single(item => item.Id == id).AltText,
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).AltText,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).AltText,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
