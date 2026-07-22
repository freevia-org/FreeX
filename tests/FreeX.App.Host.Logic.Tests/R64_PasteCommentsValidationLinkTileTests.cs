using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R64-commands-paste-special-6-1/6-2
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecutePasteComments/ExecutePasteValidation/
/// ExecutePasteLink).
///
/// Before the fix:
/// (1) ExecutePasteComments/ExecutePasteValidation always built PasteCommentsCommand/
///     PasteDataValidationCommand with the single-CellAddress (non-tiling) constructor overload,
///     anchored only at the destination selection's top-left cell -- discarding the rest of a
///     larger destination selection. Copying a 2x2 comment/validation block and pasting into a 4x4
///     selection only filled the top-left quadrant.
/// (2) ExecutePasteLink's CreatePasteLinkCommand called the 4-arg
///     PasteLinkService.CreateLinkedCells overload (which forwards destinationRange: null), so
///     Paste Link never tiled across a larger destination selection either.
///
/// The Avalonia shell's WorkbookSession.PasteCommentsFromClipboardAtActiveCell/
/// PasteDataValidationFromClipboardAtActiveCell/PasteLinkFromClipboardAtActiveCell already computed
/// the full destination range and used the GridRange-tiling overloads (fixed for
/// R34-commands-paste-special-3-2 / R36-commands-paste-special-4-1/4-2) -- this WPF host path had
/// not been brought into parity.
///
/// After the fix, all three WPF host paths pass the full selected destination GridRange (remapped
/// per grouped sheet) to the tiling overloads, so a 2x2 copied source tiles across a 4x4
/// destination selection exactly like Values/Formulas/Formats/All paste already does.
/// </summary>
public sealed class R64_PasteCommentsValidationLinkTileTests
{
    [Fact]
    public void ExecutePasteComments_2x2SourceOnto4x4Destination_TilesAcrossWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.Comments[new CellAddress(sheetId, 1, 1)] = "TL"; // A1
                sheet.Comments[new CellAddress(sheetId, 2, 1)] = "BL"; // A2
                sheet.Comments[new CellAddress(sheetId, 1, 2)] = "TR"; // B1
                sheet.Comments[new CellAddress(sheetId, 2, 2)] = "BR"; // B2

