using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for two round-27 data-validation List-source findings in
/// DataValidationService.ListSources.cs:
///
/// R27-data-validation-eval-deep-1: ValidateList(dv, value, sheet, address, workbook) fell through
/// to the 2-arg ValidateList(dv, value) whenever a formula-based List source (e.g. a cascading
/// =INDIRECT($A2) dropdown) evaluated to an ErrorValue, so ParseInlineListItems treated the raw,
/// unevaluated "=INDIRECT($A2)" formula text as the one and only allowed value, rejecting every
/// real entry. Real Excel does not enforce List validation when the source formula can't be
/// evaluated to a set of allowed values -- it accepts any entry instead.
///
/// R27-data-validation-eval-deep-3: ToValidationText had no case for DateTimeValue, so a List
/// source cell holding a date fell through to the default record ToString()
/// ("DateTimeValue { Value = 45292 }") instead of rendering as the OADate serial text, causing a
/// numerically-identical NumberValue entry to be wrongly rejected.
/// </summary>
public class R27_DataValidationListSourceTests
{
    [Fact]
    public void Validate_CascadingListSourceErrors_AcceptsAnyValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2); // B2

        // A2 is blank, so INDIRECT($A2) -> INDIRECT("") is a #REF! error, exactly like a cascading
        // dropdown before the user has picked the upstream category.
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=INDIRECT($A2)",
            AppliesTo = new GridRange(address, address),
        };

        // Real Excel does not block entry when the List source can't be resolved -- it must not
        // reject the value against the literal, unevaluated "=INDIRECT($A2)" formula text.
        DataValidationService.Validate(dv, new TextValue("Anything"), sheet, address, wb)
            .Should().BeNull("an unresolvable List source must not restrict entry");
    }

    [Fact]
    public void Validate_CascadingListSourceResolves_StillEnforcesAllowedValues()
    {
        // Sibling already-working case: once the upstream cell makes the cascading source
        // resolvable, the List rule must still enforce the resolved values normally.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2); // B2

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("D1")); // A2 = "D1"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Cyan"));    // D1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Magenta")); // D2

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=INDIRECT($A2)",
            AppliesTo = new GridRange(address, address),
        };

        DataValidationService.Validate(dv, new TextValue("Cyan"), sheet, address, wb)
            .Should().BeNull("Cyan is in the resolved source range D1:D2");

        DataValidationService.Validate(dv, new TextValue("Yellow"), sheet, address, wb)
            .Should().NotBeNull("Yellow is not in the resolved source range D1:D2");
    }

    [Fact]
    public void Validate_DateListSource_MatchesEquivalentOADateSerial()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 3); // C1

        // A1:A2 hold dates as DateTimeValue (e.g. read back from a date-formatted range).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45292)); // 2024-01-01
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(45293)); // 2024-01-02

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=A1:A2",
            AppliesTo = new GridRange(address, address),
        };

        // A raw OADate serial entered/pasted into the validated cell must match the date list item.
        DataValidationService.Validate(dv, new NumberValue(45292), sheet, address, wb)
            .Should().BeNull("45292 is the OADate serial for one of the date list items");
    }

    [Fact]
    public void Validate_DateListSource_RejectsSerialNotInList()
    {
        // Sibling already-working case: the range-containment check must still discriminate and
        // reject values that don't match any list item, not accept everything.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 3); // C1

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45292)); // 2024-01-01
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(45293)); // 2024-01-02

        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=A1:A2",
            AppliesTo = new GridRange(address, address),
        };

        DataValidationService.Validate(dv, new NumberValue(45999), sheet, address, wb)
            .Should().NotBeNull("45999 does not match either date list item's OADate serial");
    }
}
