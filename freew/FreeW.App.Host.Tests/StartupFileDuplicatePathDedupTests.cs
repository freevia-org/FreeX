using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// shared-startup-args F2: FreeW's WPF <see cref="MainWindow"/> constructor used to split
/// <c>startupFilePaths</c> into "open into this window" (index 0) and "open each into its own new
/// window" (everything else, deferred to <c>Loaded</c> -&gt; <c>OpenAdditionalStartupFiles</c>)
/// WITHOUT deduplicating first. A path repeated in argv -- e.g. multi-selecting the same file and
/// dragging it onto the taskbar icon, which Windows delivers as one launch with the path duplicated
/// -- therefore opened a SECOND, unsynchronized window on the same file with no "already open"
/// warning at open time. FreeX (<c>App.xaml.cs</c>) and FreeP (<c>PresentationStartupOpenSession</c>)
/// avoid this entirely by routing every startup argument through the shared
/// <see cref="Free.Shared.AppServices.StartupFileOpenPlanner"/>, whose <c>seenPaths</c> guard
/// collapses a duplicated path to one window. FreeW does not call that planner, so the fix instead
/// deduplicates <c>startupFilePaths</c> in the constructor itself (<c>DeduplicateStartupFilePaths</c>)
/// using the same <see cref="PlatformPathIdentityComparer"/> building block the planner uses.
///
/// <para>
/// These construct the real production <see cref="MainWindow"/> (exactly as <c>Program.cs</c>'s
/// <c>CreateWindow</c> lambda does: <c>new MainWindow(options, optionsStore, startupFilePaths:
/// startupFilePaths)</c>) and count how many <c>Loaded</c> handlers it registered -- the actual
/// production decision of whether a second window will be created -- rather than driving a real
/// <c>Show()</c>/layout pass. A second real window is only ever created once <c>Loaded</c> fires and
/// invokes an <c>OpenAdditionalStartupFiles</c> handler, so the handler count above the
/// always-registered autosave baseline (see the first test's remarks) is equivalent to "how many
/// second windows will open" without needing to render one. STA because constructing
/// <see cref="MainWindow"/> builds a real WPF visual tree (RichTextBox/FlowDocument, ribbon) that
/// requires an STA thread.
/// </para>
/// </summary>
public sealed class StartupFileDuplicatePathDedupTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.StartupDedupTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // Direct coverage of the fix itself: DeduplicateStartupFilePaths must collapse a path repeated in
    // argv to its first occurrence, using the same PlatformPathIdentityComparer the shared
    // StartupFileOpenPlanner uses for its own seenPaths guard (and case-insensitively/slash-normalized
    // on Windows, matching that comparer, not raw ordinal equality).
    [Fact]
    public void DeduplicateStartupFilePaths_CollapsesARepeatedPathToItsFirstOccurrence()
    {
        var method = GetDeduplicateMethod();
        var path = @"C:\work\Report.docx";

        var result = (string[])method.Invoke(null, [new[] { path, path }])!;

        Assert.Equal([path], result);
    }

    // Sibling no-regression: two distinct paths must both survive, in order -- the fix must not
    // collapse anything other than a true duplicate.
    [Fact]
    public void DeduplicateStartupFilePaths_KeepsTwoDistinctPathsInOrder()
    {
        var method = GetDeduplicateMethod();
        var first = @"C:\work\First.docx";
        var second = @"C:\work\Second.docx";

        var result = (string[])method.Invoke(null, [new[] { first, second } ])!;

        Assert.Equal([first, second], result);
    }

    // The exact user gesture from the finding, driven through the real MainWindow constructor: the
    // same path appears twice in startupFilePaths. Before the fix, distinctStartupFilePaths.Length
    // (really just startupFilePaths.Count) was 2, so a SECOND Loaded handler was always registered to
    // open a SECOND window on the very same path OpenPath already loaded into this window. After the
    // fix, deduplication collapses the list to one entry, so the "additional windows" branch is never
    // reached at all and no second Loaded handler is registered for it -- no second window will ever
    // be created for this launch.
    //
    // The comparison is against a single-path baseline rather than a hardcoded absolute count:
    // MainWindow always registers at least one Loaded handler of its own (autosave-recovery/start,
    // MainWindow.cs `Loaded += (_, _) => { ...; _autosave.Start(); }`), and shared window chrome/theme
    // resources may add further handlers this test has no reason to know the exact count of. What
    // must hold regardless of that baseline is that a duplicated path adds NO handler beyond it, while
    // (per the sibling test below) a genuinely distinct second path adds exactly one.
    [StaFact]
    public void MainWindow_WithDuplicateStartupFilePath_RegistersNoAdditionalWindowHandler()
    {
        var docPath = WriteDocx("Duplicate.docx", "Opened from the command line");
        var baselineHandlerCount = CountLoadedHandlers(new MainWindow(
            new FreeWOptions(),
            messageService: new RecordingUserMessageService(),
            startupFilePaths: [docPath]));

        var messages = new RecordingUserMessageService();
        var window = new MainWindow(
            new FreeWOptions(),
            messageService: messages,
            startupFilePaths: [docPath, docPath]);

        // The primary window still opens the (single distinct) path exactly as before.
        Assert.Equal(docPath, GetFileCommands(window).CurrentPath);
        Assert.False(GetFileCommands(window).IsDirty);
        Assert.Empty(messages.Messages);

        // Duplicating the same path must not register any handler beyond the single-path baseline --
        // the duplicate must not have added a second "open in a new window" handler on top of it.
        Assert.Equal(baselineHandlerCount, CountLoadedHandlers(window));
    }

    // Sibling no-regression: two DIFFERENT startup paths must still register the additional-window
    // Loaded handler (one more than the single-path baseline) -- proves the dedup fix collapses only
    // true duplicates and does not regress FreeW's existing "open every distinct file argument, each
    // in its own window" behaviour.
    [StaFact]
    public void MainWindow_WithTwoDistinctStartupFilePaths_RegistersTheAdditionalWindowHandler()
    {
        var firstPath = WriteDocx("First.docx", "First document");
        var secondPath = WriteDocx("Second.docx", "Second document");
        var baselineHandlerCount = CountLoadedHandlers(new MainWindow(
            new FreeWOptions(),
            messageService: new RecordingUserMessageService(),
            startupFilePaths: [firstPath]));

        var messages = new RecordingUserMessageService();
        var window = new MainWindow(
            new FreeWOptions(),
            messageService: messages,
            startupFilePaths: [firstPath, secondPath]);

        Assert.Equal(firstPath, GetFileCommands(window).CurrentPath);
        Assert.Empty(messages.Messages);
        // One more than the single-path baseline: the additional-window handler for secondPath.
        Assert.Equal(baselineHandlerCount + 1, CountLoadedHandlers(window));
    }

    private static MethodInfo GetDeduplicateMethod()
    {
        var method = typeof(MainWindow).GetMethod(
            "DeduplicateStartupFilePaths",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!;
    }

    // WPF routed events (Loaded included) do not expose their registered handlers through a plain
    // delegate field the way a normal C# event does -- AddHandler stores them in each UIElement's
    // internal EventHandlersStore. Reflecting into that internal store is the only way to observe
    // "was a Loaded handler registered" without actually raising Loaded (which requires a real
    // Show()/layout pass this test deliberately avoids -- see the class remarks).
    private static int CountLoadedHandlers(MainWindow window)
    {
        var storeProperty = typeof(UIElement).GetProperty(
            "EventHandlersStore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var store = storeProperty!.GetValue(window);
        if (store is null)
            return 0;

        var getHandlers = store.GetType().GetMethod(
            "GetRoutedEventHandlers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var handlers = (Array?)getHandlers!.Invoke(store, [FrameworkElement.LoadedEvent]);
        return handlers?.Length ?? 0;
    }

    private static FileCommands GetFileCommands(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_file",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (FileCommands)field!.GetValue(window)!;
    }

    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_tempDir, name);
        DocxWriter.Write(doc, path);
        return path;
    }
}
