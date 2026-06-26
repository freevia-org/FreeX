using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class MoveRangeCommandTests
{
    [Fact]
    public void Apply_MovesCellsAndUndoRestoresSourceAndDestination()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("left")));
        var formula = Cell.FromFormula("A1&\"!\"");
        formula.Value = new TextValue("left!");
        sheet.SetCell(sourceEnd, formula);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(sourceStart).Should().BeNull();
        sheet.GetCell(sourceEnd).Should().BeNull();
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("left"));
        var movedFormula = sheet.GetCell(new CellAddress(sheet.Id, 3, 4))!;
        movedFormula.FormulaText.Should().Be("C3&\"!\"");
        movedFormula.Value.Should().Be(new TextValue("left!"));

        command.Revert(context);

        sheet.GetCell(sourceStart)!.Value.Should().Be(new TextValue("left"));
        sheet.GetCell(sourceEnd)!.FormulaText.Should().Be("A1&\"!\"");
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("old"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 4)).Should().BeNull();
    }

    [Fact]
    public void Apply_KeepsMovedFormulaReferencesOutsideMovedRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var source = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(
            source,
            Cell.FromFormula("A1+$A$1+$A1+A$1+SUM(A1:A2)"));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(source, source),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(destination)!.FormulaText
            .Should()
            .Be("A1+$A$1+$A1+A$1+SUM(A1:A2)");
    }

    [Fact]
    public void Apply_RetargetsFormulaReferencesToCellsMovedWithSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var otherSheet = workbook.AddSheet("Other");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        var outsideFormula = new CellAddress(sheet.Id, 5, 5);
        var crossSheetFormula = new CellAddress(otherSheet.Id, 1, 1);

        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(
            sourceEnd,
            Cell.FromFormula("A1+$A$1+SUM(A1:B1)"));
        sheet.SetCell(
            outsideFormula,
            Cell.FromFormula("A1+$A$1+SUM(A1:B1)"));
        otherSheet.SetCell(
            crossSheetFormula,
            Cell.FromFormula("Sheet1!A1+$A$1"));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 3, 4))!.FormulaText
            .Should()
            .Be("C3+$C$3+SUM(C3:D3)");
        sheet.GetCell(outsideFormula)!.FormulaText
            .Should()
            .Be("C3+$C$3+SUM(C3:D3)");
        otherSheet.GetCell(crossSheetFormula)!.FormulaText
            .Should()
            .Be("Sheet1!C3+$A$1");

        command.Revert(context);

        sheet.GetCell(sourceEnd)!.FormulaText.Should().Be("A1+$A$1+SUM(A1:B1)");
        sheet.GetCell(outsideFormula)!.FormulaText.Should().Be("A1+$A$1+SUM(A1:B1)");
        otherSheet.GetCell(crossSheetFormula)!.FormulaText.Should().Be("Sheet1!A1+$A$1");
    }

    [Fact]
    public void Apply_InvalidatesMovedFormulaCachedAstEvenWhenTextDoesNotChange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 3);
        var formula = Cell.FromFormula("$B$2");
        formula.CachedAst = new object();
        sheet.SetCell(source, formula);

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(source, source),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        var movedFormula = sheet.GetCell(destination)!;
        movedFormula.FormulaText.Should().Be("$B$2");
        movedFormula.CachedAst.Should().BeNull();
    }

    [Fact]
    public void Apply_HandlesOverlappingMoveWithoutLosingSourcePayloads()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("A1")));
        sheet.SetCell(b1, Cell.FromValue(new TextValue("B1")));
        sheet.SetCell(a2, Cell.FromValue(new TextValue("A2")));
        sheet.SetCell(b2, Cell.FromValue(new TextValue("B2")));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(a1, b2),
            b2);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(a2).Should().BeNull();
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("A1"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 3))!.Value.Should().Be(new TextValue("B1"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.Value.Should().Be(new TextValue("A2"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3))!.Value.Should().Be(new TextValue("B2"));

        command.Revert(context);

        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("A1"));
        sheet.GetCell(b1)!.Value.Should().Be(new TextValue("B1"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("A2"));
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("B2"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 3)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3)).Should().BeNull();
    }

    [Fact]
    public void Apply_MovesStyleOnlyCellsCommentsAndHyperlinksAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceStyleOnly = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 4, 4);
        var destinationStyleOnly = new CellAddress(sheet.Id, 4, 5);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var oldDestinationStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("link")));
        sheet.SetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col, sourceStyle);
        sheet.Hyperlinks[sourceStart] = "https://example.com";
        sheet.HyperlinkMetadata[sourceStart] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example",
            "");
        sheet.Comments[sourceStart] = "move me";
        sheet.ThreadedComments[sourceStyleOnly] = new ThreadedComment("thread", "Quinn")
        {
            Replies = [new CommentReply("reply", "Codex")]
        };
        sheet.SetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col, oldDestinationStyle);
        sheet.Comments[destination] = "replace me";

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceStyleOnly),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(sourceStart).Should().BeNull();
        sheet.GetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col).Should().BeNull();
        sheet.Hyperlinks.Should().NotContainKey(sourceStart);
        sheet.Comments.Should().NotContainKey(sourceStart);
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("link"));
        sheet.Hyperlinks[destination].Should().Be("https://example.com");
        sheet.Comments[destination].Should().Be("move me");
        sheet.GetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col).Should().Be(sourceStyle);
        sheet.ThreadedComments[destinationStyleOnly].Replies.Should().Equal(new CommentReply("reply", "Codex"));

        command.Revert(context);

        sheet.GetCell(sourceStart)!.Value.Should().Be(new TextValue("link"));
        sheet.GetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col).Should().Be(sourceStyle);
        sheet.Hyperlinks[sourceStart].Should().Be("https://example.com");
        sheet.Comments[sourceStart].Should().Be("move me");
        sheet.ThreadedComments[sourceStyleOnly].Text.Should().Be("thread");
        sheet.GetCell(destination).Should().BeNull();
        sheet.GetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col).Should().Be(oldDestinationStyle);
        sheet.Comments[destination].Should().Be("replace me");
    }

    [Fact]
    public void Apply_RejectsOutOfBoundsDestination()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var outcome = new MoveRangeCommand(
                sheet.Id,
                source,
                new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol))
            .Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("outside");
    }

    [Fact]
    public void Apply_MovesFullyContainedDvAndCfRulesWithCells_AndUndoRestores()
    {
        // B2:B10 has a DV dropdown and a CF rule both covering exactly B2:B10.
        // Move B2:B10 → D2:D10; rules must follow to D2:D10.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(sheet.Id, 2, 2); // B2
        var sourceEnd   = new CellAddress(sheet.Id, 10, 2); // B10
        var destination = new CellAddress(sheet.Id, 2, 4);  // D2
        var sourceRange = new GridRange(sourceStart, sourceEnd);

        // Add a cell with a value so move is non-trivial.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromValue(new TextValue("drop")));

        var dvRule = new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        var cfRule = new ConditionalFormat
        {
            AppliesTo = sourceRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(dvRule);
        sheet.ConditionalFormats.Add(cfRule);

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        // Rules should have moved to D2:D10 (col 4).
        dvRule.AppliesTo.Start.Col.Should().Be(4, "DV rule AppliesTo should follow the move to column D");
        dvRule.AppliesTo.End.Col.Should().Be(4);
        dvRule.AppliesTo.Start.Row.Should().Be(2);
        dvRule.AppliesTo.End.Row.Should().Be(10);

        cfRule.AppliesTo.Start.Col.Should().Be(4, "CF rule AppliesTo should follow the move to column D");
        cfRule.AppliesTo.End.Col.Should().Be(4);

        // DV lookup: should work at D5 (row 5, col 4), not B5.
        var b5 = new CellAddress(sheet.Id, 5, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        DataValidationService.GetApplicable(sheet, b5).Should().BeEmpty("rule left B column");
        DataValidationService.GetApplicable(sheet, d5).Should().ContainSingle("rule now in D column");

        command.Revert(context);

        // After undo, rules back to B2:B10.
        dvRule.AppliesTo.Start.Col.Should().Be(2, "DV rule should be restored to column B on undo");
        dvRule.AppliesTo.End.Col.Should().Be(2);
        cfRule.AppliesTo.Start.Col.Should().Be(2, "CF rule should be restored to column B on undo");
        cfRule.AppliesTo.End.Col.Should().Be(2);

        DataValidationService.GetApplicable(sheet, b5).Should().ContainSingle("rule restored to B column");
        DataValidationService.GetApplicable(sheet, d5).Should().BeEmpty("D column has no rule after undo");
    }

    [Fact]
    public void Apply_PartiallyOverlappingDvAndCfRules_AreNotTranslated()
    {
        // Documented limitation: rules that only partially overlap the moved range are left unchanged.
        // DV/CF covers B2:C10; move B2:B10 — partial overlap — rule stays at B2:C10.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceStart = new CellAddress(sheet.Id, 2, 2); // B2
        var sourceEnd   = new CellAddress(sheet.Id, 10, 2); // B10
        var destination = new CellAddress(sheet.Id, 2, 4);  // D2
        var sourceRange = new GridRange(sourceStart, sourceEnd);

        // Rule spans B2:C10 — wider than the moved range.
        var ruleStart = new CellAddress(sheet.Id, 2, 2);
        var ruleEnd   = new CellAddress(sheet.Id, 10, 3);
        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(ruleStart, ruleEnd),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(ruleStart, ruleEnd),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(dvRule);
        sheet.ConditionalFormats.Add(cfRule);

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        command.Apply(context).Success.Should().BeTrue();

        // Rule must be unchanged because it's only partially contained.
        dvRule.AppliesTo.Start.Col.Should().Be(2, "partially-overlapping DV rule must not move");
        dvRule.AppliesTo.End.Col.Should().Be(3);
        cfRule.AppliesTo.Start.Col.Should().Be(2, "partially-overlapping CF rule must not move");
        cfRule.AppliesTo.End.Col.Should().Be(3);
    }

    // ── X2 regression: CF/DV FormulaText rewrite on MoveRange ────────────────

    [Fact]
    public void MoveRange_RewritesCfAndDvFormulaTextAndUndoRestores()
    {
        // Move A1:A1 to C3 — any formula ref to A1 inside a CF/DV rule on the same sheet
        // should follow the moved cell (MoveRangeOp retargets refs inside the source range).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(42));

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5)),
            Type = DvType.Custom,
            Formula1 = "A1<>\"\"",
            AlertStyle = DvAlertStyle.Stop
        };
        sheet.DataValidations.Add(dvRule);

        var destination = new CellAddress(sheet.Id, 3, 3);
        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // After moving A1 → C3 the ref inside CF/DV that was A1 should become C3.
        cfRule.FormulaText.Should().Be("C3>0", "CF formula ref to A1 retargeted to C3 after move");
        dvRule.Formula1.Should().Be("C3<>\"\"", "DV formula ref to A1 retargeted to C3 after move");

        cmd.Revert(ctx);
        cfRule.FormulaText.Should().Be("A1>0", "undo restores CF formula after move");
        dvRule.Formula1.Should().Be("A1<>\"\"", "undo restores DV formula after move");
    }

    // ── BJ1: MoveRange translates CF AdditionalRanges ────────────────────────

    [Fact]
    public void Apply_MultiRangeCfRule_FullyInsideMovedRange_TranslatesAdditionalRanges()
    {
        // CF rule sqref "A1:A5 C1:C5" (AppliesTo=A1:A5, AdditionalRanges=[C1:C5]).
        // Both ranges are fully inside the moved selection A1:C5 → destination D6.
        // After move: AppliesTo should shift to D6:D10, AdditionalRanges to F6:F10.
        // Undo should restore both to the original positions.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),   // A1
            new CellAddress(sheet.Id, 5, 3));   // C5

        var destination = new CellAddress(sheet.Id, 6, 4); // D6

        var cfRule = new ConditionalFormat
        {
            AppliesTo        = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)), // A1:A5
            AdditionalRanges = [new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3))], // C1:C5
            RuleType         = CfRuleType.ColorScale,
            Priority         = 1
        };
        sheet.ConditionalFormats.Add(cfRule);

        var cmd = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // AppliesTo A1:A5 → rowDelta=+5, colDelta=+3 → D6:D10
        cfRule.AppliesTo.Start.Row.Should().Be(6,  "AppliesTo row start should shift by +5");
        cfRule.AppliesTo.Start.Col.Should().Be(4,  "AppliesTo col start should shift by +3 (A→D)");
        cfRule.AppliesTo.End.Row.Should().Be(10,   "AppliesTo row end should shift by +5");
        cfRule.AppliesTo.End.Col.Should().Be(4,    "AppliesTo col end should remain in D");

        // AdditionalRanges C1:C5 → F6:F10
        cfRule.AdditionalRanges.Should().ContainSingle("rule has one additional range");
        var additional = cfRule.AdditionalRanges![0];
        additional.Start.Row.Should().Be(6,  "AdditionalRanges row start should shift by +5");
        additional.Start.Col.Should().Be(6,  "AdditionalRanges col start should shift by +3 (C→F)");
        additional.End.Row.Should().Be(10,   "AdditionalRanges row end should shift by +5");
        additional.End.Col.Should().Be(6,    "AdditionalRanges col end should remain in F");

        // Undo restores both
        cmd.Revert(ctx);

        cfRule.AppliesTo.Start.Row.Should().Be(1, "undo restores AppliesTo to A1:A5");
        cfRule.AppliesTo.Start.Col.Should().Be(1);
        cfRule.AppliesTo.End.Row.Should().Be(5);
        cfRule.AppliesTo.End.Col.Should().Be(1);

        cfRule.AdditionalRanges.Should().ContainSingle("undo preserves AdditionalRanges count");
        var restoredAdditional = cfRule.AdditionalRanges![0];
        restoredAdditional.Start.Row.Should().Be(1, "undo restores AdditionalRanges to C1:C5");
        restoredAdditional.Start.Col.Should().Be(3);
        restoredAdditional.End.Row.Should().Be(5);
        restoredAdditional.End.Col.Should().Be(3);
    }

    [Fact]
    public void Apply_SingleRangeCfRule_FullyInsideMovedRange_StillTranslates()
    {
        // Regression guard: a CF rule with no AdditionalRanges still moves correctly.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var dest   = new CellAddress(sheet.Id, 5, 5);

        var cfRule = new ConditionalFormat
        {
            AppliesTo        = source,
            AdditionalRanges = null,
            RuleType         = CfRuleType.CellValue,
            Operator         = CfOperator.GreaterThan,
            Value1           = "0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var cmd = new MoveRangeCommand(sheet.Id, source, dest);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.AppliesTo.Start.Row.Should().Be(5, "single-range rule AppliesTo should shift to E5");
        cfRule.AppliesTo.Start.Col.Should().Be(5);
        cfRule.AdditionalRanges.Should().BeNull("no AdditionalRanges to translate");
    }

    [Fact]
    public void Apply_MultiRangeCfRule_AdditionalRangePartiallyOutsideMovedRange_LeavesNonContainedUnchanged()
    {
        // AppliesTo=A1:A5 fully inside A1:B5; AdditionalRanges=[C1:C5] NOT inside A1:B5.
        // Only AppliesTo should translate; AdditionalRanges stays at C1:C5.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange  = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)); // A1:B5
        var destination  = new CellAddress(sheet.Id, 1, 5); // E1

        var cfRule = new ConditionalFormat
        {
            AppliesTo        = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)), // A1:A5 — inside
            AdditionalRanges = [new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3))], // C1:C5 — outside
            RuleType         = CfRuleType.ColorScale,
            Priority         = 1
        };
        sheet.ConditionalFormats.Add(cfRule);

        var cmd = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // AppliesTo A1:A5 → E1:E5 (colDelta=+4)
        cfRule.AppliesTo.Start.Col.Should().Be(5, "AppliesTo is inside moved range and should shift");
        // AdditionalRanges C1:C5 → unchanged (C is outside A1:B5)
        cfRule.AdditionalRanges.Should().ContainSingle();
        cfRule.AdditionalRanges![0].Start.Col.Should().Be(3, "AdditionalRanges outside moved range must NOT shift");
    }
}
