using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R108-commands-paste-conditional-formats-clear-1: PasteConditionalFormatsCommand.Apply only ever
/// ADDED the source's cloned conditional-format rules to the destination sheet -- it never removed
/// or shrunk any pre-existing destination conditional-format rule that already covered the pasted
/// cells. Because this add-only code path is shared by every formatting-carrying paste content kind
/// (plain Ctrl+V, AllExceptBorders, ValuesAndSourceFormatting, AllUsingSourceTheme, and Format
/// Painter), a normal Ctrl+V paste behaved exactly like "Paste Special > All merging conditional
/// formats" for CF purposes: it merged the source's rule into whatever the destination already had,
/// rather than replacing/superseding the destination's own conditional formatting the way a normal
/// Excel paste does. Real Excel supersedes only the destination cells themselves -- a pre-existing
/// destination rule whose range merely overlaps the paste footprint is shrunk to its surviving
/// (non-overlapping) portion(s), not left untouched, mirroring
/// PasteDataValidationCommand.ClearOverlappingValidationRanges for the sibling Data Validation paste.
/// </summary>
public sealed class R108_PasteConditionalFormatsSupersedesDestinationTests
{
    private static ConditionalFormat MakeRule(GridRange appliesTo, string value1) => new()
    {
        AppliesTo = appliesTo,
        RuleType = CfRuleType.CellValue,
        Operator = CfOperator.GreaterThan,
        Value1 = value1,
        FormatIfTrue = new CellStyle { Bold = true }
    };

