using System.Reflection;
using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 154, meta F3: mirrors R153_SpellCheckAddToDictionaryConcurrentPersistTests (WPF host,
/// FreeX.App.Host.Tests) for the Avalonia shell. MainWindow.Spelling.cs's ShowSpellingDialogAsync
/// hands FreeXOptionsRuntimeSession.MutateFresh a mutation lambda whose whole purpose is to run
/// against a snapshot MutateFresh just reloaded fresh from disk -- specifically so a custom
/// dictionary word another FreeX process persisted since this window's Spelling dialog opened is
/// not lost. The lambda instead threw that freshly-loaded list away and replaced it wholesale
/// with this window's own (possibly stale, reloaded-at-dialog-open) in-memory copy. FreeX has no
/// single-instance guard, so two ordinary FreeX processes sharing one options store is the normal
/// case, and the second process's "Add to Dictionary" click silently dropped the first process's
/// word from disk.
///
/// These tests drive the real ShowSpellingDialogAsync entry point (via reflection, since it is
/// private production code, not a test seam) through a genuinely modal SpellCheckDialog, clicking
/// its real "Add to Dictionary" button, so the coverage proves the production wiring in
/// MainWindow.Spelling.cs merges the freshly-loaded words instead of overwriting them.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R154_AvaloniaSpellCheckAddToDictionaryConcurrentPersistTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task AddToDictionary_WhenAnotherProcessAddedAWordSinceThisWindowLoaded_KeepsBothWords() =>
        Session.Dispatch(async () =>
        {
            await RunScenarioAsync(
                initialWords: ["Acme"],
                concurrentWriteWord: "Zulu",
                mustSurvive: ["Acme", "Zulu"]);
            return true;
        }, CancellationToken.None);

    /// <summary>
    /// Sibling no-regression case: the ordinary single-window scenario (nothing else wrote to the
    /// options store between this window's Spelling-dialog open and its own Add to Dictionary
    /// click) must keep persisting the newly-added word alongside the pre-existing one -- the
    /// union introduced for the concurrent-process case must not turn into a loss of the window's
    /// own addition or a spurious duplicate.
    /// </summary>
    [Fact]
    public Task AddToDictionary_WithNoConcurrentWriter_StillPersistsTheNewWordAlongsideExistingOnes() =>
        Session.Dispatch(async () =>
        {
            await RunScenarioAsync(
                initialWords: ["Bravo"],
                concurrentWriteWord: null,
                mustSurvive: ["Bravo"]);
            return true;
        }, CancellationToken.None);

    private static async Task RunScenarioAsync(
        string[] initialWords,
        string? concurrentWriteWord,
        string[] mustSurvive)
    {
        List<string>? savedWords = null;
        // The store's shared, live state: every `load` call reflects whatever is on "disk" at
        // that instant, exactly like AppOptionsStore.Load would for a real file two processes
        // share. MainWindow issues its own ambient Reload() calls throughout construction and
        // Show() (ribbon/status-bar/context-menu state), all long before "Add to Dictionary" is
        // clicked, so the concurrent word must not appear in the store until the test explicitly
        // simulates the other process's write, immediately before that click -- otherwise those
        // earlier ambient reloads would trivially pick it up and the test would not exercise the
        // merge-vs-overwrite defect at all.
        var storeWords = initialWords.ToList();
        var runtimeSession = new FreeXOptionsRuntimeSession(
            new AppOptions(),
            load: () => new AppOptions { SpellCheckCustomDictionaryWords = storeWords.ToList() },
            save: options =>
            {
                savedWords = options.SpellCheckCustomDictionaryWords.ToList();
                return true;
            });

        var window = new MainWindow([], null!, runtimeSession);
        try
        {
            window.Show();

            var sheet = window.Session.ActiveSheet;
            var address = new CellAddress(sheet.Id, 2, 2);
            // The same two-misspelling fixture text the production ParityCapture route
            // (ShowSpellCheckParityDialogAsync) and SpellCheckDialogLifecycleRegressionTests
            // already rely on to reliably surface a SpellCheckDialog issue.
            sheet.SetCell(address, Cell.FromValue(new TextValue("Quaterly reveneu")));

            var showSpellingDialogAsync = typeof(MainWindow).GetMethod(
                    "ShowSpellingDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowSpellingDialogAsync");

            var opener = (Task)showSpellingDialogAsync.Invoke(window, null)!;

            var dialog = await WaitForOwnedDialogAsync(window, "SpellCheckDialog");
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

            // Simulate another FreeX process persisting a word to the shared store right before
            // this window's own "Add to Dictionary" click reaches MutateFresh's fresh disk load --
            // the exact race the finding describes.
            if (concurrentWriteWord is not null)
                storeWords.Add(concurrentWriteWord);

            var addButton = dialog.GetVisualDescendants()
                .OfType<Button>()
                .Single(button =>
                    AutomationProperties.GetAutomationId(button) == "SpellCheckAddToDictionaryButton");
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

            // The fixture text has a second misspelling, so after Add to Dictionary the review
            // reopens the dialog for it; the exact word added is not the point of this test, so
            // stop the review there via the real Close/Cancel button (rather than assuming
            // completion) so the still-awaiting opener task can finish regardless of whether a
            // second issue exists.
            var nextDialog = await WaitForNewOwnedDialogAsync(window, dialog);
            nextDialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
            var dismissButton = nextDialog.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button =>
                    AutomationProperties.GetAutomationId(button) == "SpellCheckCancelButton")
                ?? nextDialog.GetVisualDescendants().OfType<Button>().Single();
            dismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

            var completed = await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().BeSameAs(opener,
                "ShowSpellingDialogAsync must finish once the review is stopped/completed");
            await opener;

            savedWords.Should().NotBeNull(
                "the Add to Dictionary action must persist through MutateFresh");
            foreach (var expected in mustSurvive)
            {
                savedWords.Should().Contain(expected,
                    $"'{expected}' must survive the merge with this window's newly-added word");
            }
            savedWords.Should().OnlyHaveUniqueItems(
                "the merge must not duplicate a word present in both the fresh and in-memory lists");
            savedWords.Should().HaveCount(mustSurvive.Length + 1,
                "the merge must contain every survivor word plus exactly the one new word this " +
                "window's Add to Dictionary click added -- no more, no fewer");
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray())
            {
                if (owned.IsVisible)
                    owned.Close();
            }

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            if (window.IsVisible)
                window.Close();
        }
    }

    private static async Task<Window> WaitForOwnedDialogAsync(MainWindow owner, string dialogAutomationId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window =>
                window.IsVisible &&
                string.Equals(
                    AutomationProperties.GetAutomationId(window),
                    dialogAutomationId,
                    StringComparison.Ordinal));
            if (dialog is not null)
                return dialog;

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException(
            $"Dialog {dialogAutomationId} did not open within 5 seconds.");
    }

    private static async Task<Window> WaitForNewOwnedDialogAsync(MainWindow owner, Window excluding)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window =>
                window.IsVisible && !ReferenceEquals(window, excluding));
            if (dialog is not null)
                return dialog;

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("A new dialog did not open within 5 seconds.");
    }
}
