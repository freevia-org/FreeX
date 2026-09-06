using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r486: a `> 0` guard after a double parse does not reject infinity.
///
/// <para>r485 fixed one instance and noted the shape was worth carrying elsewhere. It was: a sweep of
/// all three apps found six more sites where a value parsed straight out of a FILE was validated
/// only by a lower bound, so "Infinity" - and "1e999", which .NET overflows to it - passed. The
/// consequences differed by site (a font size of infinity handed to text layout, INF/INF = NaN as a
/// caption frame rate, and an animation scale that PptxPackageWriter wrote back out verbatim as
/// x="Infinity", making the saved deck schema-invalid) but the defect is one shape.</para>
///
/// <para>This is the sweep kept as a tripwire, because the shape is easy to reintroduce and reads as
/// correct. It scans production sources in all three apps for a double/float TryParse whose nearby
/// guard tests only against zero.</para>
///
/// <para>The one allowed exception documents the CORRECT form: a guard with an upper bound excludes
/// infinity on its own, so XlsxWorksheetXmlValueParser's `floating > 0 && floating &lt;= uint.MaxValue`
/// needs no separate check. Where a natural bound exists, prefer it to an IsFinite call.</para>
/// </summary>
public sealed class R486_ParsedDoubleGuardsRejectInfinityTests
{
    // Files whose guard bounds the value from ABOVE, which already excludes infinity.
    private static readonly string[] BoundedElsewhere = ["XlsxWorksheetXmlValueParser.cs"];

    private static IEnumerable<string> ProductionSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");

        foreach (var app in new[] { "src", "freew", "freep", "shared" })
        {
            var directory = Path.Combine(root, app);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains("Tests", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    [Fact]
    public void NoParsedDoubleIsValidatedOnlyAgainstZero()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in ProductionSources())
        {
            scanned++;
            var text = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(
                         text, @"(?:double|float)\.TryParse\s*\([^;]{0,300}?out\s+var\s+(\w+)\s*\)"))
            {
                var name = match.Groups[1].Value;
                var window = text.Substring(
                    match.Index + match.Length,
                    Math.Min(400, text.Length - (match.Index + match.Length)));

                var guardedAgainstZeroOnly = Regex.IsMatch(window, $@"\b{Regex.Escape(name)}\s*(?:>=|>)\s*0\b");
                if (!guardedAgainstZeroOnly)
                    continue;

                if (window.Contains("IsFinite", StringComparison.Ordinal)
                    || window.Contains("IsInfinity", StringComparison.Ordinal)
                    || window.Contains("IsNaN", StringComparison.Ordinal))
                    continue;

                if (BoundedElsewhere.Contains(Path.GetFileName(file)))
                    continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line} (parsed '{name}')");
            }
        }

        // Non-vacuity: if the walk stops finding sources the scan would pass having read nothing.
        scanned.Should().BeGreaterThan(1000, "the scan must actually be reading the three apps' sources");

        offenders.Should().BeEmpty(
            "a lower-bound guard admits positive infinity, and .NET parses both \"Infinity\" and an " +
            "overflowing literal like \"1e999\" to it -- add double.IsFinite, or bound the value from " +
            "above as XlsxWorksheetXmlValueParser does");
    }
}
