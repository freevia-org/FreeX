using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T7 fix verification: Duplicate Sheet must not copy the source's VBA CodeName
/// verbatim onto the copy (R14-workbook-structure-2).
/// </summary>
public class FreeXR14T7Tests
{
    [Fact]
    public void DuplicateSheetCommand_RegeneratesCodeNameInsteadOfCopyingSourceVerbatim()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.CodeName = "Sheet1";
        var ctx = new TestCommandContext(wb);

        var outcome = new DuplicateSheetCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        var copy = wb.Sheets[1];

        // The source's own codeName must be untouched...
        sheet.CodeName.Should().Be("Sheet1");
        // ...and the copy must get a *different*, non-empty codeName: Excel assigns a fresh,
        // workbook-unique VBA identifier when duplicating a sheet rather than copying the
        // source's codeName verbatim (which would emit two <sheetPr codeName="Sheet1"/> entries
        // on save -- invalid OOXML that Excel treats as corrupt).
        copy.CodeName.Should().NotBeNullOrWhiteSpace();
        copy.CodeName.Should().NotBe(sheet.CodeName);

        // Duplicating again must still keep every sheet's codeName workbook-unique.
        var ctx2 = new TestCommandContext(wb);
        new DuplicateSheetCommand(copy.Id).Apply(ctx2).Success.Should().BeTrue();
        wb.Sheets.Select(s => s.CodeName).Should().OnlyHaveUniqueItems();
    }
}
