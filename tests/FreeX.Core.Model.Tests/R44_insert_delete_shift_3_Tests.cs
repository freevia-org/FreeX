using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-44 fixes for the "insert-delete-shift" bucket:
/// <list type="bullet">
/// <item>
/// R44-commands-insert-delete-shift-3-1 (RowColumnShiftHelpers.Rules.cs): deleting rows/columns
/// that fully consume a multi-area CF/DV rule's PRIMARY area must shrink the rule's sqref to
/// whatever AdditionalRanges area survived, instead of dropping the whole rule.
/// </item>
/// <item>
/// R44-commands-insert-delete-shift-3-2 (RowColumnShiftHelpers.NamedRanges.cs): deleting the
/// rows/columns a plain defined name's range fully spans must leave the name defined with a
/// #REF! error (moved into Workbook.NamedFormulas), matching how a cell formula referencing the
/// same deleted rows becomes #REF!, instead of removing the name outright.
/// </item>
/// <item>
/// R44-commands-insert-delete-shift-3-3 (RowColumnShiftHelpers.PrintAndCharts.cs): deleting all
/// rows/columns a chart's DataRange spans must not leave DataRange pointing at its stale
/// pre-delete coordinates (which would alias whatever data shifted into that window) — it must
/// collapse to a single row/column at the delete boundary instead.
/// </item>
/// </list>
/// </summary>
public sealed class R44_insert_delete_shift_3_Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // ══════════════════════════════════════════════════════════════════════════
    // R44-commands-insert-delete-shift-3-1: multi-area CF/DV rule promotion
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_MultiAreaDv_PrimaryConsumed_PromotesSurvivingAdditionalRange()
    {
        // DV on A1:A5 (primary) + C10:C15 (additional). Deleting rows 1-5 fully consumes A1:A5
        // but only shifts C10:C15 up to C5:C10 — the rule must survive with AppliesTo promoted
        // to the surviving area, not be dropped outright.
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation { AppliesTo = Range(sheet.Id, 1, 1, 5, 1), Type = DvType.WholeNumber };
        dv.AdditionalRanges.Add(Range(sheet.Id, 10, 3, 15, 3));
        sheet.DataValidations.Add(dv);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(because: "the rule must survive since C10:C15 was not fully consumed");
        var survivor = sheet.DataValidations[0];
        survivor.Should().BeSameAs(dv);
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 5, 3, 10, 3), because: "C10:C15 shifts up 5 to C5:C10 and is promoted to AppliesTo");
        survivor.AdditionalRanges.Should().BeEmpty();
    }

    [Fact]
    public void DeleteRows_MultiAreaDv_Undo_RestoresOriginalPrimaryAndAdditionalRanges()
    {
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation { AppliesTo = Range(sheet.Id, 1, 1, 5, 1), Type = DvType.WholeNumber };
        dv.AdditionalRanges.Add(Range(sheet.Id, 10, 3, 15, 3));
        sheet.DataValidations.Add(dv);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle();
        sheet.DataValidations[0].AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "undo restores the original primary area");
        sheet.DataValidations[0].AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 10, 3, 15, 3), because: "undo restores the original additional area");
    }

    [Fact]
    public void DeleteRows_MultiAreaCf_PrimaryConsumed_PromotesSurvivingAdditionalRange()
    {
        // Same scenario as the DV test but for a conditional-formatting rule.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),
            AdditionalRanges = [Range(sheet.Id, 10, 3, 15, 3)],
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5).Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().ContainSingle(because: "the rule must survive since C10:C15 was not fully consumed");
        var survivor = sheet.ConditionalFormats[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 5, 3, 10, 3), because: "C10:C15 shifts up 5 to C5:C10 and is promoted to AppliesTo");
        survivor.AdditionalRanges.Should().BeNull();
    }

    [Fact]
    public void DeleteRows_SingleAreaDvAndCf_PrimaryConsumed_NoSurvivors_RuleStillRemoved()
    {
        // Sibling no-regression: when the rule has no other surviving area, it must still be
        // removed entirely (the pre-existing correct behavior for the simple single-area case).
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation { AppliesTo = Range(sheet.Id, 1, 1, 5, 1), Type = DvType.WholeNumber };
        sheet.DataValidations.Add(dv);
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 1, 5, 1),
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().BeEmpty(because: "no additional area survived, so the DV rule must be dropped");
        sheet.ConditionalFormats.Should().BeEmpty(because: "no additional area survived, so the CF rule must be dropped");
    }

    [Fact]
    public void DeleteCellsShiftUp_BandScoped_MultiAreaDv_PrimaryConsumed_PromotesSurvivingAdditionalRange()
    {
        // Band-scoped Delete-Cells-Shift-Up path (AdjustRulesDeleteShiftUp), distinct from the
        // whole-row DeleteRowsCommand path above but the same underlying bug.
        var (_, sheet, ctx) = Setup();
        var dv = new DataValidation { AppliesTo = Range(sheet.Id, 1, 1, 5, 1), Type = DvType.WholeNumber }; // A1:A5
        dv.AdditionalRanges.Add(Range(sheet.Id, 10, 1, 15, 1)); // A10:A15
        sheet.DataValidations.Add(dv);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 5, 1), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(because: "A10:A15 shifted into the band and survived");
        var survivor = sheet.DataValidations[0];
        survivor.AppliesTo.Should().Be(Range(sheet.Id, 5, 1, 10, 1), because: "A10:A15 shifts up 5 to A5:A10 and is promoted to AppliesTo");
        survivor.AdditionalRanges.Should().BeEmpty();

        cmd.Revert(ctx);
        sheet.DataValidations.Should().ContainSingle();
        sheet.DataValidations[0].AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "undo restores the original primary area");
        sheet.DataValidations[0].AdditionalRanges.Should().ContainSingle().Which.Should().Be(Range(sheet.Id, 10, 1, 15, 1));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R44-commands-insert-delete-shift-3-2: plain named range -> #REF! on full consumption
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_PlainNamedRange_FullyConsumed_BecomesRefErrorNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("MyRange", Range(sheet.Id, 1, 1, 5, 1)); // A1:A5

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5).Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges.Should().NotContainKey("MyRange", because: "the range no longer exists");
        wb.NamedFormulas.Should().ContainKey("MyRange", because: "Excel keeps the name defined, now referring to #REF!");
        wb.NamedFormulas["MyRange"].Should().Be("#REF!");
    }

    [Fact]
    public void DeleteRows_PlainNamedRange_FullyConsumed_Undo_RestoresRangeWithoutStrayRefFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("MyRange", Range(sheet.Id, 1, 1, 5, 1));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        wb.NamedRanges.Should().ContainKey("MyRange");
        wb.NamedRanges["MyRange"].Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "undo restores the original range");
        wb.NamedFormulas.Should().NotContainKey("MyRange", because: "undo must not leave a stray #REF! formula entry behind");
    }

    [Fact]
    public void DeleteColumns_PlainNamedRange_FullyConsumed_BecomesRefErrorNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("MyCols", Range(sheet.Id, 1, 1, 5, 3)); // A1:C5

        new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 3).Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges.Should().NotContainKey("MyCols");
        wb.NamedFormulas.Should().ContainKey("MyCols");
        wb.NamedFormulas["MyCols"].Should().Be("#REF!");
    }

    [Fact]
    public void DeleteRows_PlainNamedRange_PartialOverlap_StillShiftsNormally()
    {
        // Sibling no-regression: when the name's range survives (partially or fully outside the
        // deleted band), the existing shift-and-keep-as-range behavior must be unaffected.
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("MyRange", Range(sheet.Id, 3, 1, 8, 1)); // A3:A8

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2).Apply(ctx).Success.Should().BeTrue();

        wb.NamedRanges.Should().ContainKey("MyRange");
        wb.NamedRanges["MyRange"].Should().Be(Range(sheet.Id, 1, 1, 6, 1), because: "A3:A8 shifts up 2 to A1:A6");
        wb.NamedFormulas.Should().NotContainKey("MyRange");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R44-commands-insert-delete-shift-3-3: chart DataRange collapse on full consumption
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_ChartDataRange_FullyConsumed_CollapsesInsteadOfStayingStale()
    {
        var (wb, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 10, 2) }; // A1:B10
        sheet.Charts.Add(chart);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 10).Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.RowCount.Should().Be(1u, because: "no row of the original A1:B10 survives, so it must collapse rather than stay a 10-row stale range");
        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 1, 2), because: "collapses to a single row at the delete boundary, keeping the original column span A:B");
    }

    [Fact]
    public void DeleteColumns_ChartDataRange_FullyConsumed_CollapsesInsteadOfStayingStale()
    {
        var (wb, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 5, 3) }; // A1:C5
        sheet.Charts.Add(chart);

        new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 3).Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.ColCount.Should().Be(1u, because: "no column of the original A1:C5 survives, so it must collapse rather than stay a 3-column stale range");
        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "collapses to a single column at the delete boundary, keeping the original row span 1:5");
    }

    [Fact]
    public void DeleteRows_ChartDataRange_PartialOverlap_StillShrinksToSurvivingPortion()
    {
        // Sibling no-regression: the existing correct partial-overlap shrink path (a real,
        // non-null ShiftRangeRowsDown result) must be unaffected by the null-branch fix.
        var (wb, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 10, 2) }; // A1:B10
        sheet.Charts.Add(chart);

        new DeleteRowsCommand(sheet.Id, startRow: 5, count: 11).Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 4, 2), because: "rows 5-10 of A1:B10 are deleted, leaving the surviving A1:B4 prefix");
    }

    [Fact]
    public void DeleteRows_ChartDataRange_EntirelyBelowDeletedBand_StillShiftsUpNormally()
    {
        // Sibling no-regression: the ordinary (non-null) shift-down path is unaffected.
        var (wb, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 5, 1, 10, 2) }; // A5:B10
        sheet.Charts.Add(chart);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2).Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 3, 1, 8, 2), because: "A5:B10 shifts up 2 to A3:B8");
    }

    [Fact]
    public void DeleteRows_ChartDataRange_FullyConsumed_Undo_RestoresOriginalRange()
    {
        var (wb, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 10, 2) }; // A1:B10
        sheet.Charts.Add(chart);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 10);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 10, 2), because: "undo restores the original pre-delete DataRange");
    }
}
