using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for round-154 finding shared-keyboard-customization F1
/// (src/FreeX.App.Host/MainWindow.Selection.cs, MainWindow_KeyDown): Escape pressed in a workbook
/// window that has its OWN local UI state active (here: an open ribbon key-tip session) must
/// dismiss that local state instead of falling through to TryRouteFormulaPointModeKey -- which,
/// when the pressing window has no formula edit of its own, routes the Cancel to whichever OTHER
/// open workbook window has a live formula point-mode edit and silently discards it. Mirrors the
/// fix already shipped for Avalonia in
/// src/FreeX.App.Avalonia/MainWindow.KeyboardParity.cs's ShouldHandleEscapeLocallyBeforeFormulaPointMode.
/// </summary>
public sealed class R154_EscapeLocalBeforeFormulaPointModeTests
{
    [Fact]
    public void Escape_WithRibbonKeyTipsActiveInOtherWindow_DismissesKeyTipsLocally_LeavesOtherWindowsFormulaEditIntact()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = TwoWindowEscapeHarness.Create();
            harness.OwnerBeginFormulaPointModeEdit("=SUM(");
            harness.SourceEnterRibbonKeyTipMode();
            harness.SourceRibbonKeyTipsActive.Should().BeTrue(
                "the failure scenario requires window B's OWN key-tip session to be active before Escape");

            harness.SourcePressEscape();

            harness.OwnerHasActiveFormulaPointMode.Should().BeTrue(
                "window A's in-progress formula edit must survive an Escape that window B's own key-tips claimed locally");
            harness.OwnerFormulaBoxText.Should().Be("=SUM(",
                "window A's formula text must not be reverted by an Escape meant for window B's key-tips");
            harness.SourceRibbonKeyTipsActive.Should().BeFalse(
                "Escape must dismiss window B's own key-tip overlay");
        });
    }

    [Fact]
    public void Escape_WithNoLocalUiStateInOtherWindow_StillCancelsOtherWindowsFormulaEdit()
    {
        // Sibling/no-regression guard: when window B has no local UI state of its own claiming
        // Escape, the ordinary cross-window point-mode Cancel routing must still fire exactly as
        // before -- this is the legitimate "pointed into another open workbook, then cancelled"
        // gesture and must not be disabled by the fix above.
        StaTestRunner.Run(() =>
        {
            using var harness = TwoWindowEscapeHarness.Create();
            harness.OwnerBeginFormulaPointModeEdit("=SUM(");
            harness.SourceRibbonKeyTipsActive.Should().BeFalse();

            harness.SourcePressEscape();

            harness.OwnerHasActiveFormulaPointMode.Should().BeFalse(
                "with no local UI state active in window B, Escape must still cancel window A's routed formula edit");
        });
    }

    private sealed class TwoWindowEscapeHarness : IDisposable
    {
        private readonly MainWindow _owner;
        private readonly MainWindow _source;
        private readonly MethodInfo _sourceKeyDown;
        private readonly CellAddress _formulaCell;

        private TwoWindowEscapeHarness(MainWindow owner, MainWindow source, CellAddress formulaCell)
        {
            _owner = owner;
            _source = source;
            _formulaCell = formulaCell;
            _sourceKeyDown = typeof(MainWindow)
                .GetMethod("MainWindow_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_KeyDown");
        }

        public static TwoWindowEscapeHarness Create()
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
            return new TwoWindowEscapeHarness(owner, source, formulaCell);
        }

        public void OwnerBeginFormulaPointModeEdit(string formulaText)
        {
            _owner.BeginFormulaPointModeEditForTest(_formulaCell, formulaText);
            PumpDispatcher();
        }

        public void SourceEnterRibbonKeyTipMode()
        {
            // WPF keyboard focus is process-global, not per-window: window A's own
            // BeginFormulaPointModeEditForTest (called earlier in these tests) moves focus onto
            // window A's inline formula editor. Ground focus back on window B's own grid here --
            // right before window B enters key-tip mode -- so MainWindow_KeyDown's own
            // "FocusedElement is not TextBox/ComboBox" guard sees window B's real, current focus
            // instead of a stale TextBox left over from window A's edit. This mirrors the real
            // gesture the finding describes: the user is on window B's worksheet, not typing in a
            // text field, when they press Alt then Escape in window B.
            if (_source.FindName("SheetGrid") is IInputElement sheetGrid)
            {
                sheetGrid.Focus();
                Keyboard.Focus(sheetGrid);
            }
            PumpDispatcher();

            _source.EnterRibbonKeyTipModeForTest(FreeXRibbonKeyTipInputScope.TopLevel);
            PumpDispatcher();
        }

        public bool SourceRibbonKeyTipsActive => _source.RibbonKeyTipSessionForTest.IsActive;

        public bool OwnerHasActiveFormulaPointMode => _owner.HasActiveFormulaPointMode;

        public string OwnerFormulaBoxText => _owner.FormulaBoxTextForTest;

        /// <summary>
        /// Invokes MainWindow_KeyDown directly on window B -- the same entry point a real Escape
        /// keypress reaches after bubbling from whatever element has keyboard focus -- avoiding any
        /// dependency on which control actually has WPF focus in the headless test host.
        /// </summary>
        public void SourcePressEscape()
        {
            var presentationSource = PresentationSource.FromVisual(_source)
                ?? throw new InvalidOperationException("Source window presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _sourceKeyDown.Invoke(_source, [_source, args]);
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
