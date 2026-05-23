using Freexcel.Core.Model;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

/// <summary>
/// Tests for named range storage, command, and formula evaluation.
/// </summary>
public class NamedRangeTests
{
    // ── Storage ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DefineNamedRange_StoresRange()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        wb.DefineNamedRange("MyData", range);

        wb.NamedRanges.Should().ContainKey("MyData");
        wb.NamedRanges["MyData"].Should().Be(range);
        wb.NamedRangeMetadataByName["MyData"].Should().Be(NamedRangeMetadata.WorkbookScope);
    }

    [Fact]
    public void DefineNamedRange_StoresScopeAndCommentMetadata()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        wb.DefineNamedRange("MyData", range, new NamedRangeMetadata("Sheet1", "Imported list"));

        wb.TryGetNamedRangeMetadata("MYDATA", out var metadata).Should().BeTrue();
        metadata.Should().Be(new NamedRangeMetadata("Sheet1", "Imported list"));
    }

    [Fact]
    public void DefineNamedRange_IsCaseInsensitive()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        wb.DefineNamedRange("mydata", range);

        wb.TryGetNamedRange("MYDATA", out var found).Should().BeTrue();
        found.Should().Be(range);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sales Total")]
    [InlineData("1Sales")]
    [InlineData("A1")]
    [InlineData("R1C1")]
    [InlineData("Sales-Total")]
    public void DefineNamedRange_InvalidExcelName_Throws(string name)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var act = () => wb.DefineNamedRange(name, range);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*name is invalid*");
    }

    [Fact]
    public void RemoveNamedRange_ReturnsTrueAndRemovesIt()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        wb.DefineNamedRange("TestRange", range);
        var removed = wb.RemoveNamedRange("TestRange");

        removed.Should().BeTrue();
        wb.NamedRanges.Should().NotContainKey("TestRange");
        wb.NamedRangeMetadataByName.Should().NotContainKey("TestRange");
    }

    [Fact]
    public void RemoveNamedRange_ReturnsFalseForUnknownName()
    {
        var wb = new Workbook();
        wb.RemoveNamedRange("DoesNotExist").Should().BeFalse();
    }

    [Fact]
    public void TryGetNamedRange_ReturnsFalseForUnknownName()
    {
        var wb = new Workbook();
        wb.TryGetNamedRange("DoesNotExist", out _).Should().BeFalse();
    }

    // ── Formula evaluation ────────────────────────────────────────────────────

    [Fact]
    public void NamedRange_UsableInFormula_SumWithNamedRange()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));

        var range = new GridRange(a1, a3);
        wb.DefineNamedRange("MyData", range);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(MyData)", sheet, wb);

        result.Should().Be(new NumberValue(6));
    }

    // ── Command ───────────────────────────────────────────────────────────────

    private static (Workbook wb, ICommandContext ctx) CreateContext()
    {
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        var ctx = new SimpleCommandContext(wb);
        return (wb, ctx);
    }

    [Fact]
    public void DefineNamedRangeCommand_Apply_StoresRange()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var cmd = new DefineNamedRangeCommand("Sales", range);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedRanges.Should().ContainKey("Sales");
        wb.NamedRangeMetadataByName["Sales"].Should().Be(NamedRangeMetadata.WorkbookScope);
    }

    [Fact]
    public void DefineNamedRangeCommand_Apply_StoresMetadata()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var cmd = new DefineNamedRangeCommand(
            "Sales",
            range,
            new NamedRangeMetadata("Sheet1", "Current period"));
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedRangeMetadataByName["Sales"].Should().Be(new NamedRangeMetadata("Sheet1", "Current period"));
    }

    [Fact]
    public void DefineNamedRangeCommand_Revert_RemovesName()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var cmd = new DefineNamedRangeCommand("Temp", range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedRanges.Should().NotContainKey("Temp");
        wb.NamedRangeMetadataByName.Should().NotContainKey("Temp");
    }

    [Fact]
    public void DefineNamedRangeCommand_Revert_RestoresPreviousRange_WhenReplacing()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var original = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        var replacement = new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 10, 10));

        // Define original first
        wb.DefineNamedRange("Budget", original, new NamedRangeMetadata("Sheet1", "Original"));

        // Replace via command
        var cmd = new DefineNamedRangeCommand("Budget", replacement);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.TryGetNamedRange("Budget", out var restored).Should().BeTrue();
        restored.Should().Be(original);
        wb.NamedRangeMetadataByName["Budget"].Should().Be(new NamedRangeMetadata("Sheet1", "Original"));
    }

    [Fact]
    public void DefineNamedRangeCommand_InvalidName_FailsWithoutStoringName()
    {
        var (wb, ctx) = CreateContext();
        var sheet = wb.Sheets[0];
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var outcome = new DefineNamedRangeCommand("Sales Total", range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("name is invalid");
        wb.NamedRanges.Should().BeEmpty();
    }

    [Fact]
    public void NamedRange_OnSheet2_ResolvedFromSheet1_Formula()
    {
        // Arrange: two sheets; named range defined on Sheet2; formula on Sheet1 references it
        var wb     = new Workbook("multi");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(7));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(8));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(9));

        wb.DefineNamedRange("CrossData", new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 3, 1)));

        // Act: evaluate =SUM(CrossData) on Sheet1 context
        var eval   = new FormulaEvaluator();
        var result = eval.Evaluate("=SUM(CrossData)", sheet1, wb);

        // Assert
        result.Should().Be(new NumberValue(24));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private sealed class SimpleCommandContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
