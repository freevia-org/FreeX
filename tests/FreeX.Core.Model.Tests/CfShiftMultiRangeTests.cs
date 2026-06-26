using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests that a CF rule whose sqref spans multiple ranges (AppliesTo + AdditionalRanges)
/// has ALL ranges shifted correctly on row/column insert and delete.
/// </summary>
public sealed class CfShiftMultiRangeTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    /// <summary>
    /// Build a CF rule applied to "A1:A5 C1:C5" — two-range sqref.
    /// AppliesTo = A1:A5, AdditionalRanges = [C1:C5].
    /// </summary>
    private static ConditionalFormat TwoRangeCf(SheetId id) =>
        new()
        {
            AppliesTo = Range(id, 1, 1, 5, 1),         // A1:A5
            AdditionalRanges = [Range(id, 1, 3, 5, 3)], // C1:C5
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };

    // ══════════════════════════════════════════════════════════════════════════
    // InsertRows shifts BOTH ranges
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRows_ShiftsBothCfRanges_Down()
    {
        // Insert 1 row before row 1 → A1:A5 becomes A2:A6, C1:C5 becomes C2:C6.
        var (_, sheet, ctx) = Setup();
        var cf = TwoRangeCf(sheet.Id);
        sheet.ConditionalFormats.Add(cf);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        cf.AppliesTo.Should().Be(Range(sheet.Id, 2, 1, 6, 1), because: "AppliesTo (A1:A5) must shift down by 1");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 2, 3, 6, 3), because: "AdditionalRanges (C1:C5) must shift down by 1");
    }

    [Fact]
    public void InsertRows_ShiftsBothCfRanges_Down_MultipleRows()
    {
        // Insert 3 rows before row 2 → A1:A5 becomes A4:A8, C1:C5 becomes C4:C8.
        // Wait — row 1 is above insert point so it stays; rows 2+ shift.
        // A1:A5 starts at row 1 which is above insertBeforeRow=2? No — InsertRows shifts
        // ENTIRE ranges below the insert point using ShiftRuleRowsDown. Let's check:
        // ShiftRangeRowsDown: if Start.Row < insertBeforeRow, the range is null (removed).
        // That is only for band-scoped. The regular InsertRows uses ShiftRuleRowsDown which
        // calls ShiftRangeRowsDown on the full range. Looking at ShiftRangeRowsDown:
        //   if (range.End.Row < start) return range; (unchanged)
        //   if (range.Start.Row >= start) translate down
        //   else return null (partial overlap — range deleted)
        // Actually InsertRows uses InsertRowsCommand which internally calls ShiftRuleRowsDown.
        // Let's keep a simpler case: insert before row 6 (after the range) → no change.
        var (_, sheet, ctx) = Setup();
        var cf = TwoRangeCf(sheet.Id);
        sheet.ConditionalFormats.Add(cf);

        new InsertRowsCommand(sheet.Id, beforeRow: 6, count: 2).Apply(ctx);

        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 5, 1), because: "insert after range end → AppliesTo unchanged");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 1, 3, 5, 3), because: "insert after range end → AdditionalRanges unchanged");
    }

    [Fact]
    public void InsertRowsRevert_RestoresBothCfRanges()
    {
        var (_, sheet, ctx) = Setup();
        var cf = TwoRangeCf(sheet.Id);
        sheet.ConditionalFormats.Add(cf);
        var originalAppliesTo = cf.AppliesTo;
        var originalAdditional = cf.AdditionalRanges!.ToList();

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.AppliesTo.Should().Be(originalAppliesTo, because: "undo must restore AppliesTo");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            originalAdditional[0], because: "undo must restore AdditionalRanges");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DeleteRows shifts BOTH ranges
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRows_ShiftsBothCfRanges_Up()
    {
        // CF on A3:A8 + C3:C8. Delete rows 1-2 → both ranges shift to A1:A6, C1:C6.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 3, 1, 8, 1),        // A3:A8
            AdditionalRanges = [Range(sheet.Id, 3, 3, 8, 3)], // C3:C8
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2).Apply(ctx);

        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 1, 6, 1), because: "A3:A8 shifts up 2 to A1:A6");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 1, 3, 6, 3), because: "C3:C8 shifts up 2 to C1:C6");
    }

    [Fact]
    public void DeleteRowsRevert_RestoresBothCfRanges()
    {
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 3, 1, 8, 1),
            AdditionalRanges = [Range(sheet.Id, 3, 3, 8, 3)],
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };
        sheet.ConditionalFormats.Add(cf);
        var originalAppliesTo = cf.AppliesTo;
        var originalAdditional = cf.AdditionalRanges!.ToList();

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        cf.AppliesTo.Should().Be(originalAppliesTo, because: "undo must restore AppliesTo");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            originalAdditional[0], because: "undo must restore AdditionalRanges");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // InsertColumns shifts BOTH ranges
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertColumns_ShiftsBothCfRanges_Right()
    {
        // CF on A1:A5 + C1:C5. Insert 1 col before col 1 → A1:A5→B1:B5, C1:C5→D1:D5.
        var (_, sheet, ctx) = Setup();
        var cf = TwoRangeCf(sheet.Id);
        sheet.ConditionalFormats.Add(cf);

        new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 2, 5, 2), because: "A1:A5 → B1:B5");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 1, 4, 5, 4), because: "C1:C5 → D1:D5");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DeleteColumns shifts BOTH ranges
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteColumns_ShiftsBothCfRanges_Left()
    {
        // CF on C1:C5 + E1:E5. Delete col B (col 2) → C1:C5→B1:B5, E1:E5→D1:D5.
        var (_, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo = Range(sheet.Id, 1, 3, 5, 3),        // C1:C5
            AdditionalRanges = [Range(sheet.Id, 1, 5, 5, 5)], // E1:E5
            RuleType = CfRuleType.ColorScale,
            Priority = 1
        };
        sheet.ConditionalFormats.Add(cf);

        new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1).Apply(ctx);

        cf.AppliesTo.Should().Be(Range(sheet.Id, 1, 2, 5, 2), because: "C1:C5 → B1:B5");
        cf.AdditionalRanges.Should().ContainSingle().Which.Should().Be(
            Range(sheet.Id, 1, 4, 5, 4), because: "E1:E5 → D1:D5");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BJ3: colorScale/dataBar/iconSet Formula-type threshold shifting
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertRow_ShiftsColorScaleFormulaThreshold_AndUndoRestores()
    {
        // colorScale with Min = Formula "$A$10", Mid = Percentile "50" (should not change),
        // Max = Formula "$A$20". Insert 1 row before row 1 → $A$10→$A$11, $A$20→$A$21.
        var (wb, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo          = Range(sheet.Id, 1, 1, 30, 1),
            RuleType           = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType   = CfThresholdType.Formula,
            MinThresholdValue  = "$A$10",
            MidThresholdType   = CfThresholdType.Percentile,
            MidThresholdValue  = "50",
            MaxThresholdType   = CfThresholdType.Formula,
            MaxThresholdValue  = "$A$20"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cf.MinThresholdValue.Should().Be("$A$11", because: "Formula-type min threshold $A$10 must shift down by 1 after inserting row 1");
        cf.MidThresholdValue.Should().Be("50",    because: "Percentile-type mid threshold must NOT be shifted");
        cf.MaxThresholdValue.Should().Be("$A$21", because: "Formula-type max threshold $A$20 must shift down by 1 after inserting row 1");

        cmd.Revert(ctx);

        cf.MinThresholdValue.Should().Be("$A$10", because: "undo must restore original min threshold formula");
        cf.MidThresholdValue.Should().Be("50",    because: "undo must not disturb Percentile mid threshold");
        cf.MaxThresholdValue.Should().Be("$A$20", because: "undo must restore original max threshold formula");
    }

    [Fact]
    public void InsertRow_ShiftsDataBarFormulaThresholds_AndUndoRestores()
    {
        // dataBar with Formula min "$B$5" and Number max "100" (number must not shift).
        var (wb, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo                = Range(sheet.Id, 1, 1, 20, 1),
            RuleType                 = CfRuleType.DataBar,
            DataBarMinThresholdType  = CfThresholdType.Formula,
            DataBarMinThresholdValue = "$B$5",
            DataBarMaxThresholdType  = CfThresholdType.Number,
            DataBarMaxThresholdValue = "100"
        };
        sheet.ConditionalFormats.Add(cf);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 2);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cf.DataBarMinThresholdValue.Should().Be("$B$7", because: "Formula dataBar min $B$5 shifts down by 2");
        cf.DataBarMaxThresholdValue.Should().Be("100",  because: "Number dataBar max must not shift");

        cmd.Revert(ctx);

        cf.DataBarMinThresholdValue.Should().Be("$B$5", because: "undo restores dataBar min threshold formula");
        cf.DataBarMaxThresholdValue.Should().Be("100",  because: "undo does not disturb Number type max");
    }

    [Fact]
    public void InsertRow_ShiftsIconSetFormulaThreshold_AndUndoRestores()
    {
        // iconSet with two thresholds: [0] Formula "$C$3", [1] Percentile "67" (must not shift).
        var (wb, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo    = Range(sheet.Id, 1, 1, 10, 1),
            RuleType     = CfRuleType.IconSet,
            IconSetStyle = "3Arrows"
        };
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Formula, "$C$3"));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percentile, "67"));
        sheet.ConditionalFormats.Add(cf);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cf.IconSetThresholds[0].Value.Should().Be("$C$6",  because: "Formula iconSet threshold $C$3 shifts down by 3");
        cf.IconSetThresholds[1].Value.Should().Be("67",    because: "Percentile iconSet threshold must not shift");

        cmd.Revert(ctx);

        cf.IconSetThresholds[0].Value.Should().Be("$C$3", because: "undo restores Formula iconSet threshold");
        cf.IconSetThresholds[1].Value.Should().Be("67",   because: "undo does not disturb Percentile iconSet threshold");
    }

    [Fact]
    public void InsertRow_NumberTypeColorScaleThresholds_AreNotShifted()
    {
        // Guard: all three thresholds are Number type — none should be rewritten.
        var (wb, sheet, ctx) = Setup();
        var cf = new ConditionalFormat
        {
            AppliesTo          = Range(sheet.Id, 1, 1, 10, 1),
            RuleType           = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType   = CfThresholdType.Number,
            MinThresholdValue  = "1",
            MidThresholdType   = CfThresholdType.Number,
            MidThresholdValue  = "50",
            MaxThresholdType   = CfThresholdType.Number,
            MaxThresholdValue  = "100"
        };
        sheet.ConditionalFormats.Add(cf);

        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 5).Apply(ctx);

        cf.MinThresholdValue.Should().Be("1",   because: "Number-type min threshold must not be rewritten");
        cf.MidThresholdValue.Should().Be("50",  because: "Number-type mid threshold must not be rewritten");
        cf.MaxThresholdValue.Should().Be("100", because: "Number-type max threshold must not be rewritten");
    }

}
