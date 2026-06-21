using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingInsertionPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void DefaultShape_IsRectangle() =>
        DrawingInsertionPlanner.DefaultShape.Should().Be(DrawingShapeKind.Rectangle);

    [Fact]
    public void ShapeCatalog_IsGroupedAndCoversDefinedKinds()
    {
        DrawingInsertionPlanner.ShapeGroups.Should().NotBeEmpty();
        DrawingInsertionPlanner.ShapeGroups.SelectMany(group => group.Items).Should().OnlyContain(item => Enum.IsDefined(item.Kind));
        DrawingInsertionPlanner.ShapeGroups.Select(group => group.Label).Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label));
        DrawingInsertionPlanner.ShapeItems.Select(item => item.Label).Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label));
    }

    [Fact]
    public void ShapeCatalog_ExposesExcelLikeGroupsForBothRenderers()
    {
        DrawingInsertionPlanner.ShapeGroups.Select(group => group.Label)
            .Should().Equal(
                "Lines",
                "Rectangles",
                "Basic Shapes",
                "Block Arrows",
                "Equation Shapes",
                "Flowchart",
                "Stars and Banners",
                "Callouts");
    }

    [Theory]
    [InlineData(DrawingShapeKind.Rectangle)]
    [InlineData(DrawingShapeKind.Ellipse)]
    [InlineData(DrawingShapeKind.Line)]
    [InlineData(DrawingShapeKind.Star5)]
    public void BuildShapeCommand_AddsShapeOfRequestedKind_OnApply(DrawingShapeKind kind)
    {
        var workbook = new Workbook("Shapes");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = DrawingInsertionPlanner.BuildShapeCommand(sheet.Id, anchor, kind);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.DrawingShapes.Should().ContainSingle();
        var shape = sheet.DrawingShapes[0];
        shape.Kind.Should().Be(kind);
        shape.Anchor.Should().Be(anchor);
        shape.Width.Should().Be(DrawingInsertionPlanner.DefaultShapeWidth);
        shape.Height.Should().Be(DrawingInsertionPlanner.DefaultShapeHeight);
    }

    [Fact]
    public void BuildTextBoxCommand_BlankText_UsesPlaceholder_AndAddsTextBoxOnApply()
    {
        var workbook = new Workbook("TB");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);

        var command = DrawingInsertionPlanner.BuildTextBoxCommand(sheet.Id, anchor, text: "   ");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.TextBoxes.Should().ContainSingle();
        var box = sheet.TextBoxes[0];
        box.Anchor.Should().Be(anchor);
        box.Text.Should().Be(DrawingInsertionPlanner.TextBoxPlaceholder);
        box.Width.Should().Be(DrawingInsertionPlanner.DefaultTextBoxWidth);
        box.Height.Should().Be(DrawingInsertionPlanner.DefaultTextBoxHeight);
    }

    [Fact]
    public void BuildTextBoxCommand_GivenText_IsTrimmedAndUsed()
    {
        var workbook = new Workbook("TB");
        var sheet = workbook.AddSheet("Sheet1");

        var command = DrawingInsertionPlanner.BuildTextBoxCommand(
            sheet.Id, new CellAddress(sheet.Id, 1, 1), text: "  Hello  ");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.TextBoxes[0].Text.Should().Be("Hello");
    }

    [Fact]
    public void BuildInlineEditTextBoxCommand_KeepsBlankTextForRendererInlineEditing()
    {
        var workbook = new Workbook("TB");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 4, 2);

        var command = DrawingInsertionPlanner.BuildInlineEditTextBoxCommand(sheet.Id, anchor);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.TextBoxes.Should().ContainSingle();
        sheet.TextBoxes[0].Text.Should().BeEmpty();
    }
}
