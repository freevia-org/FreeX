using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    [Fact]
    public void Validate_List_AcceptsValueInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Apple"));

        result.Should().BeNull("Apple is in the allowed list");
    }

    [Fact]
    public void Validate_List_RejectsValueNotInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"));

        result.Should().NotBeNull("Mango is not in the allowed list");
    }

    [Fact]
    public void Validate_List_BlankAllowed_WhenAllowBlankTrue()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().BeNull("blank is allowed when AllowBlank=true");
    }

    [Fact]
    public void Validate_List_BlankRejected_WhenAllowBlankFalse()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = false,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().NotBeNull("blank should be rejected when AllowBlank=false");
    }

    [Fact]
    public void Validate_List_CaseInsensitive()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("apple"));

        result.Should().BeNull("matching should be case-insensitive");
    }

    // ─── WholeNumber validation ───────────────────────────────────────────────

    [Fact]
    public void Validate_ListRangeSource_AcceptsValueInReferencedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Banana"), sheet, entryAddress, workbook);

        result.Should().BeNull("Excel list validation accepts values from a referenced source range");
    }

    [Fact]
    public void Validate_ListRangeSource_RejectsValueOutsideReferencedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"), sheet, entryAddress, workbook);

        result.Should().NotBeNull("Mango is not present in the referenced list source range");
    }

    [Fact]
    public void Validate_ListNamedRangeSource_AcceptsValueInNamedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        workbook.DefineNamedRange("FruitList", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=FruitList",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Cherry"), sheet, entryAddress, workbook);

        result.Should().BeNull("Excel list validation accepts values from a named range source");
    }

    [Fact]
    public void Validate_ListNamedRangeSource_RejectsValueOutsideNamedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        workbook.DefineNamedRange("FruitList", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=FruitList",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"), sheet, entryAddress, workbook);

        result.Should().NotBeNull("Mango is not present in the named list source range");
    }

    [Fact]
    public void GetListItems_ReturnsRangeSourceItemsForVisibleDropdown()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            ShowDropdown = true
        };

        var items = DataValidationService.GetListItems(dv, sheet, workbook);

        items.Should().Equal("Apple", "Banana", "Cherry");
    }

    [Fact]
    public void GetListItems_ParsesQuotedInlineItemsContainingCommas()
    {
        var (_, sheet) = MakeWorkbook();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,\"Banana, ripe\",Cherry",
            ShowDropdown = true
        };

        var items = DataValidationService.GetListItems(dv, sheet);

        items.Should().Equal("Apple", "Banana, ripe", "Cherry");
    }

    [Fact]
    public void Validate_ListInlineSource_AcceptsQuotedItemContainingComma()
    {
        var (_, sheet) = MakeWorkbook();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,\"Banana, ripe\",Cherry",
            AllowBlank = true
        };

        var result = DataValidationService.Validate(dv, new TextValue("Banana, ripe"));

        result.Should().BeNull();
    }

    [Fact]
    public void GetListItems_ReturnsEmptyWhenDropdownArrowIsHidden()
    {
        var (_, sheet) = MakeWorkbook();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            ShowDropdown = false
        };

        var items = DataValidationService.GetListItems(dv, sheet);

        items.Should().BeEmpty("Excel hides the in-cell dropdown when the rule suppresses the arrow");
    }

    [Fact]
    public void FormatListSourceRange_UsesAbsoluteA1Reference()
    {
        var (_, sheet) = MakeWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var source = DataValidationService.FormatListSourceRange(range);

        source.Should().Be("=$A$1:$A$3");
    }

    [Fact]
    public void FormatListSourceRange_IncludesQuotedSheetNameWhenRequested()
    {
        var (_, sheet) = MakeWorkbook();
        sheet.Name = "Lookup Values";
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));

        var source = DataValidationService.FormatListSourceRange(range, sheet.Name);

        source.Should().Be("='Lookup Values'!$B$2:$C$4");
    }

    [Fact]
    public void FormatListSourceRange_OmitsSheetNameForCurrentSheet()
    {
        var (_, sheet) = MakeWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var source = DataValidationService.FormatListSourceRange(range, sheet.Name, sheet.Name);

        source.Should().Be("=$A$1:$A$3");
    }
}
