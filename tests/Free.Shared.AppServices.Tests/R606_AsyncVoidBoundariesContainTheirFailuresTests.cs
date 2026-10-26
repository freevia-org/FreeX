using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r606: every async boundary that returns void must contain its own failures, in all three apps.
///
/// <para>An async lambda or method that returns void has no caller to observe its Task. When it
/// throws, the exception surfaces on the dispatcher with nothing to catch it: WPF and Avalonia both
/// terminate the process. This repo has been bitten by it before: a valueless async lambda handed to
/// the headless <c>Dispatch</c> helper bound to its <c>Action</c> overload, making it async void, and
/// 154 tests silently passed while doing nothing. (Spelled out in prose on purpose --
/// <c>HeadlessDispatchOverloadContractTests</c> scans test sources for that literal lambda and would
/// flag this comment as if it were a real call site.)</para>
///
/// <para><c>FreeP.App.Avalonia.Tests.AsyncUiBoundarySourceTests</c> fences this, and three gaps in it
/// are what this test exists for:</para>
///
/// <list type="number">
/// <item>It scans <c>freep/</c> ONLY. FreeX and FreeW have the same boundaries and no contract.</item>
/// <item>It matches <c>+= async</c> and <c>_ = ...Async(</c>, and is blind to <c>async void</c>
/// METHOD DECLARATIONS -- the most direct spelling of the hazard it names. FreeP has eight, and its
/// companion test pins two of them by hand rather than scanning.</item>
/// <item>It is blind to the Action-bound lambda: <c>Dispatcher.BeginInvoke(async ...)</c> and
/// <c>Dispatcher.UIThread.Post(async ...)</c>, which is the exact shape of the historical bug.</item>
/// </list>
///
/// <para>Every site that exists today was traced by hand in r606 and every one is already contained,
/// so this is a fence over verified-correct code rather than a bug report. The rule is mechanical: a
/// containing <c>catch</c> must appear within the window after the boundary opens.</para>
/// </summary>
public sealed class R606_AsyncVoidBoundariesContainTheirFailuresTests(ITestOutputHelper output)
{
    private static readonly (string Name, Regex Pattern)[] Boundaries =
    [
        ("async void method", new Regex(@"\basync void [A-Za-z_]", RegexOptions.Compiled)),
        ("async event handler", new Regex(@"\+=\s*async\b", RegexOptions.Compiled)),
        ("Action-bound async lambda", new Regex(
            @"(?:Dispatcher\.UIThread\.Post|Dispatcher\.BeginInvoke)\(\s*async\b", RegexOptions.Compiled)),
    ];

    private static readonly string[] ProductionRoots = ["src", "freew", "freep", "shared"];

    /// <summary>
    /// Boundaries whose containment is real but lives in another file, keyed by "file:line-ish"
    /// content so a moved line does not silently drop the entry. Each needs a reason a reader can
    /// check, because the scan cannot.
    /// </summary>
    private static readonly Dictionary<string, string> ContainedElsewhere = new(StringComparer.Ordinal)
    {
        // App.xaml.cs: mainWindow.Dispatcher.BeginInvoke(async () => await operation());
        // `operation` is StartupRecoveryWorkflow.RestoreAndRetireCandidateAsync, a local function
        // that wraps its whole body in catch { } and retires (deletes) the snapshot only when
        // `restored` is true -- so a failed restore can neither escape nor destroy the user's only
        // surviving copy of their unsaved work. Fire-and-forget is deliberate here: the caller is
        // blocking the UI thread on GetResult(), and the restore has to run on that same thread
        // afterwards. FreeX's Avalonia host awaits instead (new ValueTask(operation())) because it
        // is not blocking.
        ["App.xaml.cs|Action-bound async lambda"] =
            "operation is StartupRecoveryWorkflow.RestoreAndRetireCandidateAsync, which catches its "
            + "whole body and only deletes the snapshot on success",
    };

