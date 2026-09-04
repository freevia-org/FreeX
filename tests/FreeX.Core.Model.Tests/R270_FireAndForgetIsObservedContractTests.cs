using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r270: the third and last way an async operation escapes supervision in the UI layers -- a task
/// started and discarded.
///
/// <para>With r268 (async void) and r269 (blocking on a task) this closes the set. The three fail
/// differently and that is worth keeping straight: an unguarded <c>async void</c> KILLS the process,
/// a bad block HANGS it, and an unobserved discarded task does neither -- it swallows the exception
/// and the feature simply does not happen. Silent is not harmless: the r269 clipboard hang at least
/// tells the user something is wrong, where a dropped autosave or a dropped update check does not.</para>
///
/// <para>All twenty-one sites are observed today, by four different mechanisms, and the contract has
/// to understand all four or it would report working code as broken:</para>
/// <list type="number">
/// <item>the discarded task's callee guards its own body (FreeX's ad-hoc style);</item>
/// <item>the discard routes through a guard helper whose whole job is observing (FreeW's
/// <c>AvaloniaUiTaskGuard</c>, which every FreeW <c>RunUiTask</c> funnels into);</item>
/// <item>the lambda passed to <c>Task.Run</c> carries its own try;</item>
/// <item>a <c>ContinueWith</c> inspects <c>IsFaulted</c>.</item>
/// </list>
/// </summary>
public sealed class R270_FireAndForgetIsObservedContractTests
{
    private static readonly string[] Layers =
    [
        "src/FreeX.App.Host",
        "src/FreeX.App.Avalonia",
        "src/FreeX.App.Presentation",
        "src/FreeX.App.Services",
        "freew/FreeW.App.Avalonia",
        "freew/FreeW.App.Host",
        "freew/FreeW.App.Presentation",
        "freep/FreeP.App.Avalonia",
        "freep/FreeP.App.Host",
        "freep/FreeP.App.Rendering.Avalonia",
        "freep/FreeP.App.Rendering.Wpf",
        // r272: added after auditing these lists against the repo. The first draft scanned eleven
        // projects and MISSED seven UI-bearing ones -- including both shared shells, which all three
        // apps run on, so a hole there was a hole in every app at once.
        "shared/Free.Shared.Shell.Avalonia",
        "shared/Free.Shared.Shell.Wpf",
        "src/FreeX.App.UI",
        "freep/FreeP.App.Presentation",
        "freep/FreeP.App.Media",
        "freep/FreeP.App.Ole.Windows",
        "freep/FreeP.App.Recording",
        "freep/FreeP.App.Recording.Windows",
    ];

    /// <summary>
    /// Methods whose entire purpose is to observe a task handed to them. A discard into one of these
    /// is observed by construction, and the sibling test below proves each still guards.
    /// </summary>
    private static readonly string[] GuardHelpers =
    [
        "ObserveAsync",
        "ObserveUiTaskAsync",
    ];

    [Fact]
    public void EveryDiscardedTaskIsObserved()
    {
        var root = RepositoryRoot();
        var unobserved = new List<string>();
        var examined = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                // NOT `_ = await X()`. Awaiting propagates the exception to the enclosing method, so
                // that is a value discard and entirely safe; only an UNawaited call abandons a task.
                // The first draft matched both and reported three working call sites as bugs.
                var match = Regex.Match(line, @"_\s*=\s*(?!await\b)([A-Za-z_][\w.]*)\s*\(");
                if (!match.Success)
                    continue;

                var callee = match.Groups[1].Value;

                // `_ = expr.GetAwaiter().GetResult()` discards a VALUE, not a task -- the call already
                // completed. Those sites belong to r269's blocking inventory, not to this class.
                if (line.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal)
                    || FollowingLinesComplete(lines, i, out var completedSynchronously) && completedSynchronously)
                {
                    continue;
                }

                if (!callee.EndsWith("Async", StringComparison.Ordinal)
                    && !callee.EndsWith("Task.Run", StringComparison.Ordinal)
                    && callee != "Task.Run")
                {
                    continue;
                }