    /// <summary>
    /// The core failing-before-fix case: a plain Ctrl+V (mode All, default Paste Special options) of
    /// a cell carrying a conditional-format rule onto a destination cell that is already covered by
    /// an UNRELATED pre-existing destination rule must shrink that pre-existing rule to exclude the
    /// pasted cell -- not leave it spanning the pasted cell unchanged. Before the fix,
    /// PasteConditionalFormatsCommand only ever added the pasted rule and never touched the
    /// destination's existing rule, so the existing rule's AppliesTo stayed E5:E7 after the paste;
    /// after the fix it must shrink to E6:E7 (E5 excised).
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_ShrinksPreExistingOverlappingDestinationRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange, "10");
        sheet.ConditionalFormats.Add(sourceRule);
        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(42)));

        // Destination E5, already covered by an existing rule spanning E5:E7 -- entirely unrelated
        // to the source's own rule (different Value1), the way a user's independently-authored
        // destination CF would be.
        var destinationStart = new CellAddress(sheet.Id, 5, 5); // E5
        var existingDestinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 7, 5)); // E5:E7
        var existingRule = MakeRule(existingDestinationRange, "20");
        sheet.ConditionalFormats.Add(existingRule);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sheet.GetCell(sourceStart)!.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Source and destination are the same sheet here, so the original source rule (still
        // covering A1, untouched by pasting a COPY of it elsewhere) also remains: sourceRule +
        // shrunk existingRule + the newly pasted rule = 3.
        sheet.ConditionalFormats.Should().HaveCount(3);

        // The pre-existing destination rule must survive only on the portion NOT covered by the
        // paste -- E6:E7 -- with E5 excised, and must keep its own identity (same Id) rather than
        // being deleted-and-recreated.
        var shrunkExisting = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == existingRule.Id).Subject;
        shrunkExisting.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 6, 5), new CellAddress(sheet.Id, 7, 5)));
        shrunkExisting.AdditionalRanges.Should().BeNull();
        shrunkExisting.Value1.Should().Be("20");

        // The pasted rule must land at the destination cell, carrying the source rule's own value.
        var pasted = sheet.ConditionalFormats.Should()
            .ContainSingle(r => r.Id != existingRule.Id && r.Id != sourceRule.Id).Subject;
        pasted.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
        pasted.Value1.Should().Be("10");

        command.Revert(ctx);

        // Revert must restore all original rules exactly (same Ids, same untouched AppliesTo).
        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats.Should().Contain(r => r.Id == sourceRule.Id && r.AppliesTo == sourceRange);
        sheet.ConditionalFormats.Should().Contain(r => r.Id == existingRule.Id && r.AppliesTo == existingDestinationRange);
    }

    /// <summary>
    /// No-regression sibling (and the trickiest part of the fix to keep correct): "Paste Special >
    /// All merging conditional formats" is a narrower, DIFFERENT action from a plain Ctrl+V whose
    /// entire point is to ADD the copied rule alongside whatever the destination already has, never
    /// to supersede it. PasteCommandFactory.CreateInternalPasteCommand implements this content kind
    /// by recursively delegating to the generic ContentKind==Default paste-with-formatting logic
    /// (the same logic the first test above exercises), so the fix must thread a "this was really an
    /// AllMergingConditionalFormats paste" signal through that recursion -- otherwise the merging
    /// action would regress into superseding too, once the plain-paste path started superseding.
    /// A pre-existing destination rule must come through completely UNTOUCHED here.
    /// </summary>
    [Fact]
    public void AllMergingConditionalFormats_NonTiled_LeavesPreExistingDestinationRuleUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange, "10");
        sheet.ConditionalFormats.Add(sourceRule);
        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(42)));

        var destinationStart = new CellAddress(sheet.Id, 5, 5); // E5
        var existingDestinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 7, 5)); // E5:E7
        var existingRule = MakeRule(existingDestinationRange, "20");
        sheet.ConditionalFormats.Add(existingRule);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sheet.GetCell(sourceStart)!.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // sourceRule (untouched, still at A1) + untouched existingRule + newly pasted rule = 3.
        sheet.ConditionalFormats.Should().HaveCount(3);

        // Unlike the plain-paste test above, the pre-existing rule must be completely unchanged --
        // still spanning its full original E5:E7 range.
        var untouchedExisting = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == existingRule.Id).Subject;
        untouchedExisting.AppliesTo.Should().Be(existingDestinationRange);

        var pasted = sheet.ConditionalFormats.Should()
            .ContainSingle(r => r.Id != existingRule.Id && r.Id != sourceRule.Id).Subject;
        pasted.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
        pasted.Value1.Should().Be("10");
    }

    /// <summary>
    /// Sibling coverage for the tiled destination path (a larger selection than the copied source):
    /// an ordinary formatting-carrying tiled paste must also supersede (shrink) an overlapping
    /// pre-existing destination rule, exactly like the non-tiled case.
    /// </summary>
    [Fact]
    public void PlainPaste_Tiled_ShrinksPreExistingOverlappingDestinationRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange, "10");
        sheet.ConditionalFormats.Add(sourceRule);
        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(42)));

        // Destination selection C1:C3 (a 1x3 tile of the 1x1 source), with a pre-existing rule
        // spanning C1:E1 (extends past the paste footprint to the right).
        var destinationStart = new CellAddress(sheet.Id, 1, 3); // C1
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 3, 3)); // C1:C3
        var existingRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 1, 5)); // C1:E1
        var existingRule = MakeRule(existingRange, "20");
        sheet.ConditionalFormats.Add(existingRule);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sheet.GetCell(sourceStart)!.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // sourceRule (untouched, still at A1) + shrunk existingRule + newly pasted rule = 3.
        sheet.ConditionalFormats.Should().HaveCount(3);

        // The CF rule is pasted once, anchored at the destination's start (matching the already-
        // established tiled CF-carry behavior), so the footprint the clear step must consider is
        // just C1:C1 -- the existing C1:E1 rule shrinks to D1:E1 (C1 excised), not deleted.
        var shrunkExisting = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == existingRule.Id).Subject;
        shrunkExisting.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 1, 5)));

        var pasted = sheet.ConditionalFormats.Should()
            .ContainSingle(r => r.Id != existingRule.Id && r.Id != sourceRule.Id).Subject;
        pasted.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
    }

    /// <summary>
    /// Sibling coverage for the OTHER real call site of PasteConditionalFormatsCommand: Format
    /// Painter (FormatPainterCommandFactory) shares this command and must get the same supersede
    /// fix -- painting formatting onto a cell that already carries its own CF rule replaces that
    /// rule's coverage of the painted cell exactly like a plain paste does, not merge into it.
    /// </summary>
    [Fact]
    public void FormatPainter_ShrinksPreExistingOverlappingDestinationRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceAddress = new CellAddress(sheet.Id, 1, 1); // A1
        var sourceRule = MakeRule(new GridRange(sourceAddress, sourceAddress), "10");
        sheet.ConditionalFormats.Add(sourceRule);

        var targetAddress = new CellAddress(sheet.Id, 5, 5); // E5
        var existingRange = new GridRange(targetAddress, new CellAddress(sheet.Id, 7, 5)); // E5:E7
        var existingRule = MakeRule(existingRange, "20");
        sheet.ConditionalFormats.Add(existingRule);

        var command = FormatPainterCommandFactory.Create(
            wb, sheet, sourceAddress, new GridRange(targetAddress, targetAddress));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // sourceRule (untouched, still at A1) + shrunk existingRule + newly painted rule = 3.
        sheet.ConditionalFormats.Should().HaveCount(3);

        var shrunkExisting = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == existingRule.Id).Subject;
        shrunkExisting.AppliesTo.Should().Be(new GridRange(new CellAddress(sheet.Id, 6, 5), new CellAddress(sheet.Id, 7, 5)));

        var pasted = sheet.ConditionalFormats.Should()
            .ContainSingle(r => r.Id != existingRule.Id && r.Id != sourceRule.Id).Subject;
        pasted.AppliesTo.Should().Be(new GridRange(targetAddress, targetAddress));
        pasted.Value1.Should().Be("10");
    }
}
