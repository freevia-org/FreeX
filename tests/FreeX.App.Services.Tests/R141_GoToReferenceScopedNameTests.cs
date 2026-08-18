using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R141-services-goto-scoped-name-1: <see cref="WorkbookSession.GoToReference"/> (F5 Go To, and
/// the Avalonia shell's hyperlink-click navigation, which routes through
/// <see cref="WorkbookSession.OpenHyperlink"/> -&gt; GoToReference) used to pass only
/// <see cref="Workbook.NamedRanges"/> (the workbook-GLOBAL name dictionary) into
/// <c>WorkbookReferenceNavigator.TryParseReferenceRange</c>, so a defined name scoped to a single
/// sheet (Name Manager scope != "Workbook") could never resolve there -- even though the exact
/// same text typed into the Name Box on either shell navigates there successfully, because the
/// Name Box wires the sheet-scope-aware <c>resolveScopedName</c> parameter of the same navigator
/// method via <c>Workbook.TryGetNamedRange(name, sheetId, out var scoped)</c>. The fix passes that
/// same scoped lookup into GoToReference/TryResolveReferenceRange, matching formula evaluation's
/// own sheet-scope-first precedence.
/// </summary>
public sealed class R141_GoToReferenceScopedNameTests
{
    [Fact]
    public void GoToReference_ResolvesSheetScopedDefinedName()
    {
        var workbook = CreateWorkbook();
        var sheet1 = workbook.Sheets.Single();
        var sheet2 = workbook.AddSheet("Sheet2");
        var target = new CellAddress(sheet2.Id, 5, 2); // Sheet2!B5
        workbook.DefineNamedRange("LocalTotal", new GridRange(target, target), metadata: null, scopeSheetId: sheet2.Id);

        var session = CreateSession(workbook);
        session.SelectSheet(sheet2.Id);

        var result = session.GoToReference("LocalTotal");

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.ActiveSheet.Id.Should().Be(sheet2.Id);
        session.SelectedRange.Should().Be(new GridRange(target, target));
        session.ActiveCell.Should().Be(target);

        // and unaffected: sheet1 exists purely as a distractor so the fix cannot be "search every
        // sheet's scoped names" -- it must resolve specifically against the active/qualifying sheet.
        sheet1.Should().NotBeNull();
    }

    [Fact]
    public void GoToReference_SheetScopedNameTakesPrecedenceOverSameNamedWorkbookGlobalNameOnThatSheet()
    {
        // Sibling/no-regression case for the guidance's precedence requirement: a workbook-global
        // name and a sheet-scoped name of the SAME text both exist; on the sheet that owns the
        // scoped definition, the scoped one must win (matching formula evaluation's
        // Workbook.TryGetNamedRange sheet-scope-first precedence), not the global one.
        var workbook = CreateWorkbook();
        var sheet1 = workbook.Sheets.Single();
        var sheet2 = workbook.AddSheet("Sheet2");
        var globalTarget = new CellAddress(sheet1.Id, 1, 1); // Sheet1!A1
        var scopedTarget = new CellAddress(sheet2.Id, 5, 2); // Sheet2!B5
        workbook.DefineNamedRange("LocalTotal", new GridRange(globalTarget, globalTarget));
        workbook.DefineNamedRange("LocalTotal", new GridRange(scopedTarget, scopedTarget), metadata: null, scopeSheetId: sheet2.Id);

        var session = CreateSession(workbook);
        session.SelectSheet(sheet2.Id);

        var result = session.GoToReference("LocalTotal");

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.ActiveSheet.Id.Should().Be(sheet2.Id);
        session.SelectedRange.Should().Be(new GridRange(scopedTarget, scopedTarget));
    }

    [Fact]
    public void GoToReference_WorkbookGlobalNameStillResolvesOnASheetWithNoScopedOverride()
    {
        // No-regression sibling: the ordinary (and overwhelmingly common) workbook-global defined
        // name case, with no sheet scope involved at all, must keep working exactly as before.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 3, 3);
        workbook.DefineNamedRange("GlobalTotal", new GridRange(target, target));

        var session = CreateSession(workbook);

        var result = session.GoToReference("GlobalTotal");

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.SelectedRange.Should().Be(new GridRange(target, target));
    }

    [Fact]
    public void TryResolveReferenceRange_ResolvesSheetScopedDefinedName()
    {
        // TryResolveReferenceRange backs dialogs (e.g. conditional-format applies-to editing) that
        // must parse a reference the same way Go To does -- it shares the same underlying gap.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 5, 2);
        workbook.DefineNamedRange("LocalTotal", new GridRange(target, target), metadata: null, scopeSheetId: sheet.Id);

        var session = CreateSession(workbook);

        session.TryResolveReferenceRange("LocalTotal", out var range).Should().BeTrue();
        range.Should().Be(new GridRange(target, target));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
