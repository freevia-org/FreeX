using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R69-meta-1: the r68 fix threaded an optional cyclicCells parameter into
// FormulaAuditingService.FindFormulaErrors/FindFormulaErrorIssues, but no real caller (WPF host,
// Avalonia shell, BackstageInfoPlanner) ever passed it, so the "Formulas with circular references"
// Error-Checking rule stayed a no-op for users. WorkbookSession.CyclicCells is the accessor the
// Avalonia shell (and BackstageInfoPlanner via its own recalc engine) uses to reach the session's
// RecalcEngine.CyclicCells set without exposing the engine itself.
public sealed class WorkbookSessionCyclicCellsTests
{
    [Fact]
    public void CyclicCells_ReflectsEngineCyclicCells_AfterRecalculateWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1");

        var session = CreateSession(workbook);
        session.CyclicCells.Should().BeEmpty("no recalculation has run yet");

        session.RecalculateWorkbook();

        session.CyclicCells.Should().Contain(a1,
            "the session must surface the engine's currently-cyclic cells so Error Checking can flag them");
    }

    // Sibling/no-regression: a workbook with no circular reference at all must report an empty
    // cyclic-cells set after recalculation, whether or not any formula was ever evaluated.
    [Fact]
    public void CyclicCells_IsEmpty_WhenWorkbookHasNoCircularReference()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(5));
        sheet.SetFormula(a1, "B1+1");

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();

        session.CyclicCells.Should().BeEmpty();
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(6));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
