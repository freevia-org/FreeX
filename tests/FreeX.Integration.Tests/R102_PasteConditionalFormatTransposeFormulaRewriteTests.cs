using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R102-commands-4: PasteConditionalFormatsCommand.CloneRuleForDestination correctly axis-swaps the
/// rule's own AppliesTo start/end via MapDestination under Paste Special "All merging conditional
/// formats" + Transpose, but previously always rewrote the rule's FORMULA content (FormulaText, and
/// any colorScale/dataBar/iconSet threshold whose ThresholdType is CfThresholdType.Formula) with a
/// uniform PasteOffsetOp(rowDelta, colDelta) -- never with the transpose-aware PasteTransposeOp that
/// PasteCommandFactory.cs uses for ordinary cell-formula transpose pastes. A real transpose needs
/// new_row = destAnchorRow + (oldCol - srcAnchorCol), new_col = destAnchorCol + (oldRow - srcAnchorRow)
/// (axis-swapping), not row+=rowDelta, col+=colDelta (axis-preserving).
/// </summary>
public sealed class R102_PasteConditionalFormatTransposeFormulaRewriteTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_Transpose_AxisSwapsFormulaTextReference()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Rule "Formula is =A2>5" anchored at A1, applied over the column vector A1:A4. Relative to
        // its own anchor A1 this formula references the cell one ROW below the anchor (offset +1 row,
        // +0 col).
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A2>5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: true)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        // Transposing the column vector A1:A4 onto anchor C1 produces the row vector C1:F1. The
        // formula's own offset from its anchor (+1 row, +0 col) must be axis-swapped onto the new
        // anchor C1, i.e. become (+0 row, +1 col) => D1. The old, buggy code applied a uniform
        // PasteOffsetOp instead, which would have produced "C2" (shifting the *column* delta of 2
        // computed from the anchor move onto a formula ref that was never expressed along that axis).
        pasted.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 6)));
        pasted.FormulaText.Should().Be("D1>5");

        // Behavioral proof through the real evaluator: C1 is the rule's own (unshifted) anchor, so its
        // shifted-at-evaluation formula is exactly the (rewritten) FormulaText itself, i.e. "D1>5".
        // Seed D1 = 1 (not >5, condition false) and, to catch the old axis-preserving bug specifically,
        // seed C2 = 10 (>5, condition true) -- C2 is what the buggy "C2>5" rewrite would have pointed
        // at instead, and C2 sits entirely outside the pasted row-1 band, so a fix regression back to
        // the old behavior would incorrectly highlight C1.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0)));   // C1 = 0 (anchor cell itself)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromValue(new NumberValue(1)));   // D1 = 1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(10)));  // C2 = 10

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
        var c1 = vp.Cells.Single(c => c.Row == 1 && c.Col == 3);

        c1.Style?.FillColor.Should().NotBe(new CellColor(255, 0, 0),
            "the correctly-transposed rule must evaluate D1 (=1, false), not the stale/mis-axised C2 (=10, true)");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_Transpose_AxisSwapsColorScaleFormulaThreshold()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A colorScale rule whose MinThresholdType is Formula holds a relative reference just like a
        // "Formula is" rule's FormulaText, and must be rewritten through the identical pasteOp
        // (this exercises the RewriteThresholdValue call site, not just RewriteFormulaText).
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.ColorScale,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "A2",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(255, 0, 0),
            MaxColor = new RgbColor(0, 255, 0)
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: true)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        // Same axis-swap as FormulaText: (+1 row, +0 col) from anchor A1 becomes (+0 row, +1 col)
        // from the new anchor C1 => D1. The old uniform-offset code would have produced "C2".
        pasted.MinThresholdValue.Should().Be("D1");
    }

    [Fact]
    public void PasteConditionalFormatsCommand_NoTranspose_StillUsesUniformOffset()
    {
        // No-regression sibling: an ordinary (non-transpose) paste of the same shape of rule must
        // keep using the plain, axis-preserving PasteOffsetOp rewrite -- the fix must only switch to
        // PasteTransposeOp when _transpose is true, never for a regular paste.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A2>5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, destination, transpose: false)
            .Apply(new TestCommandContext(wb))
            .Success.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();

        // Plain paste keeps the same shape (column vector), now anchored at C1:C4, and the reference
        // shifts uniformly by the column delta (+2 cols, +0 rows): A2 -> C2.
        pasted.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 4, 3)));
        pasted.FormulaText.Should().Be("C2>5");
    }
}
