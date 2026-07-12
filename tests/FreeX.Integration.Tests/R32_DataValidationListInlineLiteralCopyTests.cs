using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R32-commands-datavalidation-enforce-2: DataValidationCopySupport.RewriteValidationFormula applied
/// relative-reference paste-offset shifting to a List rule's Formula1/Source even when that source was an
/// inline literal list (e.g. "A1", a single item with no leading '=', comma, or '$') rather than a genuine
/// formula/range reference. LooksLikeCellReferenceFormula matched purely on the presence of a digit, so
/// copying such a rule silently rewrote the literal into a shifted cell reference (e.g. "B2"). Real Excel --
/// and DataValidationService.ListSources (ValidateList/ResolveListValues), the runtime authority on this
/// distinction -- only treats a List source as a formula/range reference when it carries a leading '=';
/// anything else is an inline literal list copied verbatim.
/// </summary>
public sealed class R32_DataValidationListInlineLiteralCopyTests
{
    [Fact]
    public void PasteDataValidationCommand_PreservesCellReferenceShapedListInlineLiteralWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Formula1 = "A1"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 2, 2), // B2 (rowDelta=1, colDelta=1)
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 2 && rule.AppliesTo.Start.Col == 2)
            .Which;

        // "A1" here is an inline literal list item (a single allowed value that happens to look
        // like a cell reference), not a formula -- it must be copied verbatim, not shifted to "B2".
        pasted.Formula1.Should().Be("A1");
    }

    [Fact]
    public void PasteDataValidationCommand_StillRebasesGenuineListRangeSourceWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Formula1 = "=A1:A5"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 2, 2), // B2 (rowDelta=1, colDelta=1)
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 2 && rule.AppliesTo.Start.Col == 2)
            .Which;

        // A genuine formula/range source (leading '=') is still a real reference and must shift
        // along with the paste, exactly like any other pasted formula: A1:A5 -> B2:B6.
        pasted.Formula1.Should().Be("=B2:B6");
    }

    [Fact]
    public void PasteDataValidationCommand_StillRebasesCustomFormulaSiblingWhenPasted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=A1>0"
        });

        new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            new CellAddress(sheet.Id, 2, 2), // B2 (rowDelta=1, colDelta=1)
            transpose: false).Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.DataValidations.Should()
            .ContainSingle(rule => rule.AppliesTo.Start.Row == 2 && rule.AppliesTo.Start.Col == 2)
            .Which;

        // Custom-formula rules are unaffected by the List-only guard and still rebase normally.
        pasted.Formula1.Should().Be("=B2>0");
    }
}
