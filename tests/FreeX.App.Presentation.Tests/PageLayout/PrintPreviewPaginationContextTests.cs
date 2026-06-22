using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewPaginationContextTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void TryCreate_EmptySheetReturnsFalse()
    {
        var (workbook, sheet) = CreateBook();

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_SingleCellSheetHasOnePage()
    {
        var (workbook, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        context.PageCount.Should().Be(1);
    }

    [Fact]
    public void TryCreate_WideTallRangeProducesMultiplePages()
    {
        var (workbook, sheet) = CreateBook();
        for (uint r = 1; r <= 200; r += 50)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 400, 60));

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        context.PageCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public void BuildPage_OutOfRangeIndexReturnsNull()
    {
        var (workbook, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();

        context.BuildPage(context.PageCount).Should().BeNull();
        context.BuildPage(-1).Should().BeNull();
    }

    [Fact]
    public void BuildPage_ProducesLayoutThatFlattensToInstructions()
    {
        var (workbook, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apples"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();

        var layout = context.BuildPage(0);

        layout.Should().NotBeNull();
        var painting = PrintPreviewInstructionBuilder.Build(layout!);

        painting.Instructions.Should().NotBeEmpty();
        painting.Instructions[0].Kind.Should().Be(PrintPreviewPaintKind.Rectangle);
        painting.Instructions.Should().Contain(i => i.Kind == PrintPreviewPaintKind.Line);
        painting.Instructions.Should().Contain(i =>
            i.Kind == PrintPreviewPaintKind.Text && i.Text == "Apples");
        painting.PageNumber.Should().Be(1);
    }
}
