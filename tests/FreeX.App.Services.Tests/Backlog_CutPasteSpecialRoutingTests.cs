using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for Backlog item "paste-special-cut-routing" (part a): Cut + "Paste Special
/// &gt; Formulas" (and the "Formulas and Number Formats" content kind, which the UI exposes via
/// mode == PasteCellsMode.All) must be treated as an Excel MOVE, not a copy+clear.
///
/// Before this fix, TryCreateCutMoveCommand (see WorkbookSession.cs) only routed the plain default
/// "Paste" gesture and Paste Special &gt; Values through MoveRangeCommand's reference-fixup
/// semantics (R20-paste-special-operations-1). Every other Paste Special variant -- including
/// Formulas -- fell back to CreateInternalPasteCommand + ClearContentsCommand, which:
///   (a) mis-rewrites the MOVED formula's own references as a relative-copy offset instead of
///       leaving them unchanged (move semantics), and
///   (b) never rewrites OTHER formulas that referenced the cut cells to follow the move.
/// This test file exercises both symptoms for mode == Formulas, plus the FormulasAndNumberFormats
/// content kind, through WorkbookSession's public Cut/Paste-Special API -- mirroring the existing
/// R20_CutPasteSpecialRefsTests.cs pattern for Paste Special &gt; Values.
/// </summary>
public sealed class Backlog_paste_special_cut_routing_Tests
{
    [Fact]
    public void PasteSpecialFormulas_AfterCut_KeepsMovedFormulasOwnReferencesUnchanged()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);

        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetFormula(b1, "A1");

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.RecalculateWorkbook();
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(42));

        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d5);

        var paste = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Formulas, default);

        paste.Success.Should().BeTrue();

        // Move semantics: A1 is OUTSIDE the cut range, so the moved formula's own reference must
        // stay exactly "=A1" -- not get relative-shifted to "=A5" the way the legacy copy+clear
        // path (relative offset row+4/col+2) would produce.
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(d5)!.FormulaText.Should().Be("A1");
        sheet.GetCell(d5)!.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void PasteSpecialFormulas_AfterCut_RewritesDependentFormulaToFollowTheMove()
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
        sheet.GetCell(c5)!.Value.Should().Be(new NumberValue(15));

        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d1);

        var paste = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Formulas, default);

        paste.Success.Should().BeTrue();
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(d1)!.FormulaText.Should().Be("A1");

        // The OTHER formula that referenced the cut cell must re-point to the new location, just
        // like a plain cut+paste move -- not silently keep referencing the now-blank source cell
        // (which would leave it evaluating to 5 instead of 15).
        sheet.GetCell(c5)!.FormulaText.Should().Be("D1+5");
        sheet.GetCell(c5)!.Value.Should().Be(new NumberValue(15));
    }

    [Fact]
    public void PasteSpecialFormulas_AfterCut_KeepsDestinationsOwnPrePasteStyle()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1");
        var sourceStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });
        sheet.GetCell(b1)!.StyleId = sourceStyle;
        var destinationStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "#,##0" });
        sheet.SetStyleOnly(d5.Row, d5.Col, destinationStyle);

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.RecalculateWorkbook();

        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d5);

        var paste = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Formulas, default);

        paste.Success.Should().BeTrue();
        sheet.GetCell(d5)!.FormulaText.Should().Be("A1");
        workbook.GetStyle(sheet.GetCell(d5)!.StyleId).NumberFormat.Should().Be("#,##0");
    }

    [Fact]
    public void PasteSpecialFormulasAndNumberFormats_AfterCut_KeepsOwnRefsAndMergesNumberFormat()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d5 = new CellAddress(sheet.Id, 5, 4);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1");
        var sourceStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00", Bold = true });
        sheet.GetCell(b1)!.StyleId = sourceStyle;
        var destinationStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "#,##0", Italic = true });
        sheet.SetStyleOnly(d5.Row, d5.Col, destinationStyle);

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.RecalculateWorkbook();

        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d5);

        var paste = session.PasteSpecialClipboardAtActiveCell(
            clipboardText,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats));

        paste.Success.Should().BeTrue();

        // Move semantics preserved: the moved formula's own reference is untouched.
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(d5)!.FormulaText.Should().Be("A1");

        // Number format comes from the moved (source) cell, but every other style attribute (e.g.
        // Italic) stays the destination's own pre-paste formatting.
        var finalStyle = workbook.GetStyle(sheet.GetCell(d5)!.StyleId);
        finalStyle.NumberFormat.Should().Be("0.00");
        finalStyle.Italic.Should().BeTrue();
        finalStyle.Bold.Should().BeFalse();
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