                Select(window, sheetId, 1, 1, 2, 2); // A1:B2
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 7, 7); // D4:G7 -- exact 2x multiple of the 2x2 source
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteComments", false);

                sheet.Comments[new CellAddress(sheetId, 4, 4)].Should().Be("TL"); // D4
                sheet.Comments[new CellAddress(sheetId, 5, 4)].Should().Be("BL"); // D5
                sheet.Comments[new CellAddress(sheetId, 4, 5)].Should().Be("TR"); // E4
                sheet.Comments[new CellAddress(sheetId, 5, 5)].Should().Be("BR"); // E5
                sheet.Comments[new CellAddress(sheetId, 4, 6)].Should().Be("TL"); // F4 (tile 0,1)
                sheet.Comments[new CellAddress(sheetId, 6, 4)].Should().Be("TL"); // D6 (tile 1,0)
                sheet.Comments[new CellAddress(sheetId, 6, 6)].Should().Be("TL"); // F6 (tile 1,1)
                sheet.Comments[new CellAddress(sheetId, 7, 7)].Should().Be("BR"); // G7 (tile 1,1)
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Regression guard: a destination selection the same size as the copied source still pastes
    /// just its own footprint anchored there, exactly as before the fix.
    /// </summary>
    [Fact]
    public void ExecutePasteComments_SameSizeDestination_StillPastesOnlySourceFootprint()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.Comments[new CellAddress(sheetId, 1, 1)] = "TL";
                sheet.Comments[new CellAddress(sheetId, 2, 1)] = "BL";
                sheet.Comments[new CellAddress(sheetId, 1, 2)] = "TR";
                sheet.Comments[new CellAddress(sheetId, 2, 2)] = "BR";

                Select(window, sheetId, 1, 1, 2, 2);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 5, 5); // D4:E5 -- same size, no tiling
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteComments", false);

                sheet.Comments[new CellAddress(sheetId, 4, 4)].Should().Be("TL");
                sheet.Comments[new CellAddress(sheetId, 5, 4)].Should().Be("BL");
                sheet.Comments[new CellAddress(sheetId, 4, 5)].Should().Be("TR");
                sheet.Comments[new CellAddress(sheetId, 5, 5)].Should().Be("BR");
                sheet.Comments.ContainsKey(new CellAddress(sheetId, 4, 6)).Should().BeFalse();
                sheet.Comments.ContainsKey(new CellAddress(sheetId, 6, 4)).Should().BeFalse();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ExecutePasteValidation_2x2SourceOnto4x4Destination_TilesAcrossWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var a1 = new CellAddress(sheetId, 1, 1);
                var b2 = new CellAddress(sheetId, 2, 2);
                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = new GridRange(a1, b2),
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "5",
                    Formula2 = "5"
                });

                Select(window, sheetId, 1, 1, 2, 2);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 7, 7);
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteValidation", false);

                sheet.DataValidations.Should().HaveCount(5); // original + 4 tiled quadrants
                AssertQuadrantRule(sheet, new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 5, 5));
                AssertQuadrantRule(sheet, new CellAddress(sheetId, 4, 6), new CellAddress(sheetId, 5, 7));
                AssertQuadrantRule(sheet, new CellAddress(sheetId, 6, 4), new CellAddress(sheetId, 7, 5));
                AssertQuadrantRule(sheet, new CellAddress(sheetId, 6, 6), new CellAddress(sheetId, 7, 7));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Regression guard: a destination selection the same size as the copied source still pastes
    /// just a single rule, exactly as before the fix.
    /// </summary>
    [Fact]
    public void ExecutePasteValidation_SameSizeDestination_StillPastesSingleRule()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var a1 = new CellAddress(sheetId, 1, 1);
                var b2 = new CellAddress(sheetId, 2, 2);
                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = new GridRange(a1, b2),
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "5",
                    Formula2 = "5"
                });

                Select(window, sheetId, 1, 1, 2, 2);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 5, 5); // same size, no tiling
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteValidation", false);

                sheet.DataValidations.Should().HaveCount(2); // original + one pasted rule
                AssertQuadrantRule(sheet, new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 5, 5));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ExecutePasteLink_2x2SourceOnto4x4Destination_TilesLinkedFormulasAcrossWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var sheetName = sheet.Name;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1)); // A1
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(2)); // A2
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new NumberValue(3)); // B1
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(4)); // B2

                Select(window, sheetId, 1, 1, 2, 2);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 7, 7);
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteLink", false, false);

                // Real Excel repeats the linked formulas to fill the whole 4x4 selection: each
                // tile's cells link back to the SAME corresponding source cell (A1/A2/B1/B2), not
                // new ones.
                sheet.GetCell(new CellAddress(sheetId, 4, 4))!.FormulaText.Should().Be($"{sheetName}!A1"); // D4
                sheet.GetCell(new CellAddress(sheetId, 5, 4))!.FormulaText.Should().Be($"{sheetName}!A2"); // D5
                sheet.GetCell(new CellAddress(sheetId, 4, 5))!.FormulaText.Should().Be($"{sheetName}!B1"); // E4
                sheet.GetCell(new CellAddress(sheetId, 5, 5))!.FormulaText.Should().Be($"{sheetName}!B2"); // E5
                sheet.GetCell(new CellAddress(sheetId, 4, 6))!.FormulaText.Should().Be($"{sheetName}!A1"); // F4 (tile 0,1)
                sheet.GetCell(new CellAddress(sheetId, 6, 4))!.FormulaText.Should().Be($"{sheetName}!A1"); // D6 (tile 1,0)
                sheet.GetCell(new CellAddress(sheetId, 7, 7))!.FormulaText.Should().Be($"{sheetName}!B2"); // G7 (tile 1,1)
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Regression guard: a destination selection the same size as the copied source still writes
    /// just the source's own linked footprint anchored there, exactly as before the fix.
    /// </summary>
    [Fact]
    public void ExecutePasteLink_SameSizeDestination_StillWritesOnlySourceFootprint()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var sheetName = sheet.Name;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(2));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new NumberValue(3));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(4));

                Select(window, sheetId, 1, 1, 2, 2);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                Select(window, sheetId, 4, 4, 5, 5); // same size as source, no tiling
                R49MainWindowTestHarness.Invoke(window, "ExecutePasteLink", false, false);

                sheet.GetCell(new CellAddress(sheetId, 4, 4))!.FormulaText.Should().Be($"{sheetName}!A1");
                sheet.GetCell(new CellAddress(sheetId, 5, 4))!.FormulaText.Should().Be($"{sheetName}!A2");
                sheet.GetCell(new CellAddress(sheetId, 4, 5))!.FormulaText.Should().Be($"{sheetName}!B1");
                sheet.GetCell(new CellAddress(sheetId, 5, 5))!.FormulaText.Should().Be($"{sheetName}!B2");
                sheet.GetCell(new CellAddress(sheetId, 4, 6)).Should().BeNull(); // F4 untouched
                sheet.GetCell(new CellAddress(sheetId, 6, 4)).Should().BeNull(); // D6 untouched
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void Select(
        MainWindow window,
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol)
    {
        window.SheetGrid.SelectedRange = new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    private static void AssertQuadrantRule(Sheet sheet, CellAddress start, CellAddress end)
    {
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(start, end) &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "5");
    }
}
