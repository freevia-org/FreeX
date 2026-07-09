using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-15 regression tests for two DataValidationService bugs:
///   R15-data-validation-ui-1: Text Length rules rejected every non-text entry outright.
///   R15-data-validation-ui-2: List sources with relative references (e.g. =INDIRECT($A2))
///     were evaluated from the rule's anchor cell for every cell in the range, instead of
///     being shifted per validated cell like ValidateCustom already does.
/// </summary>
public sealed class R15_dv_core_Tests
{
    [Fact]
    public void ValidateTextLength_AcceptsNumericEntryAtOrUnderLimit()
    {
        var rule = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "5",
        };

        // "12345" is 5 characters — should be accepted even though the stored value is a
        // NumberValue, not a TextValue.
        DataValidationService.Validate(rule, new NumberValue(12345)).Should().BeNull();
    }

    [Fact]
    public void ValidateTextLength_RejectsNumericEntryOverLimitWithLengthMessage_NotMustBeTextMessage()
    {
        var rule = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "5",
        };

        // "123456" is 6 characters — over the limit, so it must be rejected. Before the fix,
        // ValidateTextLength rejected every non-TextValue outright with "Value must be text.",
        // which is the wrong reason and wrong for values that DO satisfy the length rule.
        var error = DataValidationService.Validate(rule, new NumberValue(123456));

        error.Should().NotBeNull();
        error.Should().NotBe("Value must be text.");
        error.Should().Contain("length");
    }

    [Fact]
    public void GetListItems_ForIndirectListSource_ShiftsRelativeReferenceToTargetCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Cascading-dropdown setup: column A names which list to use for the row, column D
        // holds two disjoint named-range lists.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Fruits")));  // A2
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new TextValue("Veggies"))); // A5

        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromValue(new TextValue("Apple")));  // D1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), Cell.FromValue(new TextValue("Banana"))); // D2
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromValue(new TextValue("Carrot"))); // D4
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), Cell.FromValue(new TextValue("Pea")));    // D5

        workbook.DefineNamedRange("Fruits", new GridRange(
            new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 4)));
        workbook.DefineNamedRange("Veggies", new GridRange(
            new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 5, 4)));

        // Rule authored (anchor) at B2, applying to B2:B10, with a relative list source that
        // reads the "which list" cell in the same row of column A.
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var rule = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=INDIRECT($A2)",
            AppliesTo = new GridRange(anchor, new CellAddress(sheet.Id, 10, 2)),
        };

        // At the anchor cell itself (B2), $A2 is unshifted -> Fruits.
        DataValidationService.GetListItems(rule, sheet, anchor, workbook)
            .Should().Equal("Apple", "Banana");

        // At B5, $A2 must shift to $A5 -> Veggies, NOT stay pinned to the anchor's A2/Fruits.
        var b5 = new CellAddress(sheet.Id, 5, 2);
        DataValidationService.GetListItems(rule, sheet, b5, workbook)
            .Should().Equal("Carrot", "Pea");

        // The back-compat overload (no address) must keep behaving as before: anchor-relative.
        DataValidationService.GetListItems(rule, sheet, workbook)
            .Should().Equal("Apple", "Banana");
    }

    [Fact]
    public void Validate_ForIndirectListSource_ShiftsRelativeReferencePerValidatedCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Fruits")));  // A2
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new TextValue("Veggies"))); // A5

        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromValue(new TextValue("Apple")));  // D1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), Cell.FromValue(new TextValue("Banana"))); // D2
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromValue(new TextValue("Carrot"))); // D4
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), Cell.FromValue(new TextValue("Pea")));    // D5

        workbook.DefineNamedRange("Fruits", new GridRange(
            new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 4)));
        workbook.DefineNamedRange("Veggies", new GridRange(
            new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 5, 4)));

        var anchor = new CellAddress(sheet.Id, 2, 2);
        var rule = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=INDIRECT($A2)",
            AppliesTo = new GridRange(anchor, new CellAddress(sheet.Id, 10, 2)),
        };

        var b5 = new CellAddress(sheet.Id, 5, 2);

        // "Pea" belongs to the Veggies list resolved for row 5 (via shifted $A5) — must validate.
        DataValidationService.Validate(rule, new TextValue("Pea"), sheet, b5, workbook)
            .Should().BeNull();

        // "Apple" belongs only to the Fruits list (row 2's list); it must NOT validate for B5,
        // which is the cascading-dropdown bug this test guards against (evaluating from the
        // anchor's A2 for every cell instead of shifting to A5).
        DataValidationService.Validate(rule, new TextValue("Apple"), sheet, b5, workbook)
            .Should().NotBeNull();
    }
}
