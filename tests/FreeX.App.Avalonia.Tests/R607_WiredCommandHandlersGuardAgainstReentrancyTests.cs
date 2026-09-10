using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r607: a ribbon handler that mutates the workbook must decline while an open or a save is running.
///
/// <para>The Avalonia shell stays interactive during both: the save reports through the status bar,
/// not a modal, and its inner work is awaited with <c>ConfigureAwait(false)</c> while
/// <c>WorkbookSaveService</c> hands the LIVE <c>Workbook</c> to <c>adapter.Save(workbook, file)</c>.
/// A ribbon click that lands between the save's await points therefore mutates the very object graph
/// the serializer is walking on another thread -- the "collection was modified" shape FreeW's mail
/// merge documents when it snapshots its template before backgrounding.</para>
///
/// <para>The shell answers this with <c>if (_isOpening || _isSaving) return;</c> at each entry point:
/// 4 assignments of those flags against 211 hand-written checks. A discipline applied 211 times by
/// hand is one a census should hold, and r607 found four handlers that had missed it --
/// <c>ApplyPageLayoutScale</c> (reached by the Width/Height/Percent boxes) plus the three
/// <c>Insert*AtActiveCell</c> commands, all of which reach
/// <c>_session.ExecuteReviewCommand(...)</c>. Two other handlers in the same page-layout file already
/// carried the guard, so it was an inconsistency inside one file, not a missing convention.</para>
///
/// <para>Containment may be one level down: several handlers are one-liners delegating to a shared
/// method that carries the guard (<c>ApplySelectedRangeCurrencyFormat</c> ->
/// <c>ApplySelectedRangeNumberFormat</c>). r606 learned that lesson the hard way, so this follows a
/// single delegation before reporting.</para>
/// </summary>
public sealed class R607_WiredCommandHandlersGuardAgainstReentrancyTests(ITestOutputHelper output)
{
    private const int GuardWindow = 600;

    // Handlers wired into the shell's command ports as "SomePort = SomeHandler,". Restricted to the
    // verbs that mutate; a Show* that only opens a pane is not this test's subject.
    private static readonly Regex WiredHandler = new(
        @"^\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?<handler>(?:Apply|Insert|Toggle|Delete|Remove)[A-Za-z0-9_]*)\s*,\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    private static Dictionary<string, string> ShellSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var directory = Path.Combine(root, "src", "FreeX.App.Avalonia");
        return Directory
            .EnumerateFiles(directory, "MainWindow*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetFileName(path), path => StripComments(File.ReadAllText(path)), StringComparer.Ordinal);
    }

    private static bool IsGuarded(Dictionary<string, string> sources, string method, int depth = 0)
    {
        if (depth > 1)
            return false;

        foreach (var text in sources.Values)
        {
            var declaration = Regex.Match(
                text,
                $@"(?:private|internal|public)\s+(?:async\s+)?(?:void|Task|Task<[^>]{{0,60}}>)\s+{Regex.Escape(method)}\s*\(");
            if (!declaration.Success)
                continue;

            var start = declaration.Index + declaration.Length;
            var body = text.Substring(start, Math.Min(GuardWindow, text.Length - start));
            if (body.Contains("_isOpening", StringComparison.Ordinal)
                || body.Contains("_isSaving", StringComparison.Ordinal))
            {
                return true;
            }

            // Delegation: the guard may sit in the method this one forwards to. Only callees whose
            // name begins with a MUTATING verb are followed -- the ones that would carry the guard.
            // Following every call would make the test easy to satisfy by accident, and capping at
            // the first few calls made it wrong the other way: ApplyRibbonNumberFormat reaches its
            // guarded ApplySelectedRangeNumberFormat on the FOURTH call in its body, so a cap of
            // three reported a handler that is in fact contained.
            foreach (Match call in Regex.Matches(
                         body,
                         @"\b(?<callee>(?:Apply|Insert|Toggle|Delete|Remove|Set|Commit)[A-Za-z0-9_]*)\s*\("))
            {
                var callee = call.Groups["callee"].Value;
                if (!string.Equals(callee, method, StringComparison.Ordinal)
                    && IsGuarded(sources, callee, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void EveryWiredMutatingHandlerDeclinesDuringAnOpenOrSave()
    {
        var sources = ShellSources();
        var handlers = sources.Values
            .SelectMany(text => WiredHandler.Matches(text).Select(match => match.Groups["handler"].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Non-vacuity: the wiring table must still be found, or an empty offender list means the
        // pattern drifted rather than the shell being clean.
        sources.Should().NotBeEmpty("the Avalonia shell partials must be reachable");
        handlers.Should().HaveCountGreaterThanOrEqualTo(
            10, "the command-ports wiring must still be matched; found: " + string.Join(", ", handlers));

        var unguarded = handlers.Where(handler => !IsGuarded(sources, handler)).ToList();

        output.WriteLine($"{handlers.Count} wired mutating handlers checked");
        foreach (var handler in unguarded)
            output.WriteLine("UNGUARDED " + handler);

        unguarded.Should().BeEmpty(
            "the shell stays interactive during an open or a save, and the save hands the live "
            + "workbook to the adapter on a background thread -- a mutation from a ribbon handler "
            + "races the serializer over the same object graph. Add "
            + "'if (_isOpening || _isSaving) return;' as these handlers' siblings do:\n"
            + string.Join("\n", unguarded));
    }
}
