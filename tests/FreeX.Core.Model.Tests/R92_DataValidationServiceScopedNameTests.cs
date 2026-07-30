using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-io-defined-name-scope-eval-5-2: a Data-Validation List source formula that explicitly
/// sheet-qualifies a defined name (e.g. "=Sheet2!Data") must resolve against THAT sheet's own
/// defined-name scope, not the validated cell's own sheet -- even when the validated cell's own
/// sheet happens to have its own, differently-defined, same-named local name. Covers both real
/// product entry points that resolve a List source: <see cref="DataValidationService.GetListItems"/>
/// (dropdown population) and the 4-arg <see cref="DataValidationService.Validate"/> overload
/// (entry acceptance/rejection).
/// </summary>
public sealed class R92_DataValidationServiceScopedNameTests
{
    private static (Workbook workbook, Sheet sheet1, Sheet sheet2) BuildTwoSheetsWithOwnLocalData()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new TextValue("Apple")));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), Cell.FromValue(new TextValue("Banana")));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 1), Cell.FromValue(new TextValue("Cherry")));
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1)),
            null,
            sheet1.Id);

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), Cell.FromValue(new TextValue("Red")));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), Cell.FromValue(new TextValue("Green")));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 2), Cell.FromValue(new TextValue("Blue")));
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet2.Id, 1, 2), new CellAddress(sheet2.Id, 3, 2)),
            null,
            sheet2.Id);

        return (workbook, sheet1, sheet2);
    }

    [Fact]
    public void GetListItems_SheetQualifiedNameResolvesToQualifiedSheetsOwnLocalDefinition()
    {
        var (workbook, sheet1, _) = BuildTwoSheetsWithOwnLocalData();
        var address = new CellAddress(sheet1.Id, 1, 3); // C1 on Sheet1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=Sheet2!Data",
            AppliesTo = new GridRange(address, address),
        };

        var items = DataValidationService.GetListItems(dv, sheet1, address, workbook);

        items.Should().Equal("Red", "Green", "Blue");
    }

    [Fact]
    public void Validate_SheetQualifiedNameAcceptsQualifiedSheetsValueAndRejectsHostSheetsShadowedValue()
    {
        var (workbook, sheet1, _) = BuildTwoSheetsWithOwnLocalData();
        var address = new CellAddress(sheet1.Id, 1, 3); // C1 on Sheet1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=Sheet2!Data",
            AppliesTo = new GridRange(address, address),
            ErrorMessage = "No match",
        };

        // "Red" belongs to Sheet2's own "Data" (the sheet the reference is explicitly qualified
        // with) and must be accepted.
        DataValidationService.Validate(dv, new TextValue("Red"), sheet1, address, workbook)
            .Should().BeNull();

        // "Apple" belongs to Sheet1's own local "Data" (the validated cell's own sheet) which the
        // explicit "Sheet2!" qualifier does NOT refer to, so it must be rejected.
        DataValidationService.Validate(dv, new TextValue("Apple"), sheet1, address, workbook)
            .Should().Be("No match");
    }

    /// <summary>No-regression sibling: an UNqualified name reference must still resolve against
    /// the validated cell's own sheet, exactly as before this fix.</summary>
    [Fact]
    public void GetListItems_UnqualifiedNameStillResolvesAgainstHostSheetsOwnLocalDefinition()
    {
        var (workbook, sheet1, _) = BuildTwoSheetsWithOwnLocalData();
        var address = new CellAddress(sheet1.Id, 1, 3); // C1 on Sheet1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=Data",
            AppliesTo = new GridRange(address, address),
        };

        var items = DataValidationService.GetListItems(dv, sheet1, address, workbook);

        items.Should().Equal("Apple", "Banana", "Cherry");
    }
}
