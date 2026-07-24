using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R83-io-vba-macro-5-1: AddSheetCommand must assign a fresh, workbook-unique VBA CodeName to a
/// newly added sheet when the workbook carries a VBA project (macro-enabled workbook), matching
/// real Excel's own Insert Sheet behavior and the existing DuplicateSheetCommand codeName-
/// regeneration fix (see FreeXR14T7Tests). Before the fix, Workbook.AddSheet/InsertSheet never
/// assigned a CodeName at all, leaving the new sheet as the only worksheet with no sheetPr/
/// @codeName in a macro-enabled workbook.
/// </summary>
public class R83_AddSheetCommandVbaCodeNameTests
{
    [Fact]
    public void AddSheetCommand_AssignsUniqueCodeName_WhenWorkbookHasVbaProject()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        sheet1.CodeName = "Sheet1";
        wb.HasVbaProjectPackage = true;
        var ctx = new TestCommandContext(wb);

        var outcome = new AddSheetCommand("Sheet2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        var added = wb.Sheets[1];

        added.CodeName.Should().NotBeNullOrWhiteSpace(
            "a sheet added to a macro-enabled workbook must get its own VBA codeName, like every " +
            "other worksheet in a real .xlsm");
        added.CodeName.Should().NotBe(sheet1.CodeName);
        wb.Sheets.Select(s => s.CodeName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AddSheetCommand_Redo_ReusesSameCodeNameInsteadOfMintingANewOne()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1").CodeName = "Sheet1";
        wb.HasVbaProjectPackage = true;
        var cmd = new AddSheetCommand("Sheet2");
        var ctx = new TestCommandContext(wb);

        cmd.Apply(ctx).Success.Should().BeTrue();
        var firstCodeName = wb.Sheets[1].CodeName;

        // Undo then redo (Apply is called again on the same command instance, mirroring the
        // R16 redo-stability fix for _addedSheetId above it).
        cmd.Revert(ctx);
        cmd.Apply(ctx).Success.Should().BeTrue();
        var secondCodeName = wb.Sheets[1].CodeName;

        secondCodeName.Should().Be(firstCodeName,
            "redoing an Add Sheet must not mint a second, different codeName for the same logical sheet");
    }

    // No-regression sibling: a plain (non-macro) workbook must keep its prior behavior of NOT
    // stamping an unnecessary codeName onto newly added sheets.
    [Fact]
    public void AddSheetCommand_DoesNotAssignCodeName_WhenWorkbookHasNoVbaProject()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1");
        // wb.HasVbaProjectPackage defaults to false.
        var ctx = new TestCommandContext(wb);

        var outcome = new AddSheetCommand("Sheet2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets[1].CodeName.Should().BeNullOrWhiteSpace();
    }

    // Fix-guidance regression: deleting a sheet in a macro-enabled workbook must leave the
    // surviving sheets' codeName bindings untouched (RemoveSheetCommand never mutates another
    // sheet's CodeName -- it only rewrites dangling formula/reference text).
    [Fact]
    public void RemoveSheetCommand_PreservesSurvivingSheetsCodeNameBindings()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        sheet1.CodeName = "Sheet1";
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.CodeName = "Sheet2";
        var sheet3 = wb.AddSheet("Sheet3");
        sheet3.CodeName = "Sheet3";
        wb.HasVbaProjectPackage = true;
        var ctx = new TestCommandContext(wb);

        var outcome = new RemoveSheetCommand(sheet2.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        sheet1.CodeName.Should().Be("Sheet1");
        sheet3.CodeName.Should().Be("Sheet3");
    }
}
