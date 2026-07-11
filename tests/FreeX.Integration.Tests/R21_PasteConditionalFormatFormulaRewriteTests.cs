using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-clipboard-paste-special-deep-2: Paste Special "All merging conditional formats" copied a
/// "Formula is" rule's FormulaText verbatim without rewriting relative refs for the new anchor.
/// ViewportConditionalFormatEvaluator evaluates the rule per-cell by shifting FormulaText relative
/// to the rule's (new, post-paste) AppliesTo.Start, so an un-rewritten FormulaText still points at
/// the original source cells instead of the pasted destination -- the sibling PasteDataValidationCommand
/// already rewrites Formula1/Formula2 for the identical scenario via FormulaRewriter.
/// </summary>
public sealed class R21_PasteConditionalFormatFormulaRewriteTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_RewritesFormulaTextForNewAnchor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Rule "Formula is =A1>5" anchored at A1, applied over A1:A10 (standard relative highlight rule).
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // column C

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        // The formula must be rewritten for the new C1 anchor, not copied verbatim.
        pasted.FormulaText.Should().Be("C1>5");

        // Column A keeps values that would satisfy the STALE (un-rewritten) formula if the bug were present.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10))); // A1 = 10 (>5 true)
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new NumberValue(1)));  // A5 = 1  (>5 false)

        // Column C (the pasted destination) has the OPPOSITE truth values from column A at the same rows.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(1)));   // C1 = 1   (>5 false)
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), Cell.FromValue(new NumberValue(100))); // C5 = 100 (>5 true)

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
        var c1 = vp.Cells.Single(c => c.Row == 1 && c.Col == 3);
        var c5 = vp.Cells.Single(c => c.Row == 5 && c.Col == 3);

        // C1 is the rule's own (unshifted) anchor cell: must evaluate against C1's own value (false), not A1's.
        c1.Style!.FillColor.Should().NotBe(new CellColor(255, 0, 0), "C1=1 is not >5 -- the rule must use C1's own value, not stale A1");
        // C5 is shifted 4 rows from the new anchor C1: must evaluate "C5>5" (true), not the stale "A5>5" (false).
        c5.Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "the pasted rule must reference C5 (its own column), not the original source column A");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_AbsoluteFormulaRef_IsNotRewritten()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "$A$1>5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 1, 3), transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        // An absolute reference must survive the paste unchanged, matching Excel's paste semantics.
        pasted.FormulaText.Should().Be("$A$1>5");
    }
}
