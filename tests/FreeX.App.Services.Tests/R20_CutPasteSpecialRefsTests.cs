using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for R20-paste-special-operations-1: Cut + "Paste Special &gt; Values" must
/// be treated as an Excel MOVE, not a copy+clear. Before the fix, TryCreateCutMoveCommand only
/// routed the plain default "Paste" gesture through MoveRangeCommand's reference-fixup semantics;
/// any Paste Special variant (including mode == PasteCellsMode.Values) fell back to
/// CreateInternalPasteCommand + ClearContentsCommand, which never rewrites OTHER formulas that
/// pointed at the cut cells -- they were silently left pointing at the now-blank source instead of
/// following the move.
/// </summary>
public sealed class R20_paste_special_cut_refs_Tests
{
    [Fact]
    public void PasteSpecialValues_AfterCut_RewritesDependentFormulaToFollowTheMove()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c5 = new CellAddress(sheet.Id, 5, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1");
        sheet.SetFormula(c5, "B1+5");

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.RecalculateWorkbook();
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(c5)!.Value.Should().Be(new NumberValue(15));

        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d1);

        var paste = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        paste.Success.Should().BeTrue();

        // The moved cell lands at the destination as a plain value (Paste Special > Values), and
        // the source is cleared -- exactly like a plain cut+paste move.
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(d1)!.FormulaText.Should().BeNull();
        sheet.GetCell(d1)!.Value.Should().Be(new NumberValue(10));

        // The OTHER formula that referenced the cut cell must re-point to the new location,
        // exactly like a plain cut+paste move -- not silently break to reference the now-blank
        // source cell (which would leave it evaluating to 5 instead of 15).
        sheet.GetCell(c5)!.FormulaText.Should().Be("D1+5");
        sheet.GetCell(c5)!.Value.Should().Be(new NumberValue(15));
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
