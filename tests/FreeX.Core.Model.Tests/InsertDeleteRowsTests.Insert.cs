using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    [Fact]
    public void InsertRow_ShiftsCellsDown()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1).Apply(ctx);

        sheet.GetValue(4, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRowRevert_RestoresOriginalState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(4, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRowRevert_RestoresCapturedCellStateAfterShiftedCellMutates()
    {
        var (wb, sheet, ctx) = Setup();
        var style = wb.RegisterStyle(new CellStyle { Bold = true });
        var cachedAst = new object();
        var original = new Cell
        {
            Value = new NumberValue(100),
            IgnoreFormulaError = true,
            StyleId = style
        };
        original.FormulaText = "A1+1";
        original.CachedAst = cachedAst;
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), original);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3);

        cmd.Apply(ctx).Success.Should().BeTrue();
        var shifted = sheet.GetCell(4, 1)!;
        shifted.Value = new TextValue("mutated");
        shifted.FormulaText = null;
        shifted.CachedAst = null;
        shifted.IgnoreFormulaError = false;
        shifted.StyleId = StyleId.Default;

        cmd.Revert(ctx);

        var restored = sheet.GetCell(3, 1)!;
        restored.Should().NotBeSameAs(shifted);
        restored.Value.Should().Be(new NumberValue(100));
        restored.FormulaText.Should().Be("A1+1");
        restored.CachedAst.Should().BeSameAs(cachedAst);
        restored.IgnoreFormulaError.Should().BeTrue();
        restored.StyleId.Should().Be(style);
        sheet.GetCell(4, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRow_ShiftsCustomRowHeightsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[3] = 30;
        sheet.RowHeights[5] = 45;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowHeights.Should().NotContainKey(3);
        sheet.RowHeights.Should().NotContainKey(4);
        sheet.RowHeights[5].Should().Be(30);
        sheet.RowHeights[7].Should().Be(45);

        cmd.Revert(ctx);

        sheet.RowHeights[3].Should().Be(30);
        sheet.RowHeights[5].Should().Be(45);
        sheet.RowHeights.Should().NotContainKey(7);
    }

    // R136-io-worksheet-props-col-row-default-style-shift: sheet.RowStyles is the same
    // absolute-row key space as RowHeights and must re-key the same way on insert, or a
    // whole-row default style lands on the wrong (stale-indexed) row after the shift.
    [Fact]
    public void InsertRow_ShiftsRowStylesAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var bannerStyle = new StyleId(1);
        var totalsStyle = new StyleId(2);
        sheet.RowStyles[3] = bannerStyle;
        sheet.RowStyles[5] = totalsStyle;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowStyles.Should().NotContainKey(3);
        sheet.RowStyles.Should().NotContainKey(4);
        sheet.RowStyles[5].Should().Be(bannerStyle);
        sheet.RowStyles[7].Should().Be(totalsStyle);

        cmd.Revert(ctx);

        sheet.RowStyles[3].Should().Be(bannerStyle);
        sheet.RowStyles[5].Should().Be(totalsStyle);
        sheet.RowStyles.Should().NotContainKey(7);
    }

    // Insert BEFORE the styled row (styled row ends up further down, at a higher key).
    [Fact]
    public void InsertRow_BeforeStyledRow_ShiftsRowStyleDownAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var bannerStyle = new StyleId(1);
        sheet.RowStyles[4] = bannerStyle;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        cmd.Apply(ctx);

        sheet.RowStyles.Should().NotContainKey(4);
        sheet.RowStyles[5].Should().Be(bannerStyle);

        cmd.Revert(ctx);

        sheet.RowStyles[4].Should().Be(bannerStyle);
        sheet.RowStyles.Should().NotContainKey(5);
    }

    // Insert AFTER the styled row (styled row is untouched by the shift).
    [Fact]
    public void InsertRow_AfterStyledRow_LeavesRowStyleInPlaceAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var bannerStyle = new StyleId(1);
        sheet.RowStyles[2] = bannerStyle;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 1);
        cmd.Apply(ctx);

        sheet.RowStyles[2].Should().Be(bannerStyle);

        cmd.Revert(ctx);

        sheet.RowStyles[2].Should().Be(bannerStyle);
    }

    [Fact]
    public void InsertRow_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 2);
        var shifted = new CellAddress(sheet.Id, 5, 2);
        sheet.Comments[original] = "Check this";

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(original);
        sheet.Comments[shifted].Should().Be("Check this");

        cmd.Revert(ctx);

        sheet.Comments[original].Should().Be("Check this");
        sheet.Comments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertRow_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 2);
        var shifted = new CellAddress(sheet.Id, 5, 2);
        sheet.ThreadedComments[original] = new ThreadedComment("Check this", "Quinn");

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(original);
        sheet.ThreadedComments[shifted].Should().Be(new ThreadedComment("Check this", "Quinn"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[original].Should().Be(new ThreadedComment("Check this", "Quinn"));
        sheet.ThreadedComments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertRow_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 6, 2)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Row.Should().Be(7);
        validation.AppliesTo.End.Row.Should().Be(8);
        format.AppliesTo.Start.Row.Should().Be(7);
        format.AppliesTo.End.Row.Should().Be(8);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Row.Should().Be(5);
        validation.AppliesTo.End.Row.Should().Be(6);
        format.AppliesTo.Start.Row.Should().Be(5);
        format.AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(7);
        wb.NamedRanges["Sales"].End.Row.Should().Be(8);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(5);
        wb.NamedRanges["Sales"].End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 3));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(7);
        sheet.PrintArea.Value.End.Row.Should().Be(8);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(5);
        sheet.PrintArea.Value.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsRowPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowPageBreaks.Add(3);
        sheet.RowPageBreaks.Add(8);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowPageBreaks.Should().Equal(5u, 10u);

        cmd.Revert(ctx);

        sheet.RowPageBreaks.Should().Equal(3u, 8u);
    }

    [Fact]
    public void InsertRow_ShiftsFilterHiddenRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FilterHiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(5);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 5u, 7u });

        cmd.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 3u, 5u });
    }

    [Fact]
    public void InsertRows_WhenDataWouldBePushedPastMaxRow_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, 1), new NumberValue(1));

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pushed past the last row");
    }

    [Fact]
    public void InsertRowRevert_DataValidationLookupCacheIsRefreshedSoValidationAppliesToOriginalAddress()
    {
        // Regression: RestoreRuleRanges mutated rule.AppliesTo in-place without bumping
        // DataValidationCollection.Version, so DataValidationLookupCache.RefreshIfNeeded kept
        // the pre-undo index and GetApplicable returned nothing at the restored address.
        var (_, sheet, ctx) = Setup();

        var validatedAddr = new CellAddress(sheet.Id, 5, 1);
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(validatedAddr, validatedAddr),
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        sheet.DataValidations.Add(rule);

        // Warm the lookup cache so a stale snapshot is definitely stored.
        DataValidationService.GetApplicable(sheet, validatedAddr).Should().ContainSingle();

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        // After insert the rule applies to row 7, not row 5.
        DataValidationService.GetApplicable(sheet, validatedAddr).Should().BeEmpty();

        cmd.Revert(ctx);

        // After undo, the lookup cache must see the restored range and return the rule.
        DataValidationService.GetApplicable(sheet, validatedAddr).Should().ContainSingle();
    }

}
