using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R36-commands-paste-special-4-1/4-2/4-3: three Paste Special content kinds never tiled their
/// copied content across a destination selection that is a whole multiple of the copied source
/// range, unlike Values/Formulas/Formats/All (which repeat the source across the whole selected
/// destination -- the classic single-cell/range-to-larger-range fill gesture):
///
/// (1) Comments/Validation: PasteCommentsCommand/PasteDataValidationCommand already had a
///     GridRange-destinationRange tiling overload (added for R34-commands-paste-special-3-2), but
///     WorkbookSession.PasteCommentsFromClipboardAtActiveCell/PasteDataValidationFromClipboardAtActiveCell
///     never called it -- they always passed a single-cell CellAddress, so only the destination's
///     top-left cell/footprint ever got the pasted comment/rule.
/// (2) Paste Link: PasteLinkService.CreateLinkedCells only ever accepted a single anchor
///     CellAddress and emitted exactly one copy of the source's linked-formula footprint.
/// (3) Column widths: PasteColumnWidthsCommand only ever took a single destinationStartCol anchor
///     and fixed its own footprint width to the copied source's column count, regardless of how
///     wide the actual destination selection was.
///
/// These tests exercise the fix through the actual WorkbookSession paste-special entry points
/// (the layer where the bug lived) with a 2x2 source tiled onto a 4x4 destination (comments,
/// validation, paste link) and a 2-column source tiled onto 4 destination columns (column
/// widths), plus a sibling case per finding proving the original single-anchor (non-tiled)
/// behavior is unchanged when the destination selection is no larger than the copied source.
/// </summary>
public sealed class R36_PasteSpecialTileWiringTests
{
    [Fact]
    public void PasteCommentsFromClipboardAtActiveCell_Tiles2x2SourceOnto4x4Destination()
    {
        var (session, sheet) = CreateSessionWithCommentedSourceBlock();

        // Select D1:G4 (4x4 -- an exact 2x multiple of the 2x2 source in both dimensions) and
        // Paste Special > Comments.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var g4 = new CellAddress(sheet.Id, 4, 7);
        session.SelectRange(new GridRange(d1, g4));

        var result = session.PasteCommentsFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Real Excel tiles the copied 2x2 block of comments to fill the whole 4x4 selection.
        AssertTiledComments(sheet);

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 4, 7)).Should().BeFalse();
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 3, 6)).Should().BeFalse();
    }

    /// <summary>
    /// Regression guard: a destination selection no larger than the copied source (a single
    /// active cell) still pastes just the source's own 2x2 footprint anchored there, exactly as
    /// before the fix.
    /// </summary>
    [Fact]
    public void PasteCommentsFromClipboardAtActiveCell_NonTiledDestination_StillPastesOnlySourceFootprint()
    {
        var (session, sheet) = CreateSessionWithCommentedSourceBlock();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        session.SelectCell(d1);

        var result = session.PasteCommentsFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.Comments[new CellAddress(sheet.Id, 1, 4)].Should().Be("TL"); // D1
        sheet.Comments[new CellAddress(sheet.Id, 2, 4)].Should().Be("BL"); // D2
        sheet.Comments[new CellAddress(sheet.Id, 1, 5)].Should().Be("TR"); // E1
        sheet.Comments[new CellAddress(sheet.Id, 2, 5)].Should().Be("BR"); // E2
        // Nothing beyond the source's own 2x2 footprint was touched.
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 1, 6)).Should().BeFalse(); // F1
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 3, 4)).Should().BeFalse(); // D3
    }

    [Fact]
    public void PasteDataValidationFromClipboardAtActiveCell_Tiles2x2SourceOnto4x4Destination()
    {
        var (session, sheet) = CreateSessionWithValidatedSourceBlock();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var g4 = new CellAddress(sheet.Id, 4, 7);
        session.SelectRange(new GridRange(d1, g4));

        var result = session.PasteDataValidationFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Real Excel tiles the copied 2x2 rule footprint into every 2x2 quadrant of the 4x4
        // selection: one pasted rule per quadrant (plus the original source rule).
        sheet.DataValidations.Should().HaveCount(5);
        AssertQuadrantRule(sheet, new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 5)); // D1:E2
        AssertQuadrantRule(sheet, new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 2, 7)); // F1:G2
        AssertQuadrantRule(sheet, new CellAddress(sheet.Id, 3, 4), new CellAddress(sheet.Id, 4, 5)); // D3:E4
        AssertQuadrantRule(sheet, new CellAddress(sheet.Id, 3, 6), new CellAddress(sheet.Id, 4, 7)); // F3:G4

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        sheet.DataValidations.Should().HaveCount(1);
    }

    /// <summary>
    /// Regression guard: a destination selection no larger than the copied source still pastes
    /// just one rule at the anchor, exactly as before the fix.
    /// </summary>
    [Fact]
    public void PasteDataValidationFromClipboardAtActiveCell_NonTiledDestination_StillPastesSingleRule()
    {
        var (session, sheet) = CreateSessionWithValidatedSourceBlock();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        session.SelectCell(d1);

        var result = session.PasteDataValidationFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.DataValidations.Should().HaveCount(2); // original + one pasted 2x2 rule, no tiling
        AssertQuadrantRule(sheet, new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 5)); // D1:E2
    }

    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_Tiles2x2SourceOnto4x4Destination()
    {
        var (session, sheet) = CreateSessionWithLinkSourceBlock();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var g4 = new CellAddress(sheet.Id, 4, 7);
        session.SelectRange(new GridRange(d1, g4));

        var result = session.PasteLinkFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Real Excel repeats the linked formulas to fill the whole 4x4 selection: each tile's
        // cells link back to the SAME corresponding source cell (A1/A2/B1/B2), not new ones.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.FormulaText.Should().Be("Sheet1!A1"); // D1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 4))!.FormulaText.Should().Be("Sheet1!A2"); // D2
        sheet.GetCell(new CellAddress(sheet.Id, 1, 5))!.FormulaText.Should().Be("Sheet1!B1"); // E1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 5))!.FormulaText.Should().Be("Sheet1!B2"); // E2
        sheet.GetCell(new CellAddress(sheet.Id, 1, 6))!.FormulaText.Should().Be("Sheet1!A1"); // F1 (tile 0,1)
        sheet.GetCell(new CellAddress(sheet.Id, 3, 4))!.FormulaText.Should().Be("Sheet1!A1"); // D3 (tile 1,0)
        sheet.GetCell(new CellAddress(sheet.Id, 3, 6))!.FormulaText.Should().Be("Sheet1!A1"); // F3 (tile 1,1)
        sheet.GetCell(new CellAddress(sheet.Id, 4, 7))!.FormulaText.Should().Be("Sheet1!B2"); // G4 (tile 1,1)

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        sheet.GetCell(new CellAddress(sheet.Id, 4, 7)).Should().BeNull();
    }

    /// <summary>
    /// Regression guard: a destination selection no larger than the copied source still writes
    /// just the source's own linked footprint anchored there, exactly as before the fix.
    /// </summary>
    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_NonTiledDestination_StillWritesOnlySourceFootprint()
    {
        var (session, sheet) = CreateSessionWithLinkSourceBlock();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        session.SelectCell(d1);

        var result = session.PasteLinkFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.FormulaText.Should().Be("Sheet1!A1"); // D1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 4))!.FormulaText.Should().Be("Sheet1!A2"); // D2
        sheet.GetCell(new CellAddress(sheet.Id, 1, 5))!.FormulaText.Should().Be("Sheet1!B1"); // E1
        sheet.GetCell(new CellAddress(sheet.Id, 2, 5))!.FormulaText.Should().Be("Sheet1!B2"); // E2
        sheet.GetCell(new CellAddress(sheet.Id, 1, 6)).Should().BeNull(); // F1 untouched
        sheet.GetCell(new CellAddress(sheet.Id, 3, 4)).Should().BeNull(); // D3 untouched
    }

    [Fact]
    public void PasteColumnWidthsFromClipboardAtActiveCell_Tiles2ColSourceOnto4Cols()
    {
        var (session, sheet) = CreateSessionWithColumnWidthSource();
        // Select D1:G1 -- a 4-column destination, an exact 2x multiple of the 2-column source.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var g1 = new CellAddress(sheet.Id, 1, 7);
        session.SelectRange(new GridRange(d1, g1));

        var result = session.PasteColumnWidthsFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Real Excel tiles the copied column widths to fill all 4 destination columns:
        // D=20,E=40,F=20,G=40.
        sheet.ColumnWidths[4].Should().Be(20); // D
        sheet.ColumnWidths[5].Should().Be(40); // E
        sheet.ColumnWidths[6].Should().Be(20); // F
        sheet.ColumnWidths[7].Should().Be(40); // G

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        sheet.ColumnWidths.ContainsKey(6).Should().BeFalse();
        sheet.ColumnWidths.ContainsKey(7).Should().BeFalse();
    }

    /// <summary>
    /// Regression guard: a destination selection no wider than the copied source still pastes
    /// just the source's own 2-column footprint anchored there, exactly as before the fix.
    /// </summary>
    [Fact]
    public void PasteColumnWidthsFromClipboardAtActiveCell_NonTiledDestination_StillPastesOnlySourceFootprint()
    {
        var (session, sheet) = CreateSessionWithColumnWidthSource();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        session.SelectCell(d1);

        var result = session.PasteColumnWidthsFromClipboardAtActiveCell(_lastClipboardText!);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.ColumnWidths[4].Should().Be(20); // D
        sheet.ColumnWidths[5].Should().Be(40); // E
        sheet.ColumnWidths.ContainsKey(6).Should().BeFalse(); // F untouched
        sheet.ColumnWidths.ContainsKey(7).Should().BeFalse(); // G untouched
    }

    private static void AssertTiledComments(Sheet sheet)
    {
        // Tile (0,0): D1:E2
        sheet.Comments[new CellAddress(sheet.Id, 1, 4)].Should().Be("TL"); // D1
        sheet.Comments[new CellAddress(sheet.Id, 2, 4)].Should().Be("BL"); // D2
        sheet.Comments[new CellAddress(sheet.Id, 1, 5)].Should().Be("TR"); // E1
        sheet.Comments[new CellAddress(sheet.Id, 2, 5)].Should().Be("BR"); // E2
        // Tile (0,1): F1:G2
        sheet.Comments[new CellAddress(sheet.Id, 1, 6)].Should().Be("TL"); // F1
        sheet.Comments[new CellAddress(sheet.Id, 2, 7)].Should().Be("BR"); // G2
        // Tile (1,0): D3:E4
        sheet.Comments[new CellAddress(sheet.Id, 3, 4)].Should().Be("TL"); // D3
        sheet.Comments[new CellAddress(sheet.Id, 4, 5)].Should().Be("BR"); // E4
        // Tile (1,1): F3:G4
        sheet.Comments[new CellAddress(sheet.Id, 3, 6)].Should().Be("TL"); // F3
        sheet.Comments[new CellAddress(sheet.Id, 4, 7)].Should().Be("BR"); // G4
    }

    private static void AssertQuadrantRule(Sheet sheet, CellAddress start, CellAddress end)
    {
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(start, end) &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "5");
    }

    private string? _lastClipboardText;

    private (WorkbookSession Session, Sheet Sheet) CreateSessionWithCommentedSourceBlock()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new TextValue("a1"));
        sheet.SetCell(a2, new TextValue("a2"));
        sheet.SetCell(b1, new TextValue("b1"));
        sheet.SetCell(b2, new TextValue("b2"));
        sheet.Comments[a1] = "TL";
        sheet.Comments[a2] = "BL";
        sheet.Comments[b1] = "TR";
        sheet.Comments[b2] = "BR";

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));
        _lastClipboardText = session.CopySelectedRangeText();
        return (session, sheet);
    }

    private (WorkbookSession Session, Sheet Sheet) CreateSessionWithValidatedSourceBlock()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, b2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "5",
            Formula2 = "5"
        });

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));
        _lastClipboardText = session.CopySelectedRangeText();
        return (session, sheet);
    }

    private (WorkbookSession Session, Sheet Sheet) CreateSessionWithLinkSourceBlock()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(b1, new NumberValue(3));
        sheet.SetCell(b2, new NumberValue(4));

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));
        _lastClipboardText = session.CopySelectedRangeText();
        return (session, sheet);
    }

    private (WorkbookSession Session, Sheet Sheet) CreateSessionWithColumnWidthSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.ColumnWidths[1] = 20; // A
        sheet.ColumnWidths[2] = 40; // B
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b1));
        _lastClipboardText = session.CopySelectedRangeText();
        return (session, sheet);
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
