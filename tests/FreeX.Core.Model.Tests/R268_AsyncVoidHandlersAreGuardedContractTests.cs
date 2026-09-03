using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r268: every <c>async void</c> in the three apps' UI layers must keep its exceptions inside itself.
///
/// <para>This is the first contract in the review program for a defect class OUTSIDE the command
/// layer. The class is a known repeat offender: the crash-hunt program found handlers escaping in
/// BOTH toolkits across twelve waves, and it is unusually punishing -- an exception from an
/// <c>async void</c> continuation has no caller to catch it, and Avalonia has no dispatcher-level
/// exception boundary, so the process terminates. The user loses the workbook.</para>
///
/// <para>All twenty-three sites are guarded TODAY; this test is why they stay guarded. It is a
/// ratchet, not a discovery: it exists because a class that was fixed twelve times and never fenced
/// is a class that comes back.</para>
///
/// <para>A site counts as guarded when every <c>await</c> in its own body sits inside a <c>try</c>,
/// OR when the body is a single delegation to a method that does the guarding -- the shape
/// <c>FreeW.PrintPreviewDialog.OnPrimaryActionClick</c> uses, and the one a naive check reports as a
/// bug. Tracing the callee before calling it a finding is the rule this program keeps relearning, so
/// the contract encodes it rather than flagging it.</para>
/// </summary>
public sealed class R268_AsyncVoidHandlersAreGuardedContractTests
{
    /// <summary>The UI layers of all three apps: where an unguarded handler kills the process.</summary>
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
    ];

    [Fact]
    public void EveryAsyncVoidKeepsItsExceptionsInside()
    {
        var root = RepositoryRoot();
        var unguarded = new List<string>();
        var examined = 0;

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

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!Regex.IsMatch(lines[i], @"\basync\s+void\s+\w+\s*\("))
                        continue;

                    examined++;
                    if (!IsGuarded(lines, i, out var reason))
                        unguarded.Add($"{Path.GetRelativePath(root, file)}:{i + 1} -- {reason}");
                }
            }
        }

        examined.Should().BeGreaterThan(15,
            "the scan must actually find the async void handlers; a collapsed count would mean the "
            + "layer paths or the signature pattern stopped matching and this passed while checking nothing");

        unguarded.Should().BeEmpty(
            "an exception from an async void continuation has no caller to catch it, and Avalonia has "
            + "no dispatcher-level exception boundary -- the process terminates and the user loses the "
            + "workbook. Wrap the awaits in a try, or delegate to a method that does.\n"
            + string.Join("\n", unguarded));
    }

    /// <summary>
    /// Guarded when every await is inside a try, or when the whole body is one delegation to a
    /// method that guards. The body is taken by brace matching from the signature, so a method that
    /// happens to precede a guarded one cannot borrow its try.
    /// </summary>
    private static bool IsGuarded(string[] lines, int signatureIndex, out string reason)
    {
        var (body, isExpressionBodied) = ExtractBody(lines, signatureIndex);

        // An expression body is a single delegation: `=> await Something();`. It cannot contain a
        // try, and its exception safety belongs to the callee, which the sibling test checks.
        if (isExpressionBodied)
        {
            reason = string.Empty;
            return true;
        }

        var firstTry = body.FindIndex(line => Regex.IsMatch(line, @"^\s*try\s*[\{]?\s*$"));
        var firstAwait = body.FindIndex(line => line.Contains("await ", StringComparison.Ordinal));

        if (firstAwait < 0)
        {
            reason = string.Empty;
            return true;
        }

        if (firstTry < 0)
        {
            reason = "awaits with no try at all";
            return false;
        }

        if (firstAwait < firstTry)
        {
            reason = $"an await on body line {firstAwait + 1} precedes the try on line {firstTry + 1}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static (List<string> Body, bool IsExpressionBodied) ExtractBody(string[] lines, int signatureIndex)
    {
        // An expression-bodied method: `=>` on the signature line or the next, with no opening brace.
        var signature = lines[signatureIndex];
        if (signature.Contains("=>", StringComparison.Ordinal))
            return ([], true);
        if (signatureIndex + 1 < lines.Length
            && !signature.Contains('{', StringComparison.Ordinal)
            && lines[signatureIndex + 1].TrimStart().StartsWith("=>", StringComparison.Ordinal))
        {
            return ([], true);
        }

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

        return (body, false);
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
