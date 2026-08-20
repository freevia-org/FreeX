using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round 153, shared-proofing F3: SpellCheckBtn_Click's "Add to Dictionary" persist callback
/// (MainWindow.ReviewCommands.cs) hands FreeXOptionsRuntimeSession.MutateFresh a mutation lambda
/// whose entire purpose is to run against a snapshot MutateFresh just reloaded fresh from disk --
/// specifically so a custom-dictionary word another FreeX process persisted since this window's
/// own snapshot was taken is not lost. The lambda instead threw that freshly-loaded list away and
/// replaced it wholesale with this window's own (possibly stale) in-memory copy. FreeX has no
/// single-instance guard, so two ordinary FreeX.exe processes sharing one options store is the
/// normal case (e.g. opening two workbooks from Explorer), and the second process's "Add to
/// Dictionary" click silently dropped the first process's word from disk.
///
/// These tests drive the real SpellCheckBtn_Click entry point (via reflection, exactly as the
/// ribbon/keyboard shortcut invokes it) through a genuinely modal SpellCheckDialog -- not the
/// SpellCheckSessionController/SpellCheckWorkflowPlanner helpers directly and not a hand-wired
/// persist callback -- so the coverage proves the production wiring in MainWindow.ReviewCommands.cs
/// merges the freshly-loaded words instead of overwriting them.
/// </summary>
public sealed class R153_SpellCheckAddToDictionaryConcurrentPersistTests
{
    [Fact]
    public void AddToDictionary_WhenAnotherProcessAddedAWordSinceThisWindowLoaded_KeepsBothWords()
    {
        StaTestRunner.Run(() =>
        {
            List<string>? savedWords = null;
            var runtimeSession = new FreeXOptionsRuntimeSession(
                new AppOptions { SpellCheckCustomDictionaryWords = ["Bravo"] },
                load: () => new AppOptions { SpellCheckCustomDictionaryWords = ["Acme"] },
                save: options =>
                {
                    savedWords = options.SpellCheckCustomDictionaryWords.ToList();
                    return true;
                });

            using var harness = MainWindowHarness.Create(runtimeSession);
            // Deliberately not (1,1): that is the window's default active/selected cell, and
            // writing the misspelling there would make SpellCheckBtn_Click's own pending-edit
            // guard (TryCommitPendingSpellCheckEdit, which compares the still-blank formula bar
            // against the active cell's text) think an edit is in progress and blank the cell
            // right back out before the scan ever runs.
            harness.SetCellText(5, 5, "Fix teh value.");

            harness.InvokeSpellCheckAndAddWordToDictionary();

            savedWords.Should().NotBeNull(
                "the Add to Dictionary action must persist through MutateRuntimeOptions");
            savedWords.Should().Contain("Acme",
                "the word another FreeX process persisted to disk since this window's snapshot was taken must not be dropped");
            savedWords.Should().Contain("teh",
                "the word this window's user just added must be persisted");
        });
    }

    /// <summary>
    /// Sibling no-regression case: the ordinary single-window scenario (nothing else wrote to the
    /// options store between this window's load and its own Add to Dictionary click) must keep
    /// persisting the newly-added word alongside the pre-existing one -- the merge introduced for
    /// the concurrent-process case must not turn into a loss of the window's own addition or a
    /// spurious duplicate.
    /// </summary>
    [Fact]
    public void AddToDictionary_WithNoConcurrentWriter_StillPersistsTheNewWordAlongsideExistingOnes()
    {
        StaTestRunner.Run(() =>
        {
            List<string>? savedWords = null;
            var runtimeSession = new FreeXOptionsRuntimeSession(
                new AppOptions { SpellCheckCustomDictionaryWords = ["Bravo"] },
                load: () => new AppOptions { SpellCheckCustomDictionaryWords = ["Bravo"] },
                save: options =>
                {
                    savedWords = options.SpellCheckCustomDictionaryWords.ToList();
                    return true;
                });

            using var harness = MainWindowHarness.Create(runtimeSession);
            harness.SetCellText(5, 5, "Fix teh value.");

            harness.InvokeSpellCheckAndAddWordToDictionary();

            savedWords.Should().NotBeNull();
            savedWords.Should().BeEquivalentTo(["Bravo", "teh"],
                "the ordinary single-window case (no concurrent writer) must keep persisting both the pre-existing and newly-added words, without duplicates");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _spellCheckBtnClick;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _spellCheckBtnClick = typeof(MainWindow)
                .GetMethod("SpellCheckBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SpellCheckBtn_Click");
        }

        public Workbook Workbook => _window.Session.Workbook;

        public void SetCellText(uint row, uint col, string text)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(text));
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real "Spell Check" entry point end to end: invokes SpellCheckBtn_Click
        /// (exactly as the ribbon/keyboard shortcut does), which synchronously opens a genuinely
        /// modal SpellCheckDialog via ShowDialog(). While that call blocks and pumps the
        /// dispatcher, a queued callback locates the dialog through the owner window's
        /// OwnedWindows (no test-only seam) and invokes the dialog's own private Accept() with
        /// SpellCheckDialog.CreateAddResult() -- the same code path the "Add to Dictionary" button
        /// click drives -- so the persist callback under test is whatever
        /// MainWindow.ReviewCommands.cs actually wired in, not a callback the test supplies
        /// itself.
        /// </summary>
        public void InvokeSpellCheckAndAddWordToDictionary()
        {
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = _window.OwnedWindows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.GetType().Name == "SpellCheckDialog");
                if (dialog is null)
                    return;

                var accept = dialog.GetType()
                    .GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("SpellCheckDialog", "Accept");
                accept.Invoke(dialog, [SpellCheckDialog.CreateAddResult()]);
            }), DispatcherPriority.ApplicationIdle);

            _spellCheckBtnClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public static MainWindowHarness Create(FreeXOptionsRuntimeSession runtimeSession)
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
                NullUserMessageService.Instance,
                optionsRuntimeSession: runtimeSession)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            // Any still-open SpellCheckDialog is owned by _window, so closing it first avoids
            // leaving a modal window behind for the next test.
            foreach (var owned in _window.OwnedWindows.OfType<Window>().ToArray())
                owned.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
