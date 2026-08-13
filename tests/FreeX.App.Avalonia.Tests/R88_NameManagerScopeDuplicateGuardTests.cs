using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class R88_NameManagerScopeDuplicateGuardTests
{
    [Fact]
    public void OriginalNameIsExcludedOnlyForTheSameScopeIdentity()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("Foo", Cell(sheet, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        workbook.DefineNamedRange("Foo", Cell(sheet, 2));
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var seed = session.BuildRows().Single(row => row.Name == "Foo" && row.Scope.SheetId == sheet.Id);

        session.ValidateName("Foo", seed.Scope, seed.Identity).IsValid.Should().BeTrue();
        session.ValidateName("Foo", DefinedNameScope.Workbook, seed.Identity)
            .Error.Should().Be(DefinedNameError.Duplicate);
    }

    private static GridRange Cell(Sheet sheet, uint row) =>
        new(new CellAddress(sheet.Id, row, 1), new CellAddress(sheet.Id, row, 1));
}
