using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    /// <summary>
    /// A CF rule whose AppliesTo spans A1:A1048576 (full column) must remain A1:A1048576
    /// after inserting a row above it.  Without clamping, End.Row would become 1048577 —
    /// an out-of-bounds value that Excel rejects on load.
    /// </summary>
    [Fact]
    public void InsertRow_FullColumnCfRange_ClampsEndRowAtMaxAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var fullColumn = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var rule = new ConditionalFormat
        {
            AppliesTo = fullColumn,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.ConditionalFormats.Add(rule);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        cmd.Apply(ctx);

        rule.AppliesTo.Start.Row.Should().Be(1, "start is above insertion point — unchanged");
        rule.AppliesTo.End.Row.Should().Be(CellAddress.MaxRow,
            "end must be clamped to MaxRow, not 1048577");

        cmd.Revert(ctx);

        rule.AppliesTo.End.Row.Should().Be(CellAddress.MaxRow,
            "undo restores the original snapshot (which was already MaxRow)");
    }

    /// <summary>
    /// A DV rule covering a full column must also be clamped on insert.
    /// </summary>
    [Fact]
    public void InsertRow_FullColumnDvRange_ClampsEndRowAtMaxAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var fullColumn = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        var rule = new DataValidation
        {
            AppliesTo = fullColumn,
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        sheet.DataValidations.Add(rule);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        cmd.Apply(ctx);

        rule.AppliesTo.End.Row.Should().Be(CellAddress.MaxRow,
            "end must be clamped to MaxRow after inserting rows");

        cmd.Revert(ctx);

        rule.AppliesTo.End.Row.Should().Be(CellAddress.MaxRow);
    }

    /// <summary>
    /// A named range that spans a full column must not overflow after insert.
    /// </summary>
    [Fact]
    public void InsertRow_FullColumnNamedRange_ClampsEndRowAtMaxAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var fullColumn = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
        wb.DefineNamedRange("FullCol", fullColumn);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["FullCol"].End.Row.Should().Be(CellAddress.MaxRow,
            "named range end must be clamped at MaxRow");

        cmd.Revert(ctx);

        wb.NamedRanges["FullCol"].End.Row.Should().Be(CellAddress.MaxRow);
    }
}
