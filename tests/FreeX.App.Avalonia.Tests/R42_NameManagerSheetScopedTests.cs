using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class R42_NameManagerSheetScopedTests
{
    [Fact]
    public void DeletePlan_UsesRowsRealSheetScope_AndPreservesSameNamedGlobal()
    {
        var (workbook, sheet1, _) = CreateWorkbook();
        var globalRange = Cell(sheet1, 5, 5);
        workbook.DefineNamedRange("Rate", globalRange);
        workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);
        var session = new DefinedNamesSession(workbook, sheet1.Id);
        var scopedRow = session.BuildRows().Single(row => row.Name == "Rate" && !row.Scope.IsWorkbook);

        var outcome = Run(workbook, session.BuildDeleteCommand(scopedRow));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", sheet1.Id));
        workbook.NamedRanges["Rate"].Should().Be(globalRange);
    }

    [Fact]
    public void Validation_RejectsOnlyNamesInTheExactTargetScope()
    {
        var (workbook, sheet1, sheet2) = CreateWorkbook();
        workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), new NamedRangeMetadata("Sheet1", ""), sheet1.Id);
        var session = new DefinedNamesSession(workbook, sheet1.Id);

        session.ValidateName("Rate", session.GetScope(sheet1.Id)).Error.Should().Be(DefinedNameError.Duplicate);
        session.ValidateName("Rate", session.GetScope(sheet2.Id)).IsValid.Should().BeTrue();
        session.ValidateName("Rate", DefinedNameScope.Workbook).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WorkbookLabelCollision_RetainsDistinctGlobalAndSheetIdentities()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        workbook.DefineNamedRange("Rate", Cell(sheet, 1, 1));
        workbook.DefineNamedRange("Rate", Cell(sheet, 2, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        var session = new DefinedNamesSession(workbook, sheet.Id);

        var rows = session.BuildRows().Where(row => row.Name == "Rate").ToList();

        rows.Should().HaveCount(2);
        rows.Select(row => row.ScopeLabel).Should().OnlyContain(label => label == "Workbook");
        rows.Should().ContainSingle(row => row.Scope.IsWorkbook);
        rows.Should().ContainSingle(row => row.Scope.SheetId == sheet.Id);
    }

    private static (Workbook Workbook, Sheet Sheet1, Sheet Sheet2) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        return (workbook, workbook.AddSheet("Sheet1"), workbook.AddSheet("Sheet2"));
    }

    private static GridRange Cell(Sheet sheet, uint row, uint col) =>
        new(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new TestContext(workbook));

    private sealed class TestContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
