using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 bucket Q9 regression tests.
/// </summary>
public sealed class FreeXR12Q9Tests
{
    // R12-xlsx-data-validation-1: Paste Special > Validation must not carry over stale
    // AdditionalRanges from the source rule, and must not drop a rule that is anchored only
    // by an AdditionalRanges entry that overlaps the copied source range.
    [Fact]
    public void PasteDataValidationCommand_DoesNotCopyStaleAdditionalRangeIntoPastedRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceCell = new CellAddress(sheet.Id, 1, 1); // A1
        var additionalCell = new CellAddress(sheet.Id, 1, 3); // C1
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(sourceCell, sourceCell),
            Type = DvType.List,
            Formula1 = "Red,Blue"
        };
        rule.AdditionalRanges.Add(new GridRange(additionalCell, additionalCell));
        sheet.DataValidations.Add(rule);

        var destination = new CellAddress(sheet.Id, 5, 1); // A5
        var command = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(sourceCell, sourceCell),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        var pastedRange = new GridRange(destination, destination);
        var pasted = sheet.DataValidations.Should().ContainSingle(r => r.AppliesTo == pastedRange).Which;
        pasted.AdditionalRanges.Should().BeEmpty("the source's C1 additional range was never part of the copy and must not leak into the paste");

        // The paste must not add any *new* rule that covers C1 beyond the original, untouched
        // source rule (which legitimately carried C1 in its own AdditionalRanges before the paste
        // ever ran). Only the pasted rule (at A5) and the original source rule (at A1) may remain.
        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Should().NotContain(r =>
            r.Id != rule.Id &&
            r.AppliesTo != pastedRange &&
            (r.AppliesTo.Contains(additionalCell) || r.AdditionalRanges.Any(ar => ar.Contains(additionalCell))));
    }

    [Fact]
    public void PasteDataValidationCommand_CopiesRuleAnchoredOnlyByAdditionalRangeOverlap()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var primaryCell = new CellAddress(sheet.Id, 1, 2); // B1 (not copied)
        var additionalCell = new CellAddress(sheet.Id, 1, 1); // A1 (copied)
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(primaryCell, primaryCell),
            Type = DvType.List,
            Formula1 = "Yes,No"
        };
        rule.AdditionalRanges.Add(new GridRange(additionalCell, additionalCell));
        sheet.DataValidations.Add(rule);

        var destination = new CellAddress(sheet.Id, 5, 1); // A5
        var command = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(additionalCell, additionalCell),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        var pastedRange = new GridRange(destination, destination);
        sheet.DataValidations.Should().Contain(r => r.AppliesTo == pastedRange && r.Formula1 == "Yes,No");
    }
}
