using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R35-deferred-cell-filename-1: CELL("filename") always returned "" because neither
/// Workbook nor IEvalContext exposed an on-disk path. Workbook.FilePath now carries the
/// path (set by the host app's open/save code); CELL("filename") reproduces Excel's
/// "drive:\path\[filename]sheetname" result once it is populated, and still returns ""
/// for a never-saved, in-memory-only workbook.
/// </summary>
public class R35_CellFilenameTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Cell_Filename_ReturnsExcelStylePath_WhenWorkbookFilePathSet()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        wb.FilePath = @"C:\R\Q1.xlsx";

        _eval.Evaluate("=CELL(\"filename\")", sheet, wb)
            .Should().Be(new TextValue(@"C:\R\[Q1.xlsx]Sheet1"));
    }

    [Fact]
    public void Cell_Filename_ReturnsEmptyString_WhenWorkbookNeverSaved()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");

        _eval.Evaluate("=CELL(\"filename\")", sheet, wb)
            .Should().Be(new TextValue(""));
    }
}
