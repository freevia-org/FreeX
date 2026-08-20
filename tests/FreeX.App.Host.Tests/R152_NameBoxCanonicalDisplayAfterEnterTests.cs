using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for freex-name-box-goto F2: on Enter, the WPF Name Box computes its post-
/// navigation display text (<c>navigationText</c>) via
/// <c>DefinedNameUiPolicy.ResolveNameBoxNavigationDisplayText</c> BEFORE parsing/navigating. That
/// policy only replaces the typed text when it case-insensitively matches an existing defined
/// name/table/object; for a plain cell/range reference that matches nothing, it returns the user's
/// raw trimmed input verbatim -- so typing a lowercase or non-canonical address navigates correctly
/// but leaves the Name Box showing the exact text the user typed instead of Excel's canonical form,
/// unlike every other selection-driven Name Box update in this shell (e.g. Escape's
/// RestoreCellAddressBoxText), which always re-derives the text from the model.
/// </summary>
public sealed class R152_NameBoxCanonicalDisplayAfterEnterTests
{
    [Fact]
    public void NameBoxEnter_WithLowercaseCellReference_DisplaysCanonicalUppercaseAddress()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();

            harness.SetCellAddressBoxText("b5");
            harness.PressEnter().Should().BeTrue();

            var sheet = harness.Workbook.GetSheetAt(0);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 5, 2),
                new CellAddress(sheet.Id, 5, 2)));
            harness.CellAddressBoxText.Should().Be(
                "B5",
                "the Name Box must show Excel's canonical cell reference after Enter, not the user's " +
                "raw typed casing");
        });
    }

    [Fact]
    public void NameBoxEnter_WithDollarAnchoredReference_DisplaysCanonicalAddressWithoutDollarSigns()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();

            harness.SetCellAddressBoxText("$b$5");
            harness.PressEnter().Should().BeTrue();

            harness.CellAddressBoxText.Should().Be(
                "B5",
                "the Name Box must display the plain canonical reference, not the '$'-anchored text " +
                "the user typed");
        });
    }

    // No-regression sibling #1: a defined name typed with different casing than it was created with
    // must still resolve to the name's own canonical casing (the pre-existing, intentional behaviour
    // ResolveNameBoxNavigationDisplayText already provides) rather than falling through to the new
    // cell-reference formatter.
    [Fact]
    public void NameBoxEnter_WithDefinedNameInDifferentCasing_StillDisplaysNamesCanonicalCasing()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var range = new GridRange(new CellAddress(sheet.Id, 9, 9), new CellAddress(sheet.Id, 9, 9));
            harness.Workbook.DefineNamedRange("TaxRate", range);

            harness.SetCellAddressBoxText("taxrate");
            harness.PressEnter().Should().BeTrue();

            harness.SelectedRange.Should().Be(range);
            harness.CellAddressBoxText.Should().Be(
                "TaxRate",
                "typing a defined name in different casing must still show the name's own canonical " +
                "casing, exactly as before this fix");
        });
    }

    // No-regression sibling #2: a structured table's name typed in its own exact canonical casing
    // must keep showing the table name (not get overwritten by the new cell-reference fallback,
    // which does not know about tables and would otherwise show the raw data-body range instead).
    [Fact]
    public void NameBoxEnter_WithStructuredTableNameInCanonicalCasing_StillDisplaysTableName()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            sheet.StructuredTables.Add(new StructuredTableModel
            {
                Id = 1,
                Name = "SalesTable",
                DisplayName = "SalesTable",
                Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            });

            harness.SetCellAddressBoxText("SalesTable");
            harness.PressEnter().Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 4, 2)));
            harness.CellAddressBoxText.Should().Be(
                "SalesTable",
                "a structured table's name typed in its own canonical casing must keep displaying the " +
                "table name, not the raw data-body range address");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MethodInfo _cellAddressBoxKeyDown;

        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            Window.Show();
            Window.Activate();
            Window.UpdateLayout();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook afterward so
            // the test operates on the same Workbook instance MainWindow's handlers use.
            Workbook = workbookRef.Current;

            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
        }

        private ComboBox CellAddressBox => (ComboBox)Window.FindName("CellAddressBox")!;

        public GridRange? SelectedRange => ((SheetGridView)Window.FindName("SheetGrid")!).SelectedRange;

        public string CellAddressBoxText => CellAddressBox.Text;

        public void SetCellAddressBoxText(string text)
        {
            CellAddressBox.Text = text;
            PumpDispatcher();
        }

        public bool PressEnter()
        {
            var source = PresentationSource.FromVisual(Window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _cellAddressBoxKeyDown.Invoke(Window, [CellAddressBox, args]);
            PumpDispatcher();
            return args.Handled;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
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
}
