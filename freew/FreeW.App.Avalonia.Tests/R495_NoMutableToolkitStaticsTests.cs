using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r495: a mutable Avalonia visual in a STATIC field is a documented crash class.
///
/// <para>The first render thread to touch such an object takes ownership of it, and any later thread
/// throws "The calling thread cannot access this object because a different thread owns it". The
/// failure is intermittent and reads as a flaky test rather than a defect, which is what makes it
/// worth a tripwire rather than a one-off fix.</para>
///
/// <para>Found because FreeW's DocumentView declared its comment, revision and proofing BRUSHES as
/// ImmutableSolidColorBrush - the author plainly knew the rule - while the four PENS in the same
/// declaration block were plain Pen wrapping a mutable SolidColorBrush. Sibling drift inside a single
/// block is exactly what a scan catches and a reader does not.</para>
///
/// <para>Two allowed exceptions, both documented rather than silent: a [ThreadStatic] cache is
/// per-thread by construction, and Bitmap is not an AvaloniaObject - it carries no thread ownership
/// check, and the codebase already holds decoded bitmaps across renders in its inline-image layouts.</para>
/// </summary>
public sealed class R495_NoMutableToolkitStaticsTests
{
    private static readonly string[] MutableVisualTypes =
    [
        "SolidColorBrush", "LinearGradientBrush", "RadialGradientBrush", "ImageBrush",
        "Pen", "DashStyle", "DrawingImage", "StreamGeometry", "PathGeometry",
    ];

    private static IEnumerable<string> AvaloniaSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");

        foreach (var project in new[]
                 {
                     Path.Combine(root, "freew", "FreeW.App.Avalonia"),
                     Path.Combine(root, "freep", "FreeP.App.Avalonia"),
                     Path.Combine(root, "freep", "FreeP.App.Rendering.Avalonia"),
                     Path.Combine(root, "src", "FreeX.App.Avalonia"),
                     Path.Combine(root, "shared", "Free.Shared.Shell.Avalonia"),
                     Path.Combine(root, "shared", "Free.Shared.Ribbon.Avalonia"),
                 })
        {
            if (!Directory.Exists(project))
                continue;

            foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    [Fact]
    public void NoStaticFieldHoldsAMutableAvaloniaVisual()
    {
        var offenders = new List<string>();
        var scanned = 0;

        var pattern = new Regex(
            @"\bstatic\s+(?:readonly\s+)?(?<type>" + string.Join("|", MutableVisualTypes) + @")\s+(?<name>\w+)\s*(?==|;)",
            RegexOptions.Compiled);

        foreach (var file in AvaloniaSources())
        {
            scanned++;
            var text = File.ReadAllText(file);

            foreach (Match match in pattern.Matches(text))
            {
                var before = text[Math.Max(0, match.Index - 200)..match.Index];
                if (before.Contains("[ThreadStatic]", StringComparison.Ordinal))
                    continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line} {match.Groups["type"].Value} {match.Groups["name"].Value}");
            }
        }

        // Non-vacuity: if the walk stops finding the shells, the scan passes having read nothing.
        scanned.Should().BeGreaterThan(100, "the scan must actually be reading the Avalonia shells");

        offenders.Should().BeEmpty(
            "a mutable Avalonia visual in a static field is owned by the first render thread that " +
            "touches it, and every later thread throws -- use the Immutable* variant, as the brushes " +
            "beside these declarations already do");
    }
}
