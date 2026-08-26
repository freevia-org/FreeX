using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Cleanup batch MED2 — round-10 MED findings in the WPF clipboard/paste-special code
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs).
/// </summary>
public sealed class FreeXCleanupMED2Tests
{
    // P43 (MED): a copied range that clips a merged region whose anchor lies outside the range
    // (e.g. copy A2:B3 when A1:A3 is merged) used to mark every covered cell for skipping without
    // ever emitting a spanning <td> for the clipped slot, shifting later cells in the row left in
    // the exported CF_HTML fragment. Fixed: a synthetic clipped anchor is emitted for the visible
    // portion of the region, so every row still renders one <td> per copied COLUMN (accounting for
    // colspan/rowspan), and B2/B3 stay in the second column slot instead of shifting into the first.
    [Fact]
    public void BuildHtmlClipboardFragment_MergeAnchorOutsideCopiedRange_KeepsColumnCountPerRow()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge A1:A3 (anchor A1 is OUTSIDE the copied range below).
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("B2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("B3"));

        // Copy A2:B3 — clips the A1:A3 merge to rows 2-3.
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2));

        var viewportService = new ViewportService();
        var viewport = viewportService.GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 2_000, 2_000));

        var method = typeof(MainWindow).GetMethod(
            "BuildHtmlClipboardFragment",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("BuildHtmlClipboardFragment should exist as a private static helper on MainWindow");

        var cfHtml = (string?)method!.Invoke(
            null,
            [viewport, sheet, range, workbook.Theme]);

        cfHtml.Should().NotBeNull();

        var fragmentStart = cfHtml!.IndexOf("<!--StartFragment-->", StringComparison.Ordinal);
        var fragmentEnd = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
        var fragment = cfHtml[(fragmentStart + "<!--StartFragment-->".Length)..fragmentEnd];

        var rowMatches = Regex.Matches(fragment, "<tr>(.*?)</tr>", RegexOptions.Singleline);
        rowMatches.Count.Should().Be(2, "the copied range has two rows (2 and 3)");

        // Row 2 (top of the clipped region): a synthetic spanning <td> (rowspan="2") fills the
        // clipped merge's column-1 slot, plus a plain <td> for B2 — two <td>s total, NOT one
        // (the pre-fix bug rendered only the B2 <td>, shifting it into column 1).
        var row2Cells = Regex.Matches(rowMatches[0].Groups[1].Value, "<td[^>]*>").Count;
        row2Cells.Should().Be(2, "row 2 must keep both the spanning merge cell and the B2 cell");
        rowMatches[0].Groups[1].Value.Should().Contain("rowspan=\"2\"");

        // Row 3: column 1 is covered by the row-2 spanning cell (rowspan), so only B3's <td> is
        // emitted here — this is correct because a rowspan already occupies that slot, unlike the
        // pre-fix bug where B2 in row 2 was ALSO left alone in column 1 with no covering rowspan.
        var row3Cells = Regex.Matches(rowMatches[1].Groups[1].Value, "<td[^>]*>").Count;
        row3Cells.Should().Be(1, "row 3's column 1 is already covered by row 2's rowspan");

        // B2 and B3 must both be present, and neither must have been dropped or merged into the
        // spanning placeholder cell's own (empty) content.
        fragment.Should().Contain("B2");
        fragment.Should().Contain("B3");
        Regex.Matches(fragment, "<td[^>]*rowspan=\"2\"[^>]*></td>").Count.Should().Be(
            1, "the synthetic spanning cell for the clipped merge must be empty, not swallow B2's text");
    }

    // P44 (MED): Paste Special > Text / Unicode Text right after an in-app copy used to perform a
    // full formatted internal paste instead of plain text, because the internal-clipboard branch
    // never consulted externalTextAsText (it only compared clipboard text-equality, which is always
    // true immediately after a same-app copy).
    [Fact]
    public void PasteSpecialAsText_AfterInternalCopy_PastesPlainTextNotFormula()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                recalcEngine,
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: new InMemoryPlatformClipboard());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b1 = new CellAddress(sheet.Id, 1, 2);
                var d1 = new CellAddress(sheet.Id, 1, 4);
                sheet.SetCell(a1, new NumberValue(5));
                sheet.SetFormula(b1, "A1*2");
                // The copy step below serializes the copied cell's cached DisplayText (which is
                // derived from Cell.Value, not live-evaluated) — recalculate once up front so B1
                // actually holds its computed value/display text before it is copied, matching how
                // the real app recalculates on every edit.
                recalcEngine.RecalculateAllFormulas(workbook);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(b1, b1);

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(d1, d1);

                var executePaste = typeof(MainWindow).GetMethod(
                    "ExecutePaste",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                executePaste.Should().NotBeNull();

                // externalTextAsText: true == Paste Special > Text/Unicode Text.
                executePaste!.Invoke(
                    window,
                    [PasteMode.All, default(PasteSpecialOptions), false, true]);
                PumpDispatcher();

                var pastedCell = sheet.GetCell(d1);
                pastedCell.Should().NotBeNull();
                pastedCell!.FormulaText.Should().BeNull(
                    "Paste Special > Text must discard the copied cell's formula");
                pastedCell.Value.Should().Be(
                    new TextValue("10"),
                    "Paste Special > Text must paste the copied cell's plain display text, not re-run a formatted internal paste");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    // P46 (MED): the WPF host's external-clipboard Paste Special fallback (no FreeX-internal
    // clipboard) silently dropped Transpose/Skip Blanks/Operation because
    // PasteCommandFactory.CreateExternalTextPasteCommand had no PasteSpecialOptions parameter at
    // all. Verify pasting external TSV text with Transpose actually transposes.
    [Fact]
    public void ExternalTextPasteSpecial_WithTranspose_TransposesRows()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var destination = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "1", "2" },
            new List<string> { "3", "4" },
        };

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            destination,
            rows,
            preserveText: false,
            new PasteSpecialOptions(Transpose: true));

        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Un-transposed the 2x2 block would paste row-major (A1=1,B1=2,A2=3,B2=4); transposed it
        // must paste column-major (A1=1,B1=3,A2=2,B2=4).
        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.Value.Should().Be(new NumberValue(1));
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value.Should().Be(new NumberValue(3));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.Value.Should().Be(new NumberValue(4));
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
