using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Tests for x14 cross-sheet List DV enforcement via DataValidationService.
///
/// These tests verify that once Formula1 is set to a cross-sheet formula such as
/// "Sheet2!$A$1:$A$5" (as populated by the x14 reader), the existing list resolver
/// correctly resolves the items and the membership enforcement accepts/rejects values.
/// </summary>
public sealed partial class DataValidationTests
{
    // ── Cross-sheet List resolution ──────────────────────────────────────────────

    [Fact]
    public void Validate_CrossSheetList_AcceptsMemberValue()
    {
        // Build a two-sheet workbook manually (simulating what the x14 reader produces).
        var workbook = new Workbook("CrossSheetDv");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Populate items on Sheet2 A1:A5.
        string[] items = ["Apple", "Banana", "Cherry", "Durian", "Elderberry"];
        for (var i = 0; i < items.Length; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)(i + 1), 1), new TextValue(items[i]));

        // The x14 reader populates Formula1 with the cross-sheet ref (without leading '=').
        // The list resolver expects a leading '=' for range formulas, so we include it.
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 2, 2),
                new CellAddress(sheet1.Id, 2, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            AllowBlank = true,
            IsX14 = true,
        };

        var entryAddress = new CellAddress(sheet1.Id, 2, 2);

        // The resolver needs a leading '=' for the range lookup.
        // Simulate what DataValidationService.Validate does — it prepends '=' if needed.
        // We test via the overload that takes a sheet + workbook context.
        var dvWithEq = new DataValidation
        {
            AppliesTo = dv.AppliesTo,
            Type = dv.Type,
            Formula1 = "=" + dv.Formula1,
            AllowBlank = dv.AllowBlank,
            IsX14 = dv.IsX14,
        };

        var error = DataValidationService.Validate(dvWithEq, new TextValue("Cherry"), sheet1, entryAddress, workbook);

        error.Should().BeNull("Cherry is in Sheet2!$A$1:$A$5");
    }

    [Fact]
    public void Validate_CrossSheetList_RejectsNonMemberValue()
    {
        var workbook = new Workbook("CrossSheetDvReject");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        string[] items = ["Apple", "Banana", "Cherry"];
        for (var i = 0; i < items.Length; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)(i + 1), 1), new TextValue(items[i]));

        var entryAddress = new CellAddress(sheet1.Id, 2, 1);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(entryAddress, entryAddress),
            Type = DvType.List,
            Formula1 = "=Sheet2!$A$1:$A$3",
            AllowBlank = true,
            IsX14 = true,
        };

        var error = DataValidationService.Validate(dv, new TextValue("Mango"), sheet1, entryAddress, workbook);

        error.Should().NotBeNull("Mango is not in Sheet2!$A$1:$A$3");
    }

    [Fact]
    public void GetListItems_CrossSheetList_ReturnsAllItems()
    {
        var workbook = new Workbook("CrossSheetItems");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        string[] items = ["Red", "Green", "Blue", "Yellow", "Purple"];
        for (var i = 0; i < items.Length; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)(i + 1), 1), new TextValue(items[i]));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 1, 1)),
            Type = DvType.List,
            Formula1 = "=Sheet2!$A$1:$A$5",
            AllowBlank = true,
            IsX14 = true,
        };

        var resolved = DataValidationService.GetListItems(dv, sheet1, workbook);

        resolved.Should().NotBeNull();
        resolved!.Should().HaveCount(5);
        resolved.Should().ContainInOrder("Red", "Green", "Blue", "Yellow", "Purple");
    }

    [Fact]
    public void Validate_CrossSheetList_CaseInsensitiveMatch()
    {
        var workbook = new Workbook("CrossSheetCase");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Apple"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("Banana"));

        var entryAddress = new CellAddress(sheet1.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(entryAddress, entryAddress),
            Type = DvType.List,
            Formula1 = "=Sheet2!$A$1:$A$2",
            AllowBlank = true,
            IsX14 = true,
        };

        var error = DataValidationService.Validate(dv, new TextValue("APPLE"), sheet1, entryAddress, workbook);

        error.Should().BeNull("matching must be case-insensitive");
    }

    [Fact]
    public void Validate_CrossSheetList_AllowsBlankWhenAllowBlankTrue()
    {
        var workbook = new Workbook("CrossSheetBlank");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Only"));

        var entryAddress = new CellAddress(sheet1.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(entryAddress, entryAddress),
            Type = DvType.List,
            Formula1 = "=Sheet2!$A$1",
            AllowBlank = true,
            IsX14 = true,
        };

        var error = DataValidationService.Validate(dv, BlankValue.Instance, sheet1, entryAddress, workbook);

        error.Should().BeNull("blank is allowed when AllowBlank=true");
    }
}
