using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteDataValidationCommand_CopiesIntersectingRulesAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var existingDestinationRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5)),
            Type = DvType.Decimal,
            Formula1 = "1",
            Formula2 = "9"
        };
        sheet.DataValidations.Add(existingDestinationRule);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.List,
            Formula1 = "Red,Blue",
            AllowBlank = false,
            ErrorTitle = "Pick a color"
        });

        var command = new PasteDataValidationCommand(
            sheet.Id,
            sourceRange,
            new CellAddress(sheet.Id, 5, 5),
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Should().NotContain(existingDestinationRule);
        var pastedRange = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 6, 5));
        sheet.DataValidations.Count(rule => rule.AppliesTo == pastedRange && rule.Formula1 == "Red,Blue").Should().Be(1);
        var pasted = sheet.DataValidations.First(rule => rule.AppliesTo == pastedRange && rule.Formula1 == "Red,Blue");
        pasted.Formula1.Should().Be("Red,Blue");
        pasted.AllowBlank.Should().BeFalse();
        pasted.ErrorTitle.Should().Be("Pick a color");

        command.Revert(ctx);

        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Should().Contain(rule => rule.AppliesTo == existingDestinationRule.AppliesTo && rule.Type == DvType.Decimal);
        sheet.DataValidations.Should().Contain(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "Red,Blue");
    }

    [Fact]
    public void PasteDataValidationCommand_CopiesValidationAcrossSheets()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sourceSheet.Id, 1, 1), new CellAddress(sourceSheet.Id, 1, 2));
        sourceSheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.List,
            Formula1 = "Yes,No"
        });

        var command = new PasteDataValidationCommand(
            targetSheet.Id,
            sourceRange,
            new CellAddress(targetSheet.Id, 4, 3),
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.DataValidations.Should().ContainSingle().Which.AppliesTo.Should().Be(
            new GridRange(new CellAddress(targetSheet.Id, 4, 3), new CellAddress(targetSheet.Id, 4, 4)));

        command.Revert(ctx);

        targetSheet.DataValidations.Should().BeEmpty();
        sourceSheet.DataValidations.Should().ContainSingle();
    }

    [Fact]
    public void PasteDataValidationCommand_RejectsProtectedTargetSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var existingDestinationRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5)),
            Type = DvType.Decimal,
            Formula1 = "1",
            Formula2 = "9"
        };
        sheet.DataValidations.Add(existingDestinationRule);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.List,
            Formula1 = "Yes,No"
        });
        sheet.IsProtected = true;

        var outcome = new PasteDataValidationCommand(
            sheet.Id,
            sourceRange,
            new CellAddress(sheet.Id, 5, 5),
            transpose: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Should().ContainSingle(rule => ReferenceEquals(rule, existingDestinationRule));
        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "Yes,No");
    }

    [Fact]
    public void PasteDataValidationCommand_RebasesRelativeCustomFormulaWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=B1+$C1+B$1+$C$1>0"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 3, 3),
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 3 && rule.AppliesTo.Start.Col == 3)
            .Which;
        pasted.Formula1.Should().Be("=D3+$C3+D$1+$C$1>0");
    }

    [Fact]
    public void PasteDataValidationCommand_RebasesBothBoundaryFormulasWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 4, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=B4",
            Formula2 = "=C4"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 6, 3),
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 6 && rule.AppliesTo.Start.Col == 3)
            .Which;
        pasted.Formula1.Should().Be("=D6");
        pasted.Formula2.Should().Be("=E6");
    }

    [Fact]
    public void PasteDataValidationCommand_RebasesRelativeListRangeAndKeepsAbsoluteListRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Type = DvType.List,
            Formula1 = "=B1:B3"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)),
            Type = DvType.List,
            Formula1 = "=$B$1:$B$3"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            sourceRange,
            new CellAddress(sheet.Id, 4, 3),
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(new CellAddress(sheet.Id, 4, 3), new CellAddress(sheet.Id, 4, 3))
            && rule.Formula1 == "=D3:D5");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(new CellAddress(sheet.Id, 5, 3), new CellAddress(sheet.Id, 5, 3))
            && rule.Formula1 == "=$B$1:$B$3");
    }

    [Fact]
    public void PasteDataValidationCommand_PreservesInlineListAndNamedRangeSourcesWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Type = DvType.List,
            Formula1 = "Apple,Banana"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Type = DvType.List,
            Formula1 = "=Codes"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            sourceRange,
            new CellAddress(sheet.Id, 4, 3),
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().Contain(rule => rule.AppliesTo.Start.Row == 4 && rule.Formula1 == "Apple,Banana");
        sheet.DataValidations.Should().Contain(rule => rule.AppliesTo.Start.Row == 5 && rule.Formula1 == "=Codes");
    }

    [Fact]
    public void PasteDataValidationCommand_RebasesSheetQualifiedRelativeListSourceWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 3, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Formula1 = "=Lookup!A1:A3"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 5, 3),
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo.Start.Row == 5
            && rule.AppliesTo.Start.Col == 3
            && rule.Formula1 == "=Lookup!C3:C5");
    }

    // R102-commands-data-validation-transpose-5-5: a Custom-type validation rule's relative
    // Formula1 references must be AXIS-SWAPPED when pasted via Paste Special > Validation with
    // Transpose, exactly like an ordinary transposed cell formula (PasteCommandFactory) and the
    // sibling ConditionalFormat.FormulaText fix (PasteConditionalFormatsCommand.CloneRuleForDestination).
    // Before the fix, DataValidationCopySupport unconditionally built a PasteOffsetOp from the
    // already-axis-swapped rowDelta/colDelta and applied it as a UNIFORM translation, which is the
    // wrong transform once the block's shape has been transposed.
    [Fact]
    public void R102_PasteDataValidationCommand_TransposesRelativeCustomFormulaAxisSwap()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=B1+$C1+B$1+$C$1>0"
        });

        var outcome = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 3, 3), // C3
            transpose: true).Apply(ctx);

        outcome.Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 3 && rule.AppliesTo.Start.Col == 3)
            .Which;
        // B1 (rel,rel): its ROW offset from the source anchor (0) becomes the new COLUMN offset
        // from the dest anchor -> col C (3+0); its COLUMN offset from the source anchor (+1)
        // becomes the new ROW offset from the dest anchor -> row 4 (3+1). => C4.
        // $C1 (abs col, rel row): column untouched; row swaps using the column's own offset
        // (+2 from anchor col 1) -> row 5. => $C5.
        // B$1 (rel col, abs row): column swaps using the row's own literal value (offset 0 from
        // anchor row 1) -> col C; row untouched. => C$1.
        // $C$1 (abs,abs): unchanged.
        pasted.Formula1.Should().Be("=C4+$C5+C$1+$C$1>0");
    }

    // Sibling no-regression coverage: the refactor that threaded a RewriteOperation (instead of
    // raw rowDelta/colDelta ints) through DataValidationCopySupport.CloneValidation must leave the
    // ordinary non-transpose uniform-translation path byte-for-byte unchanged.
    [Fact]
    public void R102_PasteDataValidationCommand_NonTransposePasteStillUsesUniformOffset()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=B1+$C1+B$1+$C$1>0"
        });

        var outcome = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 3, 3), // C3
            transpose: false).Apply(ctx);

        outcome.Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 3 && rule.AppliesTo.Start.Col == 3)
            .Which;
        pasted.Formula1.Should().Be("=D3+$C3+D$1+$C$1>0");
    }

}
