using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R29-performance-scale-correctness-3: a List-validation source pointing at
/// the full nominal extent of a column (e.g. "=$A$1:$A$1048576", the explicit-bounds form real
/// Excel uses for an entire-column selection) used to build and string.Join the full ~1,048,576-row
/// resolved range into the rejection message whenever the rule's ErrorMessage was left blank (the
/// default), both re-scanning the whole column a second time and allocating a multi-megabyte string
/// on every rejected edit.
///
/// Real Excel's default rejection message (no custom Error Alert text authored) is a fixed, generic
/// sentence; it never enumerates the source list, regardless of size. DataValidationService now
/// returns that fixed message directly instead of materializing/joining the resolved list, and skips
/// the redundant re-scan entirely when the fast range/named-source match check already answered the
/// question.
/// </summary>
public class R29_DataValidationListSourceScaleErrorMessageTests
{
    [Fact]
    public void Validate_FullExtentColumnListSource_RejectedEntryGetsFixedGenericMessage_NotEnumeratedList()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 10, 2); // B10

        // Column A has only a handful of populated rows; the source spans the column's full nominal
        // extent -- the bug scenario, where ErrorMessage is intentionally left blank (the default /
        // unauthored case).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Red"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Green"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Blue"));

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$1048576",
            AppliesTo = new GridRange(address, address),
        };

        var message = DataValidationService.Validate(dv, new TextValue("Purple"), sheet, address, wb);

        message.Should().Be(
            "Value must match one of the list items.",
            "real Excel shows a fixed generic message for an unauthored Error Alert, never the " +
            "enumerated source list");
        message.Should().NotContain("Red").And.NotContain("Green").And.NotContain("Blue");
    }

    [Fact]
    public void Validate_FullExtentColumnListSource_StillAcceptsAndRejectsCorrectly()
    {
        // Sibling already-working case: switching to the fixed message must not break the actual
        // match/no-match decision for a full-extent column source.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 10, 2); // B10

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Red"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Green"));

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$1048576",
            AppliesTo = new GridRange(address, address),
        };

        DataValidationService.Validate(dv, new TextValue("green"), sheet, address, wb)
            .Should().BeNull("Green is present in column A (case-insensitive match)");

        DataValidationService.Validate(dv, new TextValue("Purple"), sheet, address, wb)
            .Should().NotBeNull("Purple is not present anywhere in column A");
    }

    [Fact]
    public void Validate_FullExtentColumnListSource_CustomErrorMessage_IsUnaffected()
    {
        // Sibling already-working case: an authored custom ErrorMessage must still be returned
        // verbatim, exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 10, 2); // B10

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Red"));

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$1048576",
            AppliesTo = new GridRange(address, address),
            ErrorMessage = "Please pick a listed color."
        };

        DataValidationService.Validate(dv, new TextValue("Purple"), sheet, address, wb)
            .Should().Be("Please pick a listed color.");
    }

    [Fact]
    public void Validate_SmallInlineListSource_RejectedEntryGetsSameFixedGenericMessage()
    {
        // Sibling already-working case: the same fixed-message behavior applies to the (already
        // small, scale-unaffected) literal inline-list source path, via the 2-arg overload used
        // when there is no sheet/workbook context.
        var sheetId = SheetId.New();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Red,Green,Blue",
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1)),
        };

        DataValidationService.Validate(dv, new TextValue("Purple"))
            .Should().Be("Value must match one of the list items.");
    }
}
