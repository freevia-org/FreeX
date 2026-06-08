using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_CrossSheetFilteredRangeCopiesFilterHiddenRowsByDefault()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(
            new CellAddress(sourceSheet.Id, 1, 1),
            new CellAddress(sourceSheet.Id, 3, 1));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("visible top"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("filter hidden"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 3, 1), new TextValue("visible bottom"));
        sourceSheet.FilterHiddenRows.Add(2);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            targetSheet.Id,
            sourceRange,
            CaptureCells(sourceSheet, sourceRange),
            new CellAddress(targetSheet.Id, 5, 3),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(new CellAddress(targetSheet.Id, 5, 3)).Should().Be(new TextValue("visible top"));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 6, 3)).Should().Be(new TextValue("filter hidden"));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 7, 3)).Should().Be(new TextValue("visible bottom"));
    }

    [Fact]
    public void PasteCommandFactory_CrossSheetStructuredTableDataBodyCopiesHiddenRowsAsRectangularPayload()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("Amount"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new TextValue("Next"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new NumberValue(10));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new NumberValue(11));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 3, 1), new NumberValue(20));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 3, 2), Cell.FromFormula("A3+1"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 4, 1), new NumberValue(30));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 4, 2), new NumberValue(31));
        sourceSheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sourceSheet.Id, 1, 1),
                new CellAddress(sourceSheet.Id, 4, 2)),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Amount"),
                new StructuredTableColumnModel(2, "Next")
            }
        });
        sourceSheet.FilterHiddenRows.Add(3);
        var dataBodyRange = new GridRange(
            new CellAddress(sourceSheet.Id, 2, 1),
            new CellAddress(sourceSheet.Id, 4, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            targetSheet.Id,
            dataBodyRange,
            CaptureCells(sourceSheet, dataBodyRange),
            new CellAddress(targetSheet.Id, 10, 4),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(new CellAddress(targetSheet.Id, 10, 4)).Should().Be(new NumberValue(10));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 11, 4)).Should().Be(new NumberValue(20));
        targetSheet.GetCell(new CellAddress(targetSheet.Id, 11, 5))!.FormulaText.Should().Be("D11+1");
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 12, 4)).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void PasteCommandFactory_CrossSheetPasteWritesHiddenDestinationRowsAndColumns()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(
            new CellAddress(sourceSheet.Id, 1, 1),
            new CellAddress(sourceSheet.Id, 2, 2));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("A"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new TextValue("B"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("C"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new TextValue("D"));
        targetSheet.HiddenRows.Add(6);
        targetSheet.HiddenCols.Add(4);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            targetSheet.Id,
            sourceRange,
            CaptureCells(sourceSheet, sourceRange),
            new CellAddress(targetSheet.Id, 5, 3),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(new CellAddress(targetSheet.Id, 5, 3)).Should().Be(new TextValue("A"));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 5, 4)).Should().Be(new TextValue("B"));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 6, 3)).Should().Be(new TextValue("C"));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 6, 4)).Should().Be(new TextValue("D"));
        targetSheet.HiddenRows.Should().Contain(6);
        targetSheet.HiddenCols.Should().Contain(4);
    }

    private static List<(CellAddress Source, Cell Cell)> CaptureCells(Sheet sheet, GridRange range) =>
        range
            .AllCells()
            .Select(address => (address, sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();
}
