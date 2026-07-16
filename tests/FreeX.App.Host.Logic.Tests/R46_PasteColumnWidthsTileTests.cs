using System.Reflection;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R46-commands-paste-special-ops-2-1
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecutePasteColumnWidthsOnly).
///
/// Before the fix: Paste Special "Column widths" (and the ribbon's "Keep Source Column Widths"
/// quick-paste option) always built <c>new PasteColumnWidthsCommand(sheetId, clip.SourceRange,
/// currentRange.Start.Col)</c> -- the 3-arg constructor, which always applies exactly one
/// untiled copy of the source range's own column footprint anchored at the destination's start
/// column. Real Excel (and FreeX's own Values/Formulas/All paste, and the Avalonia-only
/// WorkbookSession.PasteColumnWidthsFromClipboardAtActiveCell path) tile a narrower copied
/// selection across a wider destination selection. So pasting a single copied column's width
/// onto a 3-column destination selection only widened the first destination column; the other
/// two silently kept their old widths.
///
/// After the fix, the call passes <c>currentRange.ColCount</c> as a 4th argument, using
/// PasteColumnWidthsCommand's tiling overload (added in R36-commands-paste-special-4-3) so the
/// copied column width(s) repeat across the whole destination selection.
/// </summary>
public sealed class R46_PasteColumnWidthsTileTests
{
    [Fact]
    public void ExecutePasteColumnWidthsOnly_WiderDestination_TilesSourceWidthAcrossEveryColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = PasteColumnWidthsHarness.Create();

            // Source: column A, width 30.
            harness.Sheet.ColumnWidths[1] = 30.0;
            harness.SetSelection(1, 1, 3, 1); // A1:A3
            harness.Copy();

            // Destination: B1:D3 -- three columns, wider than the one-column copied source.
            harness.SetSelection(1, 2, 3, 4); // B1:D3
            harness.PasteColumnWidthsOnly();

            // Real Excel tiles the copied column's width across every destination column, the
            // same way Values/Formulas/All paste already tiles a narrower source across a wider
            // selection -- not just the first destination column.
            harness.Sheet.ColumnWidths.Should().ContainKey(2).WhoseValue.Should().Be(30.0, "column B is the first destination column");
            harness.Sheet.ColumnWidths.Should().ContainKey(3).WhoseValue.Should().Be(30.0, "column C must also receive the tiled source width");
            harness.Sheet.ColumnWidths.Should().ContainKey(4).WhoseValue.Should().Be(30.0, "column D must also receive the tiled source width");
        });
    }

    [Fact]
    public void ExecutePasteColumnWidthsOnly_SameWidthDestination_AppliesOnlyToThatColumn()
    {
        // Sibling/no-regression case: pasting onto a destination selection exactly as wide as the
        // copied source must still behave as a single, untiled application anchored at the
        // destination's start column -- confirming the tiling fix doesn't over-apply when there's
        // nothing to tile.
        StaTestRunner.Run(() =>
        {
            using var harness = PasteColumnWidthsHarness.Create();

            harness.Sheet.ColumnWidths[1] = 30.0; // source column A
            harness.Sheet.ColumnWidths[3] = 12.0; // unrelated column C must be left untouched
            harness.SetSelection(1, 1, 3, 1); // A1:A3
            harness.Copy();

            harness.SetSelection(1, 2, 3, 2); // B1:B3 -- single column, same width as source
            harness.PasteColumnWidthsOnly();

            harness.Sheet.ColumnWidths.Should().ContainKey(2).WhoseValue.Should().Be(30.0, "the single destination column receives the copied width");
            harness.Sheet.ColumnWidths.Should().ContainKey(3).WhoseValue.Should().Be(12.0, "an unselected neighboring column must not be touched");
        });
    }

    private sealed class PasteColumnWidthsHarness : IDisposable
    {
        private readonly MethodInfo _executeCopy;
        private readonly MethodInfo _executePasteColumnWidthsOnly;

        private PasteColumnWidthsHarness(MainWindow window, Workbook workbook)
        {
            Window = window;
            Workbook = workbook;
            _executeCopy = typeof(MainWindow)
                .GetMethod("ExecuteCopy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCopy");
            _executePasteColumnWidthsOnly = typeof(MainWindow)
                .GetMethod("ExecutePasteColumnWidthsOnly", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecutePasteColumnWidthsOnly");
        }

        public MainWindow Window { get; }

        public Workbook Workbook { get; }

        public Sheet Sheet => Workbook.GetSheetAt(0);

        public void SetSelection(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheetId = Sheet.Id;
            Window.SheetGrid.SelectedRange = new GridRange(
                new CellAddress(sheetId, startRow, startCol),
                new CellAddress(sheetId, endRow, endCol));
        }

        public void Copy()
        {
            _executeCopy.Invoke(Window, [false]);
        }

        public void PasteColumnWidthsOnly()
        {
            _executePasteColumnWidthsOnly.Invoke(Window, null);
            PumpDispatcher();
        }

        public static PasteColumnWidthsHarness Create()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (MainWindow.Startup.cs) replaces the constructor's workbook with a
            // brand-new default one unless adopting a shared document, so the live workbook is
            // whatever workbookRef.Current now points to (see R41_FreezePaneScrollPreservationTests).
            var workbook = workbookRef.Current;
            var sheet = workbook.GetSheetAt(0);
            for (uint row = 1; row <= 5; row++)
                for (uint col = 1; col <= 5; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 10 + col));

            return new PasteColumnWidthsHarness(window, workbook);
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// No-op <see cref="Free.Shared.AppServices.IUserMessageService"/> for tests that construct
    /// <see cref="MainWindow"/> directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
