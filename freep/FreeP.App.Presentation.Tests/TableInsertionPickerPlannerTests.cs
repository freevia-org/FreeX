using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TableInsertionPickerPlannerTests
{
    [Fact]
    public void BuildPlan_ReturnsPowerPointStyleGridWithDefaultChoice()
    {
        var plan = TableInsertionPickerPlanner.BuildPlan();

        plan.MaxRows.Should().Be(5);
        plan.MaxColumns.Should().Be(5);
        plan.Choices.Should().HaveCount(25);
        plan.Choices.Should().ContainSingle(choice =>
            choice.Rows == 3 &&
            choice.Columns == 3 &&
            choice.IsDefault &&
            choice.Label == "3 x 3 Table" &&
            choice.DisplayLabel == "3 x 3 Table (default)" &&
            choice.AutomationId == "table-3x3");
        plan.Choices.First().Label.Should().Be("1 x 1 Table");
        plan.Choices.First().DisplayLabel.Should().Be("1 x 1 Table");
        plan.Choices.First().AutomationId.Should().Be("table-1x1");
        plan.Choices.Last().Label.Should().Be("5 x 5 Table");
    }

    [Fact]
    public void TryApplyChoice_InsertsSelectedTableSizeThroughEditingSession()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        TableInsertionPickerPlanner.TryApplyChoice(editor, rows: 4, columns: 5).Should().BeTrue();

        var table = editor.CurrentSlide!.Shapes.Single(shape => shape.Kind == SlideShapeKind.Table).Table;
        table.Should().NotBeNull();
        table!.Rows.Should().HaveCount(4);
        table.ColumnWidthsEmu.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(11, 1)]
    [InlineData(1, 11)]
    public void TryApplyChoice_RejectsOutOfRangeSizes(int rows, int columns)
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        TableInsertionPickerPlanner.TryApplyChoice(editor, rows, columns).Should().BeFalse();

        editor.CurrentSlide!.Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Table);
    }
}
