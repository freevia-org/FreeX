using System.Diagnostics;
using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R118 (MainWindow.WorkbookUiState.cs, FormatNameBoxSelectionText): the Name Box used to re-scan
/// every entry of both <c>Workbook.ScopedNamedRanges</c> and <c>Workbook.NamedRanges</c> from
/// scratch on EVERY call -- i.e. on every plain click/arrow-key move (SetActiveCell), paste, sort,
/// and merge -- with no cache, unlike the revision-keyed <c>WorkbookSelectionStatsCache</c> this
/// same codebase already built for the analogous status-bar aggregates. In a workbook with a large
/// defined-name count (a well-documented Excel copy/paste bloat pattern), this made ordinary
/// navigation measurably slower with every additional name, forever, since nothing was ever cached.
///
/// <see cref="NameBoxSelectionText_WithManyDefinedNames_RepeatedCallsAtSameRevisionStayCheap"/> is
/// the perf regression: it defines a large number of names once, then calls the real product method
/// (via reflection -- the nearest headless seam for a WPF MainWindow) many times with NO intervening
/// model change, and asserts the total time stays far below the O(names x calls) cost the old
/// unconditional re-scan would have paid. Verified failing against the pre-fix implementation (see
/// the fail-before evidence in the round's report) and passing after the fix, via the mandated
/// cp-backup/hand-revert technique -- not by asserting on cache internals.
///
/// <see cref="NameBoxSelectionText_AfterDefiningNameThroughCommandBus_ReflectsNewNameImmediately"/>
/// is the no-regression sibling: it proves the cache invalidation that makes the perf fix safe --
/// defining a name through the REAL app flow (DefineNamedRangeCommand via the command bus, exactly
/// like MainWindow.Editing.cs's Name Box "Enter with new name" flow and the Name Manager dialog) must
/// still make the Name Box show the new name on the very next selection change, proving the cache
/// never serves stale data across an actual model change.
/// </summary>
public sealed class R118_NameBoxRangeIndexCacheTests
{
    [Fact]
    public void NameBoxSelectionText_WithManyDefinedNames_RepeatedCallsAtSameRevisionStayCheap()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = NameBoxHarness.Create();
            var sheet = harness.Workbook.Sheets[0];

            // A workbook with a large defined-name count, mirroring the review finding's "hundreds to
            // several thousand defined names" real-world bloat scenario. None of these ranges match
            // the range that will actually be queried below, so the pre-fix implementation is forced
            // to walk every single entry of both dictionaries on every call (the worst case its
            // unconditional `foreach` always pays, match or no match).
            const int definedNameCount = 20_000;
            for (var i = 0; i < definedNameCount; i++)
            {
                var row = (uint)(1000 + i);
                var namedRange = new GridRange(
                    new CellAddress(sheet.Id, row, 1),
                    new CellAddress(sheet.Id, row, 1));
                harness.Workbook.DefineNamedRange($"BloatName{i}", namedRange);
            }

            // The queried range never matches any defined name, so every call falls all the way
            // through to the plain A1 fallback -- exactly the branch that used to force a full,
            // fruitless scan of both dictionaries every single time.
            var queriedRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var formatMethod = typeof(MainWindow).GetMethod(
                "FormatNameBoxSelectionText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormatNameBoxSelectionText");

            // Warm-up call: builds/primes any lazy cache (or, pre-fix, just does its one scan) so the
            // timed region below measures only the cost of REPEATED calls with no model change in
            // between -- i.e. ordinary repeated navigation, not the unavoidable first-touch cost.
            formatMethod.Invoke(harness.Window, [queriedRange]);

            const int repeatedCalls = 4_000;
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < repeatedCalls; i++)
                formatMethod.Invoke(harness.Window, [queriedRange]);
            stopwatch.Stop();

            // With the revision-keyed cache, 4,000 repeated queries against an unchanged 20,000-name
            // workbook are ~4,000 O(1) dictionary lookups -- comfortably under a second even on a
            // loaded CI box. Without it (the pre-fix behavior), the same loop re-walks 20,000 names
            // twice per call -- 160,000,000+ comparisons -- which takes several seconds on the same
            // hardware. 1.5s leaves a wide margin above the fixed cost while staying far below the
            // unfixed cost.
            // Measured on this round's dev machine: the fixed (cached) implementation completes this
            // loop in ~2ms; the pre-fix (unconditional re-scan) implementation takes ~1.1s for the
            // same loop. 300ms sits comfortably above normal jitter for the fixed path and far below
            // the unfixed path's cost, so it reliably tells the two implementations apart without
            // being a hair-trigger flake risk.
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(300),
                "repeated Name Box queries against an unchanged large defined-name table must be " +
                "served from a cache, not re-scan every name on every single call");
        });
    }

    // Sibling no-regression: the perf fix must never serve a stale answer across a REAL model
    // change. Defining a name through the command bus (the same path MainWindow.Editing.cs's Name
    // Box "Enter with new name" flow and NamedRangeDialog use) bumps the navigation-cache revision
    // the index is keyed on, so the very next selection change must already see the new name.
    [Fact]
    public void NameBoxSelectionText_AfterDefiningNameThroughCommandBus_ReflectsNewNameImmediately()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = NameBoxHarness.Create();
            var sheet = harness.Workbook.Sheets[0];
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));

            var formatMethod = typeof(MainWindow).GetMethod(
                "FormatNameBoxSelectionText", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormatNameBoxSelectionText");

            // Prime the cache with a query BEFORE the name exists, at the current revision.
            var before = (string)formatMethod.Invoke(harness.Window, [range])!;
            before.Should().Be("B2:C3");

            // Define the name through the real command path (mirrors DefineNamedRangeCommand usage
            // in MainWindow.Editing.cs / NamedRangeDialog.xaml.cs), which bumps the navigation-cache
            // revision via TryExecuteCommand.
            harness.DefineNamedRangeThroughCommandBus("Budget", range);

            // Querying the SAME range again must now show the freshly-defined name, not the stale
            // A1-style reference the cache returned a moment ago.
            var after = (string)formatMethod.Invoke(harness.Window, [range])!;
            after.Should().Be("Budget",
                "a name defined through the real command path must be visible on the very next Name Box query, proving the cache is invalidated on real model changes rather than only lazily built once");
        });
    }

    private sealed class NameBoxHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        private NameBoxHarness(MainWindow window, Workbook workbook)
        {
            Window = window;
            Workbook = workbook;
        }

        public void DefineNamedRangeThroughCommandBus(string name, GridRange range)
        {
            var tryExecuteCommand = typeof(MainWindow).GetMethod(
                "TryExecuteCommand",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(IWorkbookCommand), typeof(string)],
                modifiers: null)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryExecuteCommand");

            var command = new DefineNamedRangeCommand(name, range);
            var result = (bool)tryExecuteCommand.Invoke(Window, [command, "Define Name"])!;
            result.Should().BeTrue("the command bus define-name path must succeed for a fresh, valid name/range pair");
            PumpDispatcher();
        }

        public static NameBoxHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new NameBoxHarness(window, workbookRef.Current);
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
