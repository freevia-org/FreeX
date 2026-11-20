using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Excel's background error checking: the green indicator on cells that break an error-checking
/// rule, and the smart tag's Convert to Number and Ignore Error.
/// </summary>
public sealed class BackgroundErrorIndicatorTests
{
    [Fact]
    public void NumberStoredAsText_ShowsAnIndicator_AndConvertToNumberFixesItAsOneUndoStep()
    {
        var (session, sheet) = CreateSession();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("123"));
        sheet.SetCell(a2, new TextValue("4.5"));
        var cache = new BackgroundErrorIndicatorCache();

        cache.GetErrorCode(session.Workbook, a1).Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);

        session.SelectRange(new GridRange(a1, a2));
        session.ConvertSelectionTextToNumbers().Success.Should().BeTrue();

        sheet.GetValue(a1).Should().Be(new NumberValue(123));
        sheet.GetValue(a2).Should().Be(new NumberValue(4.5));
        cache.GetErrorCode(session.Workbook, a1).Should().BeNull("the converted cell no longer shows the indicator");

        session.UndoLastEdit();
        sheet.GetValue(a1).Should().Be(new TextValue("123"), "one undo restores every converted cell");
        sheet.GetValue(a2).Should().Be(new TextValue("4.5"));
    }

    [Fact]
    public void FormulaReturningAnError_ShowsAnIndicator_ButATypedErrorConstantDoesNot()
    {
        var (session, sheet) = CreateSession();
        var formulaCell = new CellAddress(sheet.Id, 1, 1);
        var constantCell = new CellAddress(sheet.Id, 2, 1);
        session.SelectCell(formulaCell);
        session.CommitCellText("=1/0").Success.Should().BeTrue();
        sheet.SetCell(constantCell, ErrorValue.NA);

        var indicators = FormulaAuditingService.FindBackgroundErrorIndicators(session.Workbook, sheet.Id);

        indicators.Should().ContainKey(formulaCell).WhoseValue.Should().Be(ErrorValue.DivByZero.Code);
        indicators.Should().NotContainKey(constantCell);
    }

    [Fact]
    public void IgnoreError_HidesTheIndicator_AndUndoBringsItBack()
    {
        var (session, sheet) = CreateSession();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("42"));
        var cache = new BackgroundErrorIndicatorCache();
        cache.GetErrorCode(session.Workbook, a1).Should().NotBeNull();

        session.SelectCell(a1);
        session.IgnoreSelectionErrors().Success.Should().BeTrue();

        cache.GetErrorCode(session.Workbook, a1).Should().BeNull();
        sheet.GetValue(a1).Should().Be(new TextValue("42"), "Ignore Error keeps the text");

        session.UndoLastEdit();
        cache.GetErrorCode(session.Workbook, a1).Should().NotBeNull();
    }

    [Fact]
    public void DisablingTheRule_RemovesItsIndicators()
    {
        var (session, sheet) = CreateSession();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("7"));
        var cache = new BackgroundErrorIndicatorCache();
        cache.GetErrorCode(session.Workbook, a1).Should().NotBeNull();

        session.Workbook.DisabledFormulaErrorCodes.Add(FormulaAuditingService.NumberStoredAsTextErrorCode);

        cache.GetErrorCode(session.Workbook, a1).Should().BeNull();
    }

    [Fact]
    public void SmartTagMenu_FollowsExcel()
    {
        ErrorSmartTagPlanner.BuildMenu(FormulaAuditingService.NumberStoredAsTextErrorCode)
            .Select(item => item.IsSeparator ? "-" : item.Header)
            .Should().Equal(
                "Number Stored as Text",
                "Convert to Number",
                "Help on this Error",
                "Ignore Error",
                "Edit in Formula Bar",
                "-",
                "Error Checking Options...");

        ErrorSmartTagPlanner.BuildMenu(ErrorValue.DivByZero.Code)
            .Select(item => item.Header)
            .Should().StartWith(["Divide by Zero Error", "Show Calculation Steps..."]);
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(new CellAddress(sheet.Id, 1, 1));
        return (session, sheet);
    }
}
