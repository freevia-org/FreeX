using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r607: a save must gate every window that shares the workbook, not just the one saving.
///
/// <para><c>NewWindow()</c> builds its sibling from <c>_session.CreateSiblingView(...)</c>, so both
/// windows enumerate the SAME <c>Workbook</c>. <c>WorkbookSaveService</c> hands that live workbook to
/// <c>adapter.Save(workbook, file)</c> on a background thread, and the shell's only reentrancy guard
/// is the PER-WINDOW <c>_isSaving</c> field. Set in the saving window alone, every one of the
/// sibling's 211 guards keeps passing, and a keystroke there tears the shared cell dictionaries
/// mid-enumeration.</para>
///
/// <para>The WPF host fixed exactly this as R115-app-host-save-race, broadcasting through the window
/// registry to <c>SameDocumentExceptOrigin</c>. The Avalonia host had the same sharing, the same
/// registry, the same audience -- and no broadcast. This holds the fix in place by requiring the flag
/// to be written only through the helper that broadcasts.</para>
/// </summary>
public sealed class R607_SiblingWindowsAreToldWhenTheSharedWorkbookIsSerializedTests(ITestOutputHelper output)
{
    // The two methods allowed to touch the flag: the one that broadcasts, and the sibling-side
    // receiver it broadcasts to.
    private static readonly string[] FlagOwners = ["SetSavingAndTellSiblings", "ApplySaveInProgress"];

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    private static IEnumerable<(string Name, string Text)> ShellSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var directory = Path.Combine(root, "src", "FreeX.App.Avalonia");
        foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (Path.GetFileName(path), StripComments(File.ReadAllText(path)));
        }
    }

    [Fact]
    public void TheSavingFlagIsOnlyWrittenThroughTheBroadcastingHelper()
    {
        var offenders = new List<string>();
        var assignments = 0;

        foreach (var (name, text) in ShellSources())
        {
            foreach (Match match in Regex.Matches(text, @"_isSaving\s*="))
            {
                assignments++;

                // The owning method is the nearest declaration above the assignment.
                var preceding = text[..match.Index];
                var owner = FlagOwners.FirstOrDefault(candidate =>
                    preceding.LastIndexOf(candidate + "(", StringComparison.Ordinal) >
                    preceding.LastIndexOf("private ", StringComparison.Ordinal) - candidate.Length - 8);

                var lastOwnerAt = FlagOwners
                    .Select(candidate => preceding.LastIndexOf(candidate, StringComparison.Ordinal))
                    .Max();
                var lastMethodAt = preceding.LastIndexOf("    private ", StringComparison.Ordinal);
                if (lastOwnerAt >= lastMethodAt)
                    continue;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{name}:{line}");
            }
        }

        // Non-vacuity: the flag must still exist and be assigned somewhere, or this passes over a
        // shell that no longer has the guard at all.
        assignments.Should().BeGreaterThanOrEqualTo(
            2, "the shell must still track _isSaving; a collapsed count means the guard was removed");

        foreach (var offender in offenders)
            output.WriteLine("RAW ASSIGNMENT " + offender);

        offenders.Should().BeEmpty(
            "_isSaving is per-window, and a New Window sibling shares this window's Workbook. Writing "
            + "the flag directly leaves the sibling's guards passing while the adapter enumerates the "
            + "shared model on a background thread. Use SetSavingAndTellSiblings, which broadcasts:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void BothHostsBroadcastSaveStateToSiblingWindows()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Host", "WorkbookWindowRegistry.cs"));
        var avalonia = File.ReadAllText(
            Path.Combine(root, "src", "FreeX.App.Avalonia", "AvaloniaWorkbookWindowRegistry.cs"));

        // The asymmetry WAS the defect: one host broadcast and the other did not, over identical
        // sharing and an identical registry core.
        wpf.Should().Contain("BroadcastSaveInProgress");
        avalonia.Should().Contain("NotifySaveInProgress");

        foreach (var registry in new[] { wpf, avalonia })
        {
            registry.Should().Contain(
                "WorkbookWindowNotificationAudience.SameDocumentExceptOrigin",
                "the gate must reach the windows sharing this document, and only those");
        }
    }
}
