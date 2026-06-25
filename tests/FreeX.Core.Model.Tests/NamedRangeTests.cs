using FreeX.Core.Model;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

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
        var ctx = new TestCommandContext(wb);
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

    // ── Sheet-scoped name collision support (Q13 fix) ────────────────────────

    [Fact]
    public void SheetScoped_And_WorkbookScoped_SameName_StoredWithoutCollision()
    {
        // Both a workbook-global 'Rate' and a Sheet2-scoped 'Rate' must coexist.
        var wb     = new Workbook("rate-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var rangeWb = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var rangeS2 = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1));

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(0.05));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(0.07));

        // Workbook-scoped name
        wb.DefineNamedRange("Rate", rangeWb);
        // Sheet2-scoped name with same name — must NOT overwrite workbook-scoped
        wb.DefineNamedRange("Rate", rangeS2, metadata: null, sheet2.Id);

        // Both must be present and distinct
        wb.TryGetNamedRange("Rate", out var globalRange).Should().BeTrue();
        globalRange.Should().Be(rangeWb);

        wb.ScopedNamedRanges.Should().ContainKey(("Rate", sheet2.Id));
        wb.ScopedNamedRanges[("Rate", sheet2.Id)].Should().Be(rangeS2);
    }

    [Fact]
    public void SheetScoped_ResolvesSheetLocal_WhenOnMatchingSheet()
    {
        // A formula on Sheet2 using 'Rate' resolves the Sheet2-scoped name (0.07), not the
        // workbook-global one (0.05). This is Excel's sheet-scope-first resolution rule.
        var wb     = new Workbook("rate-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(0.05));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(0.07));

        wb.DefineNamedRange("Rate",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1)));
        wb.DefineNamedRange("Rate",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null, sheet2.Id);

        var eval = new FormulaEvaluator();

        // Formula on Sheet2 → should resolve to Sheet2-scoped Rate = 0.07
        // (SUM wraps the named range to force scalar extraction)
        var resultOnSheet2 = eval.Evaluate("=SUM(Rate)", sheet2, wb);
        resultOnSheet2.Should().Be(new NumberValue(0.07));
    }

    [Fact]
    public void WorkbookScoped_ResolvesGlobal_WhenOnNonMatchingSheet()
    {
        // A formula on Sheet1 using 'Rate' falls back to the workbook-global name (0.05)
        // because there is no Sheet1-scoped 'Rate'.
        var wb     = new Workbook("rate-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(0.05));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(0.07));

        wb.DefineNamedRange("Rate",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1)));
        wb.DefineNamedRange("Rate",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null, sheet2.Id);

        var eval = new FormulaEvaluator();

        // Formula on Sheet1 → no Sheet1-scoped Rate; falls back to workbook-global = 0.05
        var resultOnSheet1 = eval.Evaluate("=SUM(Rate)", sheet1, wb);
        resultOnSheet1.Should().Be(new NumberValue(0.05));
    }

    [Fact]
    public void RemoveSheet_ClearsScopedNamesForThatSheet()
    {
        var wb     = new Workbook("cleanup-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));
        wb.DefineNamedRange("Temp",
            new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 1, 1)),
            metadata: null, sheet2.Id);

        wb.ScopedNamedRanges.Should().ContainKey(("Temp", sheet2.Id));

        // Removing Sheet2 must clean up its scoped names
        wb.RemoveSheet(sheet2.Id);

        wb.ScopedNamedRanges.Should().NotContainKey(("Temp", sheet2.Id));
        _ = sheet1; // suppress unused
    }

    [Fact]
    public void SheetScopedNamedFormula_ResolvesSheetLocal_WhenOnMatchingSheet()
    {
        // A named formula scoped to Sheet2 resolves to the sheet-local value.
        var wb     = new Workbook("formula-scope-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        // Workbook-global constant formula
        wb.NamedFormulas["MyConst"] = "100";
        // Sheet2-scoped override
        wb.DefineNamedFormula("MyConst", "200", sheet2.Id);

        var eval = new FormulaEvaluator();

        // On Sheet2: sheet-scoped formula wins
        eval.Evaluate("=MyConst", sheet2, wb).Should().Be(new NumberValue(200));
        // On Sheet1: falls back to workbook-global
        eval.Evaluate("=MyConst", sheet1, wb).Should().Be(new NumberValue(100));
    }

    [Fact]
    public void TryGetNamedRange_SheetAware_FindsScopedFirst()
    {
        var wb     = new Workbook("api-test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var rangeGlobal = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var rangeScoped = new GridRange(new CellAddress(sheet2.Id, 2, 2), new CellAddress(sheet2.Id, 2, 2));

        wb.DefineNamedRange("X", rangeGlobal);
        wb.DefineNamedRange("X", rangeScoped, metadata: null, sheet2.Id);

        // Sheet2 context → scoped wins
        wb.TryGetNamedRange("X", sheet2.Id, out var found2).Should().BeTrue();
        found2.Should().Be(rangeScoped);

        // Sheet1 context → no scoped, falls back to global
        wb.TryGetNamedRange("X", sheet1.Id, out var found1).Should().BeTrue();
        found1.Should().Be(rangeGlobal);
    }
}
