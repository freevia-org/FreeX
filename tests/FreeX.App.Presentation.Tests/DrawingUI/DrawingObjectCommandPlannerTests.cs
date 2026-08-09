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

    [Theory]
    [InlineData(DrawingObjectTargetKind.Shape, true)]
    [InlineData(DrawingObjectTargetKind.Shape, false)]
    [InlineData(DrawingObjectTargetKind.TextBox, true)]
    [InlineData(DrawingObjectTargetKind.TextBox, false)]
    public void BuildFillColorCommand_UpdatesFillOrNoFillForShapesAndTextBoxes(
        DrawingObjectTargetKind kind,
        bool hasFill)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);
        var originalOutline = new CellColor(40, 50, 60);
        SetColors(sheet, kind, id, new CellColor(10, 20, 30), originalOutline);
        CellColor? fillColor = hasFill ? new CellColor(100, 110, 120) : null;

        var command = DrawingObjectCommandPlanner.BuildFillColorCommand(sheet.Id, kind, id, fillColor);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetHasFill(sheet, kind, id).Should().Be(hasFill);
        GetFillColor(sheet, kind, id).Should().Be(fillColor);
        GetOutlineColor(sheet, kind, id).Should().Be(originalOutline);
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Shape, true)]
    [InlineData(DrawingObjectTargetKind.Shape, false)]
    [InlineData(DrawingObjectTargetKind.TextBox, true)]
    [InlineData(DrawingObjectTargetKind.TextBox, false)]
    public void BuildOutlineColorCommand_UpdatesOutlineOrNoOutlineForShapesAndTextBoxes(
        DrawingObjectTargetKind kind,
        bool hasOutline)
    {
        var (workbook, sheet, id) = CreateWorkbook(kind);
        var originalFill = new CellColor(10, 20, 30);
        SetColors(sheet, kind, id, originalFill, new CellColor(40, 50, 60));
        CellColor? outlineColor = hasOutline ? new CellColor(100, 110, 120) : null;

        var command = DrawingObjectCommandPlanner.BuildOutlineColorCommand(sheet.Id, kind, id, outlineColor);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        GetHasFill(sheet, kind, id).Should().BeTrue();
        GetFillColor(sheet, kind, id).Should().Be(originalFill);
        GetOutlineColor(sheet, kind, id).Should().Be(outlineColor);
    }

    [Fact]
    public void BuildFillAndOutlineColorCommands_RejectPictures()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var id = Guid.NewGuid();

        Action fill = () => DrawingObjectCommandPlanner.BuildFillColorCommand(
            sheet.Id,
            DrawingObjectTargetKind.Picture,
            id,
            new CellColor(1, 2, 3));
        Action outline = () => DrawingObjectCommandPlanner.BuildOutlineColorCommand(
            sheet.Id,
            DrawingObjectTargetKind.Picture,
            id,
            null);

        fill.Should().Throw<ArgumentOutOfRangeException>();
        outline.Should().Throw<ArgumentOutOfRangeException>();
    }

    // R129-model-drawing-nudge-1: arrow-key nudge command family. Picture/Shape/TextBox accumulate
    // the pixel delta onto AnchorOffsetX/Y without touching the anchor cell (see the "why offset,
    // not anchor" comment on NudgeDrawingObjectCommands.cs); Chart has no anchor/offset pair at all
    // and gets the delta added directly to its Left/Top.
    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void BuildNudgeCommand_AccumulatesOffsetWithoutMovingAnchor(SelectionPaneObjectKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(ToTargetKind(kind));
        var originalAnchor = GetAnchor(sheet, ToTargetKind(kind), id);

        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(sheet.Id, kind, id, deltaX: 3, deltaY: -3);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        GetAnchor(sheet, ToTargetKind(kind), id).Should().Be(originalAnchor, "nudging must never re-anchor the object to a different cell");
        GetOffset(sheet, kind, id).Should().Be((3, -3));

        // A second nudge accumulates onto the first, matching repeated arrow-key presses.
        var second = DrawingObjectCommandPlanner.BuildNudgeCommand(sheet.Id, kind, id, deltaX: 1, deltaY: 1);
        second.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        GetOffset(sheet, kind, id).Should().Be((4, -2));
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void BuildNudgeCommand_Revert_RestoresPreviousOffset(SelectionPaneObjectKind kind)
    {
        var (workbook, sheet, id) = CreateWorkbook(ToTargetKind(kind));
        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(sheet.Id, kind, id, deltaX: 3, deltaY: -3);
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();

        command.Revert(ctx);

        GetOffset(sheet, kind, id).Should().Be((0, 0));
    }

    [Fact]
    public void BuildNudgeCommand_Chart_AddsDeltaToLeftAndTop()
    {
        var workbook = new Workbook("chart-nudge");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel { Left = 50, Top = 50, Width = 200, Height = 150 };
        sheet.Charts.Add(chart);

        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(sheet.Id, SelectionPaneObjectKind.Chart, chart.Id, deltaX: 3, deltaY: 1);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        chart.Left.Should().Be(53);
        chart.Top.Should().Be(51);
    }

    [Fact]
    public void BuildNudgeCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var workbook = new Workbook("locked-nudge");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2), Width = 120, Height = 80 };
        sheet.DrawingShapes.Add(shape);

        var command = DrawingObjectCommandPlanner.BuildNudgeCommand(sheet.Id, SelectionPaneObjectKind.Shape, shape.Id, deltaX: 3, deltaY: 0);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse("a locked shape on a protected sheet must reject the nudge exactly like Move/Resize do");
        shape.AnchorOffsetX.Should().Be(0);
    }

    private static DrawingObjectTargetKind ToTargetKind(SelectionPaneObjectKind kind) =>
        DrawingObjectCommandPlanner.ToDrawingObjectTargetKind(kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    private static (double OffsetX, double OffsetY) GetOffset(Sheet sheet, SelectionPaneObjectKind kind, Guid id) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => ToOffset(sheet.Pictures.Single(item => item.Id == id)),
            SelectionPaneObjectKind.Shape => ToOffset(sheet.DrawingShapes.Single(item => item.Id == id)),
            SelectionPaneObjectKind.TextBox => ToOffset(sheet.TextBoxes.Single(item => item.Id == id)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static (double OffsetX, double OffsetY) ToOffset(PictureModel picture) => (picture.AnchorOffsetX, picture.AnchorOffsetY);
    private static (double OffsetX, double OffsetY) ToOffset(DrawingShapeModel shape) => (shape.AnchorOffsetX, shape.AnchorOffsetY);
    private static (double OffsetX, double OffsetY) ToOffset(TextBoxModel textBox) => (textBox.AnchorOffsetX, textBox.AnchorOffsetY);

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

    private static void SetColors(
        Sheet sheet,
        DrawingObjectTargetKind kind,
        Guid id,
        CellColor? fillColor,
        CellColor? outlineColor)
    {
        switch (kind)
        {
            case DrawingObjectTargetKind.Shape:
                var shape = sheet.DrawingShapes.Single(item => item.Id == id);
                shape.HasFill = fillColor is not null;
                shape.FillColor = fillColor;
                shape.OutlineColor = outlineColor;
                break;
            case DrawingObjectTargetKind.TextBox:
                var textBox = sheet.TextBoxes.Single(item => item.Id == id);
                textBox.HasFill = fillColor is not null;
                textBox.FillColor = fillColor;
                textBox.OutlineColor = outlineColor;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static bool GetHasFill(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).HasFill,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).HasFill,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static CellColor? GetFillColor(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).FillColor,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).FillColor,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static CellColor? GetOutlineColor(Sheet sheet, DrawingObjectTargetKind kind, Guid id) =>
        kind switch
        {
            DrawingObjectTargetKind.Shape => sheet.DrawingShapes.Single(item => item.Id == id).OutlineColor,
            DrawingObjectTargetKind.TextBox => sheet.TextBoxes.Single(item => item.Id == id).OutlineColor,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
