using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for round-154 remediation finding M3
/// (src/FreeX.App.Host/MainWindow.Editing.cs, FormulaBar_KeyDown): the window-level Escape guard
/// added for shared-keyboard-customization F1
/// (ShouldHandleEscapeLocallyBeforeFormulaPointMode in MainWindow.Selection.cs, applied to
/// MainWindow_KeyDown) was not mirrored onto FormulaBar_KeyDown -- the identical,
/// formula-bar-focused entry point that calls the same unguarded TryRouteFormulaPointModeKey.
/// FormulaBar is wired via PreviewKeyDown (see MainWindow.xaml), so it tunnels and handles the
/// key before MainWindow_KeyDown's bubble-phase handler ever runs -- the window-level fix alone
/// never reaches this case. A workbook window ("source") with its OWN F8 sticky-selection mode
/// armed must claim Escape locally instead of letting it fall through to
/// TryRouteFormulaPointModeKey, which -- when source has no formula edit of its own -- routes the
/// Cancel to whichever OTHER open workbook window ("owner") has a live formula point-mode edit and
/// silently discards it. See R90_CrossWorkbookFormulaPointModeWpfTests for the deliberate
/// cross-window routing contract this fix must NOT disturb.
/// </summary>
public sealed class R154_M3_FormulaBarEscapeLocalBeforeFormulaPointModeTests
{
    [Fact]
    public void Escape_WithF8StickySelectionArmedInOtherWindow_ClaimsEscapeLocally_LeavesOtherWindowsFormulaEditIntact()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = TwoWindowFormulaBarEscapeHarness.Create();
            harness.OwnerBeginFormulaPointModeEdit("=SUM(");
            harness.OwnerHasActiveFormulaPointMode.Should().BeTrue(
                "the failure scenario requires window A's formula edit to be live before Escape");

            harness.SourceArmF8ExtendMode();
            harness.SourceSelectionMode.Should().Be(ExcelSelectionMode.Extend,
                "the failure scenario requires window B's OWN F8 sticky-selection mode to be armed before Escape");

            harness.SourcePressEscapeInFormulaBar();

            harness.OwnerHasActiveFormulaPointMode.Should().BeTrue(
                "window A's in-progress formula edit must survive an Escape that window B's own F8 mode claimed locally");
            harness.OwnerFormulaBoxText.Should().Be("=SUM(",
                "window A's formula text must not be reverted by an Escape meant for window B's own local state");
        });
    }

    [Fact]
    public void Escape_WithNoLocalUiStateInOtherWindow_StillRoutesAndCancelsOtherWindowsFormulaEdit()
    {
        // Sibling/no-regression guard: when window B has no local UI state of its own claiming
        // Escape (F8 sticky-selection mode is Normal), the ordinary cross-window point-mode Cancel
        // routing exercised by R90_CrossWorkbookFormulaPointModeWpfTests must still fire exactly
        // as before -- this is the legitimate "pointed into another open workbook, then cancelled"
        // gesture and must not be disabled by the fix above.
        StaTestRunner.Run(() =>
        {
            using var harness = TwoWindowFormulaBarEscapeHarness.Create();
            harness.OwnerBeginFormulaPointModeEdit("=SUM(");
            harness.SourceSelectionMode.Should().Be(ExcelSelectionMode.Normal);

            harness.SourcePressEscapeInFormulaBar();

            harness.OwnerHasActiveFormulaPointMode.Should().BeFalse(
                "with no local UI state active in window B, Escape must still route to and cancel window A's formula edit");
        });
    }

    private sealed class TwoWindowFormulaBarEscapeHarness : IDisposable
    {
        private readonly MainWindow _owner;
        private readonly MainWindow _source;
        private readonly MethodInfo _sourceFormulaBarKeyDown;
        private readonly MethodInfo _setSelectionMode;
        private readonly FieldInfo _selectionModeField;
        private readonly CellAddress _formulaCell;

        private TwoWindowFormulaBarEscapeHarness(MainWindow owner, MainWindow source, CellAddress formulaCell)
        {
            _owner = owner;
            _source = source;
            _formulaCell = formulaCell;
            _sourceFormulaBarKeyDown = typeof(MainWindow)
                .GetMethod("FormulaBar_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBar_KeyDown");
            _setSelectionMode = typeof(MainWindow)
                .GetMethod("SetSelectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetSelectionMode");
            _selectionModeField = typeof(MainWindow)
                .GetField("_selectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionMode");
        }

        public static TwoWindowFormulaBarEscapeHarness Create()
        {
            var ownerWorkbook = NewWorkbook("Owner.xlsx", "Owner");
            var sourceWorkbook = NewWorkbook("Source.xlsx", "Input Data");
            var registry = new WorkbookWindowRegistry();
            var owner = CreateWindow(ownerWorkbook, registry);
            var source = CreateWindow(sourceWorkbook, registry);

            owner.Show();
            owner.Activate();
            source.Show();
            source.Activate();
            PumpDispatcher();
            owner.AdoptWorkbookForParityCapture(ownerWorkbook);
            source.AdoptWorkbookForParityCapture(sourceWorkbook);
            PumpDispatcher();

            var formulaCell = new CellAddress(ownerWorkbook.GetSheetAt(0).Id, 8, 7);
            return new TwoWindowFormulaBarEscapeHarness(owner, source, formulaCell);
        }

        public void OwnerBeginFormulaPointModeEdit(string formulaText)
        {
            _owner.BeginFormulaPointModeEditForTest(_formulaCell, formulaText);
            PumpDispatcher();
        }

        /// <summary>Arms window B's own F8 (Extend Selection) sticky-selection mode directly, the
        /// same state transition ExcelSelectionModePlanner.TryToggle drives from a real F8
        /// keypress in MainWindow.Selection.cs.</summary>
        public void SourceArmF8ExtendMode()
        {
            _setSelectionMode.Invoke(_source, [ExcelSelectionMode.Extend]);
            PumpDispatcher();
        }

        public ExcelSelectionMode SourceSelectionMode => (ExcelSelectionMode)_selectionModeField.GetValue(_source)!;

        public bool OwnerHasActiveFormulaPointMode => _owner.HasActiveFormulaPointMode;

        public string OwnerFormulaBoxText => _owner.FormulaBoxTextForTest;

        /// <summary>Invokes FormulaBar_KeyDown directly on window B's formula bar -- the same
        /// entry point a real Escape keypress reaches via the FormulaBar's PreviewKeyDown wiring
        /// (see MainWindow.xaml) -- avoiding any dependency on which control actually has WPF
        /// focus in the headless test host.</summary>
        public void SourcePressEscapeInFormulaBar()
        {
            var presentationSource = PresentationSource.FromVisual(_source)
                ?? throw new InvalidOperationException("Source window presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            _sourceFormulaBarKeyDown.Invoke(_source, [_source, args]);
            PumpDispatcher();
        }

        private static Workbook NewWorkbook(string name, string sheetName)
        {
            var workbook = new Workbook(name);
            workbook.AddSheet(sheetName);
            return workbook;
        }

        private static MainWindow CreateWindow(Workbook workbook, WorkbookWindowRegistry registry)
        {
            var workbookRef = new WorkbookRef { Current = workbook };
            return new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                windowRegistry: registry);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_source);
            MainWindowTestCleanup.CloseWithoutSavePrompt(_owner);
            PumpDispatcher();
        }
    }
}