                examined++;
                if (!IsObserved(lines, i, callee, file))
                    unobserved.Add($"{Relative(root, file)}:{i + 1} -- discards {callee}(...) with nothing observing it");
            }
        }

        // r282 lowered this from 12 to 8. Four discards disappeared legitimately: the FreeW dialogs
        // that each ran `_ = ObserveUiTaskAsync(...)` through their own private funnel now call the
        // shared AvaloniaUiTaskGuard instead. The floor keeps the same headroom under the real count
        // as before, and it earned its keep here -- it noticed the population change immediately
        // rather than letting the scan quietly shrink.
        examined.Should().BeGreaterThan(8,
            "the scan must find the discarded tasks; a collapsed count means the layer paths or the "
            + "discard pattern stopped matching and this passed while checking nothing");

        unobserved.Should().BeEmpty(
            "a discarded task's exception reaches TaskScheduler.UnobservedTaskException and is "
            + "swallowed -- no crash, no dialog, no log line, and the feature silently does not "
            + "happen. Guard the callee, route through a UI task guard, or handle the fault in a "
            + "ContinueWith.\n" + string.Join("\n", unobserved));
    }

    /// <summary>
    /// The guard helpers the discards rely on must actually guard. Without this the contract above
    /// would accept a discard into a helper that had quietly stopped catching -- exactly the
    /// delegation hole r268 had to encode and r262 paid a round to learn.
    /// </summary>
    [Fact]
    public void TheGuardHelpersStillCatch()
    {
        var root = RepositoryRoot();
        var checkedHelpers = 0;

        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!GuardHelpers.Any(helper =>
                        Regex.IsMatch(lines[i], @"\basync\s+Task\s+" + Regex.Escape(helper) + @"\s*\(")))
                {
                    continue;
                }

                checkedHelpers++;
                var body = MethodBody(lines, i);
                body.Any(l => Regex.IsMatch(l, @"^\s*catch\b")).Should().BeTrue(
                    $"{Relative(root, file)}:{i + 1} is a UI task guard that discards rely on; "
                    + "without a catch every discard routed through it becomes unobserved at once");

                // r282: having a catch is not the same as observing. Three FreeW dialog funnels
                // caught the general exception into an EMPTY body and this contract passed, so a
                // failing OK button did nothing with no message. A guard's general catch must do
                // something with the exception it binds -- the r277 lesson applied to my own fence:
                // the check tested the shape and not the substance.
                var generalCatch = body.FindIndex(l =>
                    Regex.IsMatch(l, @"^\s*catch\s*\(\s*Exception\s+(\w+)"));
                if (generalCatch < 0)
                    continue;

                var caught = Regex.Match(body[generalCatch], @"^\s*catch\s*\(\s*Exception\s+(\w+)")
                    .Groups[1].Value;
                var handler = string.Join("\n", body.Skip(generalCatch + 1).Take(12));
                // No lookahead here: the catch FILTER (`when (ex is not ...)`) already sits on the
                // catch line, which this scan starts after. An earlier draft excluded a trailing
                // `)` to skip that filter and thereby rejected `onFailure?.Invoke(ex)` -- reporting
                // the shared guard, which is correct, as a swallower. Fifth false positive of this
                // kind in the program, and the code was right every time.
                var usesIt = Regex.IsMatch(handler, @"\b" + Regex.Escape(caught) + @"\b");

                usesIt.Should().BeTrue(
                    $"{Relative(root, file)}:{generalCatch + i + 1} binds '{caught}' and never uses it, "
                    + "so every failure routed through this guard vanishes with no message, no log and "
                    + "no crash -- the silent-failure shape this whole contract exists to prevent");
            }
        }

        checkedHelpers.Should().BeGreaterThan(0,
            "the guard helpers must be found for this check to mean anything; if they were renamed, "
            + "update GuardHelpers so the discard scan keeps recognising them");
    }

    /// <summary>
    /// Observed when the discarded call is a known guard helper, when a Task.Run lambda or a
    /// ContinueWith handles the fault at the call site, or when the callee -- found in the same file
    /// -- guards its own awaits.
    /// </summary>
    private static bool IsObserved(string[] lines, int index, string callee, string file)
    {
        var simpleName = callee.Contains('.', StringComparison.Ordinal)
            ? callee[(callee.LastIndexOf('.') + 1)..]
            : callee;

        if (GuardHelpers.Contains(simpleName, StringComparer.Ordinal))
            return true;

        // The statement can span lines: take the call-site region up to the next blank line or 25
        // lines, whichever comes first, and look for its own fault handling.
        var region = string.Join("\n", lines.Skip(index).Take(25));
        if (Regex.IsMatch(region, @"ContinueWith") && Regex.IsMatch(region, @"IsFaulted|Exception"))
            return true;
        if (simpleName == "Run" || callee.EndsWith("Task.Run", StringComparison.Ordinal))
            return Regex.IsMatch(region, @"^\s*try\s*$", RegexOptions.Multiline);

        // Otherwise the callee must guard itself. It is looked up in the same file, which is where
        // every current site defines it; a cross-file callee would fail here and want its own entry.
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"\basync\s+(Task|ValueTask)(<[^>]+>)?\s+" + Regex.Escape(simpleName) + @"\s*\("))
                continue;

            var body = MethodBody(lines, i);
            var firstTry = body.FindIndex(l => Regex.IsMatch(l, @"^\s*try\s*[\{]?\s*$"));

            // `await Task.Yield()` is the one await that provably cannot fault -- its awaiter only
            // reschedules -- and the shared window-close coordinator opens with it deliberately, to
            // leave the synchronous Closing callback before doing anything else. Requiring it inside
            // the try would force a pointless edit to correct shared code.
            var firstAwait = body.FindIndex(l =>
                l.Contains("await ", StringComparison.Ordinal)
                && !l.Contains("Task.Yield()", StringComparison.Ordinal));

            return firstAwait < 0 || (firstTry >= 0 && firstTry < firstAwait);
        }

        return false;
    }

    private static List<string> MethodBody(string[] lines, int signatureIndex)
    {
        var body = new List<string>();
        var depth = 0;
        var opened = false;
        for (var i = signatureIndex; i < lines.Length; i++)
        {
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            if (lines[i].Contains('{', StringComparison.Ordinal))
                opened = true;
            body.Add(lines[i]);
            if (opened && depth == 0)
                break;
        }

        return body;
    }

    /// <summary>
    /// True when the discard statement resolves synchronously on a following line -- the
    /// `.AsTask().GetAwaiter().GetResult()` chain split across lines.
    /// </summary>
    private static bool FollowingLinesComplete(string[] lines, int index, out bool completedSynchronously)
    {
        completedSynchronously = false;
        for (var i = index; i < Math.Min(index + 5, lines.Length); i++)
        {
            if (lines[i].Contains("GetAwaiter().GetResult()", StringComparison.Ordinal)
                || lines[i].Contains(".GetResult()", StringComparison.Ordinal))
            {
                completedSynchronously = true;
                return true;
            }

            if (lines[i].TrimEnd().EndsWith(";", StringComparison.Ordinal) && i > index)
                return false;
        }

        return false;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var layer in Layers)
        {
            var directory = Path.Combine(root, layer.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
