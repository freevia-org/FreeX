using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for five round-60 findings in the host-print-find bucket:
/// R60-services-print-preview-6-1 (Entire Workbook print restarts &amp;P/&amp;N per sheet),
/// R60-services-print-preview-6-3 (Black and white not honored for grid cells),
/// R60-commands-find-replace-6-1 (WPF Find Next never starts from the active cell),
/// R60-commands-find-replace-6-3 (WPF "same search" detection ignores search options), and
/// R60-commands-freeze-split-6-2 (Split at active cell A1 is a no-op instead of a middle split).
/// </summary>
public sealed class R60_HostPrintFindFreezeSplitTests
{
    private const double ColumnWidth = 120.0;
    private const double RowHeight = 40.0;

    /// <summary>
    /// SourceTextTestSupport.GetPrivateField&lt;T&gt; requires a reference-typed T and an exact
    /// runtime-type match, which doesn't fit FindReplaceDialog's private `int _currentIndex` (a
    /// value type) or `IReadOnlyList&lt;FindResult&gt; _results` (whose runtime type is some
    /// concrete list, not the interface). Read both directly instead.
    /// </summary>
    private static object? GetPrivateFieldValue(object instance, string name)
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull();
        return field!.GetValue(instance);
    }

    private static int GetCurrentFindIndex(FindReplaceDialog dialog) =>
        (int)GetPrivateFieldValue(dialog, "_currentIndex")!;

    private static List<FindResult> GetFindResults(FindReplaceDialog dialog) =>
        ((System.Collections.IEnumerable)GetPrivateFieldValue(dialog, "_results")!)
            .Cast<FindResult>()
            .ToList();

    // ── R60-services-print-preview-6-1 ──────────────────────────────────────────────────────

    [Fact]
    public void RenderWorkbook_ContinuesPageNumberingAcrossSheets()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Continuous numbering");
            var sheet1 = workbook.AddSheet("Sheet1");
            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("Top"));
            sheet1.SetCell(new CellAddress(sheet1.Id, 10, 1), new TextValue("Bottom"));
            sheet1.RowPageBreaks.Add(6); // forces Sheet1 to exactly 2 pages
            sheet1.PageFooter = new WorksheetHeaderFooter("", "&P of &N", "");

            var sheet2 = workbook.AddSheet("Sheet2");
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Solo"));
            sheet2.PageFooter = new WorksheetHeaderFooter("", "&P of &N", "");

            var document = PrintRenderer.RenderWorkbook(workbook, new ViewportService());

            document.Pages.Should().HaveCount(3);
            var footerTexts = document.Pages
                .Select(page => PdfTextOverlayExtractor.Extract(page.GetPageRoot(forceReload: false)!)
                    .Select(overlay => overlay.Text)
                    .FirstOrDefault(text => text.Contains(" of ", StringComparison.Ordinal)))
                .ToList();

            // Pre-fix, &N restarted at each sheet's own page count and &P restarted at 1 per sheet,
            // so this would read ["1 of 2", "2 of 2", "1 of 1"] instead.
            footerTexts.Should().Equal(
                ["1 of 3", "2 of 3", "3 of 3"],
                "Excel's Entire Workbook print keeps &P/&N continuous across the whole print job instead of restarting at each sheet");
        });
    }

    [Fact]
    public void RenderWorksheet_SingleSheetPrint_StillUsesOnlyItsOwnPageCount()
    {
        // Sibling no-regression: printing ONE sheet (Print Preview's default "Active Sheet(s)"
        // mode, i.e. RenderWorksheet without a workbook-wide offset/total override) must keep
        // showing that sheet's own page count, not the whole workbook's.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Single sheet");
            var sheet1 = workbook.AddSheet("Sheet1");
            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("Top"));
            sheet1.SetCell(new CellAddress(sheet1.Id, 10, 1), new TextValue("Bottom"));
            sheet1.RowPageBreaks.Add(6);
            sheet1.PageFooter = new WorksheetHeaderFooter("", "&P of &N", "");
            workbook.AddSheet("Sheet2"); // present, but not part of this render

            var document = PrintRenderer.RenderWorksheet(workbook, sheet1.Id, new ViewportService());

            document.Pages.Should().HaveCount(2);
            var footerTexts = document.Pages
                .Select(page => PdfTextOverlayExtractor.Extract(page.GetPageRoot(forceReload: false)!)
                    .Select(overlay => overlay.Text)
                    .FirstOrDefault(text => text.Contains(" of ", StringComparison.Ordinal)))
                .ToList();

            footerTexts.Should().Equal(["1 of 2", "2 of 2"]);
        });
    }

    // ── R60-services-print-preview-6-3 ──────────────────────────────────────────────────────

    [Fact]
    public void DrawPrintedGridCells_BlackAndWhite_SuppressesCellFill()
    {
        StaTestRunner.Run(() =>
        {
            var style = new CellStyle { FillColor = new CellColor(255, 0, 0) };

            var colorPixels = RenderFilledCell(style, blackAndWhite: false);
            var bwPixels = RenderFilledCell(style, blackAndWhite: true);

            HasRedFillPixel(colorPixels).Should().BeTrue(
                "sanity check: without Black and white the authored red fill must render");
            // Pre-fix, DrawPrintedGridCells never received/consulted the blackAndWhite flag, so
            // this would still be red.
            HasRedFillPixel(bwPixels).Should().BeFalse(
                "Page Setup > Sheet > Black and white must suppress every cell fill (transparent/white), not draw the authored color");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_ColorMode_StillRendersAuthoredFill()
    {
        // Sibling no-regression: the (far more common) default color print path must keep
        // rendering authored fills exactly as before -- threading the new parameter through must
        // not accidentally suppress fills when blackAndWhite is false.
        StaTestRunner.Run(() =>
        {
            var style = new CellStyle { FillColor = new CellColor(255, 0, 0) };

            var colorPixels = RenderFilledCell(style, blackAndWhite: false);

            HasRedFillPixel(colorPixels).Should().BeTrue();
        });
    }

    // ── R60-commands-find-replace-6-1 ──────────────────────────────────────────────────────

    [Fact]
    public void WpfFindNext_StartsSearchForwardFromActiveCell_NotFirstSheetOrderMatch()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var firstMatch = new CellAddress(sheet.Id, 1, 1); // A1
                var secondMatch = new CellAddress(sheet.Id, 100, 26); // Z100
                sheet.SetCell(firstMatch, new TextValue("Apple"));
                sheet.SetCell(secondMatch, new TextValue("Apple"));

                var activeCell = new CellAddress(sheet.Id, 50, 26); // Z50 -- just before the 2nd match
                window.SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "FindBox").Text = "Apple";

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                    var currentIndex = GetCurrentFindIndex(dialog);
                    var results = GetFindResults(dialog);

                    // Pre-fix, FindNext always jumped to index 0 (sheet-order first match, A1) on the
                    // very first click regardless of the active cell. Post-fix, it must land on the
                    // match strictly after the active cell (Z100), matching Excel and the Avalonia shell.
                    results[currentIndex].Address.Should().Be(secondMatch,
                        "Excel's Find Next always searches forward from the active cell, wrapping at the end");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void WpfFindNext_NoActiveCell_StillFindsFirstSheetOrderMatch()
    {
        // Sibling no-regression: when there is no active-cell callback (or it returns null), Find
        // Next must still behave sanely and land on SOME match instead of throwing/no-oping.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var onlyMatch = new CellAddress(sheet.Id, 5, 1);
                sheet.SetCell(onlyMatch, new TextValue("Apple"));

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => null)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "FindBox").Text = "Apple";

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                    var currentIndex = GetCurrentFindIndex(dialog);
                    var results = GetFindResults(dialog);

                    results[currentIndex].Address.Should().Be(onlyMatch);
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R60-commands-find-replace-6-3 ──────────────────────────────────────────────────────

    [Fact]
    public void WpfFindNext_ChangingMatchEntireCellOption_RestartsFromActiveCell_NotStaleIndex()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                // Active cell (A1, row 1) is not itself a match -- every match sorts strictly after
                // it, so "restart from the active cell" always lands on the FIRST match of whichever
                // result set is current, isolating just the "did an option change trigger a restart"
                // behavior from find-6-1's separate active-cell-skip semantics.
                var activeCell = new CellAddress(sheet.Id, 1, 1);
                var b2 = new CellAddress(sheet.Id, 2, 1); // "Cat"
                var b3 = new CellAddress(sheet.Id, 3, 1); // "Category"
                var b4 = new CellAddress(sheet.Id, 4, 1); // "cat"
                sheet.SetCell(b2, new TextValue("Cat"));
                sheet.SetCell(b3, new TextValue("Category"));
                sheet.SetCell(b4, new TextValue("cat"));

                window.SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    var findBox = DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "FindBox");
                    findBox.Text = "cat";

                    // One Find Next click (Match Case/Entire off): matches [B2, B3, B4] (substring,
                    // case-insensitive) -> lands on B2, the first match after the active cell.
                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                    // Now toggle "Match entire cell contents" WITHOUT retyping the search text, and
                    // click Find Next again. The result set becomes [B2, B4] (only exact matches).
                    var matchEntireBox = DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.CheckBox>(dialog, "MatchEntireBox");
                    matchEntireBox.IsChecked = true;

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                    var currentIndex = GetCurrentFindIndex(dialog);
                    var results = GetFindResults(dialog);

                    // Pre-fix, the stale index (0, from the earlier 3-result search) was reused
                    // unchanged against this differently-filtered 2-result set, computing
                    // (0+1)%2 = 1 -> B4 -- silently skipping B2 even though the active cell never
                    // moved. Post-fix, the option change must be treated like a new search and
                    // restart forward from the active cell (A1), landing back on B2 (the first exact
                    // match after A1).
                    results[currentIndex].Address.Should().Be(b2,
                        "toggling Match Entire Cell Contents must restart the search from the active cell, not reuse an index computed against the previous (differently-filtered) result set");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void WpfFindNext_RepeatedClicksWithUnchangedOptions_StillAdvanceSequentially()
    {
        // Sibling no-regression: repeated Find Next clicks with NOTHING changed must keep advancing
        // through the result set in order (the pre-existing, already-correct behavior) instead of
        // restarting from the active cell on every click.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var activeCell = new CellAddress(sheet.Id, 1, 1);
                var b2 = new CellAddress(sheet.Id, 2, 1);
                var b3 = new CellAddress(sheet.Id, 3, 1);
                sheet.SetCell(b2, new TextValue("Cat"));
                sheet.SetCell(b3, new TextValue("Category"));

                window.SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "FindBox").Text = "cat";

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                    var firstIndex = GetCurrentFindIndex(dialog);

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                    var secondIndex = GetCurrentFindIndex(dialog);

                    var results = GetFindResults(dialog);

                    results[firstIndex].Address.Should().Be(b2);
                    results[secondIndex].Address.Should().Be(b3);
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R60-commands-freeze-split-6-2 ──────────────────────────────────────────────────────

    [Fact]
    public void SplitViewBtn_Click_AtActiveCellA1_SplitsAtViewportMidpoint_NotNoOp()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var a1 = new CellAddress(sheetId, 1, 1);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                window.SheetGrid.SelectedRange = new GridRange(a1, a1);

                // Capture the viewport actually in effect right before the click -- SplitViewBtn_Click
                // reads SheetGrid.Viewport as it stood BEFORE the split; UpdateViewport() (called at
                // the end of the same handler) then recomputes it for the new, now-split panes, which
                // is a different (smaller) viewport and must not be used to derive the expectation.
                var viewportBeforeSplit = window.SheetGrid.Viewport;
                viewportBeforeSplit.Should().NotBeNull();

                R49MainWindowTestHarness.Invoke(window, "SplitViewBtn_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);

                // Pre-fix, both SplitRow and SplitColumn stayed null (a complete no-op). Post-fix,
                // Excel's fallback splits the visible window at (roughly) its midpoint.
                (sheet.SplitRow is not null || sheet.SplitColumn is not null).Should().BeTrue(
                    "Excel's Split command is never a no-op, even at A1 -- it falls back to a middle-of-window split");

                if (viewportBeforeSplit!.RowMetrics.Count > 1)
                    sheet.SplitRow.Should().Be(viewportBeforeSplit.RowMetrics[viewportBeforeSplit.RowMetrics.Count / 2].Row);
                if (viewportBeforeSplit.ColMetrics.Count > 1)
                    sheet.SplitColumn.Should().Be(viewportBeforeSplit.ColMetrics[viewportBeforeSplit.ColMetrics.Count / 2].Col);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SplitViewBtn_Click_AwayFromA1_StillSplitsAtThatCell()
    {
        // Sibling no-regression: the already-correct "split at a real anchor cell" behavior
        // (R51-commands-freeze-split-view-3-2) must be unaffected by the new A1 fallback.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var cell = new CellAddress(sheetId, 6, 3); // C6

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", cell);

                R49MainWindowTestHarness.Invoke(window, "SplitViewBtn_Click", null!, null!);

                var sheet = workbook.GetSheetAt(0);
                sheet.SplitRow.Should().Be(6u);
                sheet.SplitColumn.Should().Be(3u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static byte[] RenderFilledCell(CellStyle style, bool blackAndWhite)
    {
        var cell = new DisplayCell(
            Row: 1,
            Col: 1,
            RawValue: new TextValue(""),
            DisplayText: "",
            Formula: null,
            StyleId: default,
            Error: null,
            Style: style);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cell,
        };

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = new uint[] { 1u };

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        var linkTargetType = typeof(PrintRenderer).GetNestedType("PdfLinkTarget", BindingFlags.NonPublic)!;
        var hyperlinkLookupType = typeof(Dictionary<,>).MakeGenericType(typeof(ValueTuple<uint, uint>), linkTargetType);
        var hyperlinkLookup = Activator.CreateInstance(hyperlinkLookupType)!;
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var width = (int)ColumnWidth;
        var height = (int)RowHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null,
            [
                dc,
                textOverlays,
                linkOverlays,
                cellDestinationOverlays,
                measurement,
                pageRows,
                pageColumns,
                cellLookup,
                hyperlinkLookup,
                cellDestinationLookup,
                false,
                WorksheetPrintErrorValue.Displayed,
                0.0,
                0.0,
                new Workbook(),
                blackAndWhite,
                null,
            ]);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static bool HasRedFillPixel(byte[] pixels)
    {
        const int width = (int)ColumnWidth;
        const int height = (int)RowHeight;
        var cx = width / 2;
        var cy = height / 2;
        var i = (cy * width + cx) * 4;
        var blue = pixels[i];
        var green = pixels[i + 1];
        var red = pixels[i + 2];
        var alpha = pixels[i + 3];
        return alpha > 0 && red > 150 && green < 100 && blue < 100;
    }
}
