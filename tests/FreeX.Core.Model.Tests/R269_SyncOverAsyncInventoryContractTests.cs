using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r269: every place the FreeX app layers block a thread on a task, with the reason it is safe.
///
/// <para>Sync-over-async is not always wrong, so a blanket ban would be a false contract. What is
/// dangerous is an UNEXAMINED one: blocking on a task whose continuation needs the thread you are
/// blocking gives a hang with no exception, no log line and no crash dump -- the user sees the app
/// stop responding. So this is an inventory, in the shape the no-op program's debt list used: every
/// site listed with a stated reason, and a failure when an unlisted one appears.</para>
///
/// <para>The nine current sites fall into three groups, each safe for a DIFFERENT reason, which is
/// why one blanket rule could not cover them.</para>
/// </summary>
public sealed class R269_SyncOverAsyncInventoryContractTests
{
    /// <summary>
    /// File -> count of examined blocking calls, with the reason that count is safe. A new blocking
    /// call in a listed file fails the count; one in an unlisted file fails the inventory.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Inventory = new(StringComparer.Ordinal)
    {
        ["src/FreeX.App.Host/MainWindow.ClipboardCommands.cs"] = (6,
            "Safe ONLY because WpfPlatformClipboard.InvokeAsync runs its action INLINE when "
            + "Dispatcher.CheckAccess() is true. These six calls run on the UI thread, so without that "
            + "fast path each would await a continuation posted to the very thread it is blocking, and "
            + "Ctrl+C/Ctrl+V would hang the app. The fast path is asserted separately below."),

        ["src/FreeX.App.Host/SentryCrashAnalytics.cs"] = (2,
            "Deliberate and correct: flushing crash telemetry while the process is terminating. There "
            + "is no continuation to starve because nothing else will run on this thread again."),

        ["src/FreeX.App.Services/PortablePdfDocumentExporter.cs"] = (1,
            "Reachable only from the path-taking Save overload, which has no production caller -- the "
            + "Avalonia PDF router passes a Stream and resolves to the stream overload. It matters "
            + "because AtomicExportExecutor awaits WITHOUT ConfigureAwait(false) in three places and "
            + "has two bare `await using` disposals, so a UI-thread caller of the path overload WOULD "
            + "deadlock. If one is ever added, fix the executor's awaits first."),
    };

    private static readonly string[] Layers =
    [
        "src/FreeX.App.Host",
        "src/FreeX.App.Avalonia",
        "src/FreeX.App.Presentation",
        "src/FreeX.App.Services",
    ];

    [Fact]
    public void EveryBlockingCallIsInTheInventory()
    {
        var root = RepositoryRoot();
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var layer in Layers)
        {
            var directory = Path.Combine(root, layer.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file))
                    continue;

                var blocking = File.ReadAllLines(file)
                    .Count(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && Regex.IsMatch(line, @"\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)"));

                if (blocking > 0)
                    found[Relative(root, file)] = blocking;
            }
        }

        var unlisted = found.Keys.Where(file => !Inventory.ContainsKey(file)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        unlisted.Should().BeEmpty(
            "a thread blocked on a task whose continuation needs that same thread hangs with no "
            + "exception and no crash dump. Add the site here with the reason it cannot deadlock, or "
            + "make the caller async. New:\n" + string.Join("\n", unlisted));

        var changed = Inventory
            .Where(entry => found.GetValueOrDefault(entry.Key) != entry.Value.Count)
            .Select(entry => $"{entry.Key}: expected {entry.Value.Count}, found {found.GetValueOrDefault(entry.Key)}")
            .ToList();

        changed.Should().BeEmpty(
            "the count in a listed file changed, so a blocking call was added or removed there and "
            + "its reason no longer describes what is present:\n" + string.Join("\n", changed));
    }

    /// <summary>
    /// The invariant six live call sites depend on. <c>WpfPlatformClipboard</c>'s async methods are
    /// awaited with <c>GetAwaiter().GetResult()</c> from the WPF UI thread; that is safe only while
    /// <c>InvokeAsync</c> short-circuits to a synchronous call when already on the dispatcher thread.
    /// Delete the fast path -- or add an await ahead of it -- and copy/paste hangs the application.
    /// </summary>
    [Fact]
    public void TheWpfClipboardRunsInlineOnTheDispatcherThread()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "shared", "Free.Shared.Shell.Wpf", "WpfPlatformClipboard.cs"));

        var start = source.IndexOf("private async ValueTask<T> InvokeAsync<T>(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "InvokeAsync must exist for this contract to check it");

        var body = source[start..source.IndexOf("\n    }", start, StringComparison.Ordinal)];

        body.Should().Contain("_dispatcher.CheckAccess()",
            "six GetAwaiter().GetResult() call sites in MainWindow.ClipboardCommands.cs run on the UI "
            + "thread and block on this method's result. Without the CheckAccess fast path the "
            + "continuation is posted to the thread that is blocking, and clipboard operations hang "
            + "the app with no exception to diagnose.");

        var checkAccessIndex = body.IndexOf("_dispatcher.CheckAccess()", StringComparison.Ordinal);
        var firstAwaitIndex = body.IndexOf("await ", StringComparison.Ordinal);
        (firstAwaitIndex < 0 || checkAccessIndex < firstAwaitIndex).Should().BeTrue(
            "the fast path has to be reached BEFORE any await, or the method yields on the UI thread "
            + "before it can short-circuit and the blocking callers deadlock anyway");
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
