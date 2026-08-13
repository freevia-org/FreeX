using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED10 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED10Tests
{
    /// <summary>
    /// P99: cloning a page for a multi-sheet PDF export (MainWindow.CloneExportPage) must carry
    /// over the cell-destination overlays produced by PrintRenderer, not just text/link overlays —
    /// otherwise an internal "Place in this document" hyperlink whose target sheet is exported in
    /// the same multi-sheet PDF loses its destination anchor and PdfDocumentExporter drops the
    /// clickable annotation entirely.
    /// </summary>
    [Fact]
    public void CloneExportPage_PreservesCellDestinationOverlaysForInternalHyperlinkTarget()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Internal hyperlink export");
            var sheet = workbook.AddSheet("Sheet1");
            var sourceAddress = new CellAddress(sheet.Id, 1, 1);
            var targetAddress = new CellAddress(sheet.Id, 5, 2);
            sheet.SetCell(sourceAddress, new TextValue("Jump"));
            sheet.SetCell(targetAddress, new TextValue("Target"));
            sheet.PrintArea = new GridRange(sourceAddress, targetAddress);
            sheet.Hyperlinks[sourceAddress] = "Sheet1!B5";
            sheet.HyperlinkMetadata[sourceAddress] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var originalPage = document.Pages[0];

            // Sanity check: the un-cloned page produced by PrintRenderer really does carry the
            // destination overlay for the target cell (proves the fixture is meaningful).
            var originalRoot = originalPage.GetPageRoot(forceReload: false)!;
            PdfCellDestinationOverlayExtractor.Extract(originalRoot)
                .Should().ContainSingle(overlay => overlay.Address == targetAddress);

            var cloneMethod = typeof(MainWindow).GetMethod(
                "CloneExportPage",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("MainWindow.CloneExportPage not found.");
            var clonedPageContent = (PageContent)cloneMethod.Invoke(null, [document, originalPage])!;
            var clonedPage = clonedPageContent.Child
                ?? throw new InvalidOperationException("Cloned PageContent had no FixedPage child.");

            var cellDestinationOverlays = clonedPage.Children
                .OfType<VisualHost>()
                .SelectMany(host => host.CellDestinationOverlays)
                .ToList();

            cellDestinationOverlays.Should().ContainSingle(overlay => overlay.Address == targetAddress);
        });
    }

    /// <summary>
    /// P64: the WPF Find &amp; Replace dialog's default Look-in mode must be Formulas (matching
    /// both Excel's own default and the Avalonia shell's tabbed Find/Replace dialog), not Values —
    /// otherwise the same "Find All" action on the same workbook produces different match sets on
    /// Windows vs. Linux/macOS purely because of a mismatched default.
    /// </summary>
    [Fact]
    public void FindReplaceDialog_DefaultLookInMode_IsFormulas()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Look-in default");
            var sheet = workbook.AddSheet("Sheet1");
            var formulaCell = new CellAddress(sheet.Id, 1, 1);
            // A1: formula text contains "SUM" but the cached/displayed value ("5") does not, so
            // Values-mode and Formulas-mode disagree on whether this cell matches a "SUM" search.
            sheet.SetCell(formulaCell, Cell.FromFormula("=SUM(1,4)"));
            sheet.GetCell(formulaCell)!.Value = new NumberValue(5);

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: false,
                getCurrentSheetId: () => sheet.Id);
            dialog.Show();
            try
            {
                var lookInCombo = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "LookInCombo");

                // The dialog's own default (no user interaction) must be Formulas (index 0), so
                // searching "SUM" finds the formula-text match rather than only the displayed "5".
                lookInCombo.SelectedIndex.Should().Be(0);

                var findBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "FindBox");
                findBox.Text = "SUM";
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindAll_Click");

                var resultsGrid = DialogSourceTestSupport.GetPrivateField<DataGrid>(dialog, "FindResultsGrid");
                resultsGrid.Items.Count.Should().Be(1, "the default Look-in mode must match formula text, not just the displayed value");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
