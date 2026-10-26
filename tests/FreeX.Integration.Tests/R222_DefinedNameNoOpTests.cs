using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r222: the three commands in the Add/Create/Define family that have a same-value path. The Name
/// Manager's Edit dialog pre-fills the current Refers To, comment and hidden flag, so pressing OK
/// unchanged redefines a name to exactly what it already is; and Create from Selection is idempotent
/// by nature, so running it twice on the same labelled block re-defines every name to the range it
/// already has.
/// <para>
/// The interesting part is that the four branches involved -- range vs formula, global vs scoped --
/// needed four DIFFERENT guards, and reading each one beat assuming they matched:
/// </para>
/// <list type="bullet">
/// <item>The global RANGE branch removes the key before re-adding it, specifically so a case-only
/// rename takes effect. Its guard therefore compares the stored key ORDINALLY; without that clause
/// it would swallow a rename the user asked for.</item>
/// <item>The scoped range branch assigns through a case-insensitive comparer without removing, so it
/// cannot re-case a key and needs no such clause.</item>
/// <item>Defining a range DELETES a colliding named formula as a side effect (and vice versa), so
/// each guard carries a clause for the other kind.</item>
/// <item>Null metadata means "write WorkbookScope" for ranges and "leave what is stored untouched"
/// for formulas -- the same argument with opposite meanings, so the two guards cannot share a
/// shape.</item>
/// </list>
/// </summary>
public sealed class R222_DefinedNameNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint fromRow, uint fromCol, uint toRow, uint toCol) =>
        new(new CellAddress(sheet.Id, fromRow, fromCol), new CellAddress(sheet.Id, toRow, toCol));

    [Fact]
    public void RedefiningAGlobalNameToTheRangeItAlreadyHas_ReportsNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        var range = Range(sheet, 1, 1, 5, 2);
        workbook.DefineNamedRange("Revenue", range, null);

        new DefineNamedRangeCommand("Revenue", range, allowRedefine: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RedefiningAGlobalNameToADifferentRange_DoesNotReportNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        workbook.DefineNamedRange("Revenue", Range(sheet, 1, 1, 5, 2), null);

        var outcome = new DefineNamedRangeCommand(
            "Revenue", Range(sheet, 1, 1, 9, 2), allowRedefine: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ChangingOnlyTheCaseOfAGlobalName_IsARealRename()
    {
        // The clause that would have been easy to leave out. Workbook.DefineNamedRange removes the
        // key before re-adding it precisely so this works, so a guard on the range alone would have
        // reported no-op and silently kept the old casing.
        var (workbook, sheet, ctx) = Fixture();
        var range = Range(sheet, 1, 1, 5, 2);
        workbook.DefineNamedRange("revenue", range, null);

        var outcome = new DefineNamedRangeCommand("Revenue", range, allowRedefine: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        workbook.NamedRanges.Keys.Should().Contain("Revenue");
    }

    [Fact]
    public void RedefiningARangeOverACollidingFormulaName_IsARealChange()
    {
        // Defining a range deletes a colliding named formula. Same range in, but a definition
        // disappears -- so this must not be reported as nothing happening.
        var (workbook, sheet, ctx) = Fixture();
        var range = Range(sheet, 1, 1, 5, 2);
        workbook.DefineNamedRange("Revenue", range, null);
        workbook.NamedFormulas["Revenue"] = "=1+1";

        var outcome = new DefineNamedRangeCommand("Revenue", range, allowRedefine: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        workbook.NamedFormulas.Should().NotContainKey("Revenue");
    }

    [Fact]
    public void RedefiningAGlobalNameWithChangedMetadata_DoesNotReportNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        var range = Range(sheet, 1, 1, 5, 2);
        workbook.DefineNamedRange("Revenue", range, null);

        var outcome = new DefineNamedRangeCommand(
                "Revenue",
                range,
                new NamedRangeMetadata("Workbook", "Quarterly revenue"),
                allowRedefine: true)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse("the comment is part of the definition");
    }

    [Fact]
    public void RedefiningAScopedNameToTheRangeItAlreadyHas_ReportsNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        var range = Range(sheet, 1, 1, 5, 2);
        workbook.DefineNamedRange("Local", range, null, sheet.Id);

        new DefineNamedRangeCommand("Local", range, scopeSheetId: sheet.Id, allowRedefine: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RedefiningANamedFormulaToTheTextItAlreadyHas_ReportsNoOp()
    {
        var (workbook, _, ctx) = Fixture();
        workbook.NamedFormulas["Rate"] = "=0.2";

        new DefineNamedFormulaCommand("Rate", "=0.2").Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RedefiningANamedFormulaToDifferentText_DoesNotReportNoOp()
    {
        var (workbook, _, ctx) = Fixture();
        workbook.NamedFormulas["Rate"] = "=0.2";

        var outcome = new DefineNamedFormulaCommand("Rate", "=0.25").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        workbook.NamedFormulas["Rate"].Should().Be("=0.25");
    }

    [Fact]
    public void RunningCreateFromSelectionTwice_ReportsNoOpTheSecondTime()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));

        var first = new CreateNamedRangesFromSelectionCommand(
            Range(sheet, 1, 1, 3, 1), UseTopRow: true, UseLeftColumn: false,
            UseBottomRow: false, UseRightColumn: false).Apply(ctx);
        first.IsNoOp.Should().BeFalse("the first run defines the name");

        new CreateNamedRangesFromSelectionCommand(
                Range(sheet, 1, 1, 3, 1), UseTopRow: true, UseLeftColumn: false,
                UseBottomRow: false, UseRightColumn: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue("the second run redefines it to what it already is");
    }
}
