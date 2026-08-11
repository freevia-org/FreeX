using System.Linq;
using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for a worksheet whose display name is the same as the workbook-scope label.
/// Scope identity is owned by <see cref="DefinedNamesSession"/> and never reconstructed from renderer text.
/// </summary>
public sealed class R134_NameManagerWorkbookScopeIdentityTests
{
    [Fact]
    public void BuildRows_ForNameScopedToSheetNamedWorkbook_CarriesSheetIdentity()
    {
        var (workbook, _, workbookSheet) = CreateAmbiguousWorkbook();
        workbook.DefineNamedRange(
            "Rate",
            Cell(workbookSheet, 2, 2),
            new NamedRangeMetadata("Workbook", ""),
            workbookSheet.Id);

        var row = new DefinedNamesSession(workbook, workbookSheet.Id)
            .BuildRows()
            .Single(candidate => candidate.Name == "Rate");

        row.ScopeLabel.Should().Be("Workbook");
        row.Scope.SheetId.Should().Be(workbookSheet.Id);
        row.Scope.IsWorkbook.Should().BeFalse();
    }

    [Fact]
    public void BuildRows_ForWorkbookGlobalName_CarriesWorkbookIdentity()
    {
        var (workbook, sheet1, _) = CreateAmbiguousWorkbook();
        workbook.DefineNamedRange("Total", Cell(sheet1, 1, 1));

        var row = new DefinedNamesSession(workbook, sheet1.Id)
            .BuildRows()
            .Single(candidate => candidate.Name == "Total");

        row.Scope.IsWorkbook.Should().BeTrue();
        row.Scope.SheetId.Should().BeNull();
    }

    [Fact]
    public void DeleteFlow_OnSheetNamedWorkbook_RemovesScopedEntryOnly()
    {
        var (workbook, sheet1, workbookSheet) = CreateAmbiguousWorkbook();
        var globalRange = Cell(sheet1, 1, 1);
        var scopedRange = Cell(workbookSheet, 2, 2);
        workbook.DefineNamedRange("Rate", globalRange);
        workbook.DefineNamedRange(
            "Rate",
            scopedRange,
            new NamedRangeMetadata("Workbook", ""),
            workbookSheet.Id);

        var session = new DefinedNamesSession(workbook, workbookSheet.Id);
        var row = session.BuildRows().Single(candidate =>
            candidate.Name == "Rate" && candidate.Scope.SheetId == workbookSheet.Id);

        var outcome = session.BuildDeleteCommand(row).Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", workbookSheet.Id));
        workbook.NamedRanges["Rate"].Should().Be(globalRange);
    }

    [Fact]
    public void DeleteFlow_OnGlobalName_LeavesSameTextScopedEntry()
    {
        var (workbook, sheet1, workbookSheet) = CreateAmbiguousWorkbook();
        var globalRange = Cell(sheet1, 1, 1);
        var scopedRange = Cell(workbookSheet, 2, 2);
        workbook.DefineNamedRange("Rate", globalRange);
        workbook.DefineNamedRange(
            "Rate",
            scopedRange,
            new NamedRangeMetadata("Workbook", ""),
            workbookSheet.Id);

        var session = new DefinedNamesSession(workbook, sheet1.Id);
        var row = session.BuildRows().Single(candidate =>
            candidate.Name == "Rate" && candidate.Scope.IsWorkbook);

        var outcome = session.BuildDeleteCommand(row).Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        workbook.NamedRanges.Should().NotContainKey("Rate");
        workbook.ScopedNamedRanges[("Rate", workbookSheet.Id)].Should().Be(scopedRange);
    }

    [Fact]
    public void FindScopeIndex_UsesScopeIdentityWhenLabelsCollide()
    {
        var (workbook, _, workbookSheet) = CreateAmbiguousWorkbook();
        var session = new DefinedNamesSession(workbook, workbookSheet.Id);
        var sheetScope = session.GetScope(workbookSheet.Id);

        var sheetIndex = session.FindScopeIndex(sheetScope);
        var workbookIndex = session.FindScopeIndex(DefinedNameScope.Workbook);

        sheetIndex.Should().NotBe(workbookIndex);
        session.ScopeChoices[sheetIndex].SheetId.Should().Be(workbookSheet.Id);
        session.ScopeChoices[workbookIndex].IsWorkbook.Should().BeTrue();
    }

    private static (Workbook Workbook, Sheet Sheet1, Sheet WorkbookSheet) CreateAmbiguousWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var workbookSheet = workbook.AddSheet("Workbook");
        return (workbook, sheet1, workbookSheet);
    }

    private static GridRange Cell(Sheet sheet, uint row, uint col) =>
        new(new CellAddress(sheet.Id, row, col), new CellAddress(sheet.Id, row, col));

}
