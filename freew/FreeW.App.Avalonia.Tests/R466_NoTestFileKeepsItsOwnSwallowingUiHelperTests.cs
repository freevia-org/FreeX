using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r466: no test file may keep its own <c>OnUiThread</c> that swallows exceptions.
///
/// <para>r360 replaced that helper with <see cref="HeadlessUiThread"/> for a specific reason, written
/// in its own comment: a <c>catch (Exception) { return false; }</c> around the dispatch discards
/// EVERYTHING -- assertion failures included -- so the <c>if (!ran) return;</c> that follows every
/// call turns into an unconditional pass. That comment puts the blast radius at "over a thousand"
/// such guards in this assembly.</para>
///
/// <para>One file survived that migration with a private copy
/// (<c>DocumentViewBookmarkMergeLifecycleTests</c>, three tests). Its three tests passed either way,
/// so nothing was being hidden on the day it was found -- but the mechanism was there, and it was
/// demonstrated rather than argued: injecting one failing assertion into a test body made the file
/// report <c>Failed 1/3</c> through the shared helper and <c>Passed 3/3</c> through its own.</para>
///
/// <para>This guard exists because the same fix was already applied once, to both apps, and one copy
/// still came back. A source scan is what stops the next one -- the recurring shape this review keeps
/// meeting is a contract fixed in one place and not propagated.</para>
/// </summary>
public sealed class R466_NoTestFileKeepsItsOwnSwallowingUiHelperTests
{
    /// <summary>
    /// A local <c>OnUiThread</c> whose body catches and returns false rather than delegating. Matched
    /// across the whole declaration so a helper split over several lines is still caught.
    /// </summary>
    private static readonly Regex SwallowingHelper = new(
        @"Task<bool>\s+OnUiThread\s*\([^)]*\)\s*\{(?:[^{}]|\{[^{}]*\})*catch\s*\([^)]*\)\s*\{[^{}]*return\s+false\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void NoTestSourceDeclaresASwallowingUiThreadHelper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var directory in new[] { Path.Combine(root, "freew"), Path.Combine(root, "freep") })
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (!text.Contains("OnUiThread", StringComparison.Ordinal))
                    continue;

                scanned++;

                if (SwallowingHelper.IsMatch(text))
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        scanned.Should().BeGreaterThan(
            10,
            "the scan must actually be reaching the files that use OnUiThread -- if this falls to " +
            "nothing, a moved directory has made the guard vacuously green, which is precisely the " +
            "failure mode it exists to prevent");

        offenders.Should().BeEmpty(
            "a local OnUiThread that catches and returns false discards assertion failures too, so " +
            "every `if (!ran) return;` after it becomes an unconditional pass. Delegate to " +
            "HeadlessUiThread.Run, which lets the failure propagate.\n" + string.Join("\n", offenders),
            Array.Empty<object>());
    }
}