    /// <summary>
    /// Characters after the boundary in which a containing catch must appear. Generous on purpose:
    /// the point is to catch a boundary with NO handler at all, not to police how tightly it is
    /// scoped. Measured against the sites that exist today, the largest gap is well inside this.
    /// </summary>
    private const int ContainmentWindow = 1200;

    /// <summary>
    /// The names a boundary awaits, in the order they appear in its body.
    /// </summary>
    private static readonly Regex AwaitedCall = new(
        @"await\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// True when the boundary awaits a method DECLARED IN THE SAME FILE whose own body contains a
    /// catch. Deliberately one level deep and same-file only: a deeper or cross-file search would
    /// start proving things this test cannot honestly check, and a boundary whose containment lives
    /// further away should carry an allowlist entry saying where, so a reader can verify it.
    /// </summary>
    private static bool DelegatesToAContainedMethod(string text, string window)
    {
        // Only the first few awaits, and only from the boundary's own opening -- not the whole
        // window, which may run past the end of the handler.
        foreach (Match awaited in AwaitedCall.Matches(window).Take(3))
        {
            var method = awaited.Groups["method"].Value;
            var declaration = Regex.Match(
                text,
                $@"\b(?:async\s+)?(?:Task|ValueTask)(?:<[^>]{{0,60}}>)?\s+{Regex.Escape(method)}\s*\(");
            if (!declaration.Success)
                continue;

            var bodyStart = declaration.Index + declaration.Length;
            var body = text.Substring(bodyStart, Math.Min(ContainmentWindow, text.Length - bodyStart));
            if (body.Contains("catch", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    private static IEnumerable<string> ProductionSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");

        foreach (var area in ProductionRoots)
        {
            var directory = Path.Combine(root, area);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("TestSupport", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    [Fact]
    public void EveryVoidReturningAsyncBoundaryHasACatch()
    {
        var offenders = new List<string>();
        var scanned = 0;
        var perKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var allowed = 0;

        foreach (var file in ProductionSources())
        {
            scanned++;
            var text = StripComments(File.ReadAllText(file));

            foreach (var (name, pattern) in Boundaries)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    perKind[name] = perKind.GetValueOrDefault(name) + 1;

                    var start = match.Index + match.Length;
                    var window = text.Substring(start, Math.Min(ContainmentWindow, text.Length - start));
                    if (window.Contains("catch", StringComparison.Ordinal))
                        continue;

                    // A one-line handler that just delegates -- "Click += async (_, _) =>
                    // await StopProtectionAsync();" -- is contained when the METHOD it awaits
                    // catches. That containment is real but not lexically nearby, which the window
                    // alone cannot see: the first draft of this test reported all five such sites,
                    // and hand-tracing each one showed the catch sitting in the callee. Follow one
                    // level of delegation within the same file before reporting.
                    if (DelegatesToAContainedMethod(text, window))
                        continue;

                    if (ContainedElsewhere.ContainsKey($"{Path.GetFileName(file)}|{name}"))
                    {
                        allowed++;
                        continue;
                    }

                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line}: {name}");
                }
            }
        }

        // Non-vacuity: all three spellings must actually be found, or a pattern has drifted and an
        // empty offender list would mean nothing. The two the FreeP guard already covers are here
        // too, so this test still reports if that guard is ever removed.
        scanned.Should().BeGreaterThan(500, "the production roots or the locator has drifted");
        foreach (var (name, _) in Boundaries)
        {
            perKind.GetValueOrDefault(name).Should().BeGreaterThan(
                0, $"no '{name}' boundary was matched at all; that pattern has drifted");
        }

        foreach (var (name, count) in perKind.OrderByDescending(entry => entry.Value))
            output.WriteLine($"{count,4}  {name}");
        foreach (var offender in offenders)
            output.WriteLine("UNCONTAINED " + offender);

        offenders.Should().BeEmpty(
            "a void-returning async boundary has no caller to observe its Task, so an escaping "
            + "exception reaches the dispatcher and terminates the app. Wrap the body in try/catch "
            + $"(or await it from a Func<Task> the caller observes). {offenders.Count} uncontained:\n"
            + string.Join("\n", offenders));
    }
}
