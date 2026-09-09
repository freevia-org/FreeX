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

    /// <summary>
    /// r579: strips // and /* */ comments before scanning. Every rule here works in a fixed
    /// character window after the parse, and a multi-line explanatory comment between the parse
    /// and its guard pushes the guard out of that window. r577 catalogued that as a FALSE POSITIVE
    /// mode of an ad-hoc scan; r579 found it working in the other direction against THIS FILE --
    /// the new reject-range rule passed with its fix reverted, because the comment explaining the
    /// fix was longer than the window. A tripwire that a comment can silence is not a tripwire, and
    /// the r512 rule applies to the tripwire itself: it was only trustworthy once reverting a fix
    /// was shown to make it fail. String literals are left alone -- they cannot contain a guard.
    /// </summary>
    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

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
            var text = StripComments(File.ReadAllText(file));

            foreach (Match match in Regex.Matches(
                        // r574: this matched "out var name" ONLY, so every site declaring the target
                        // another way was invisible to it: "out value" (a pre-declared out parameter),
                        // "out double seconds", "out int ms". r571, r572 and r573 each fixed a defect
                        // of exactly this shape that THIS TRIPWIRE was built to catch -- it had been
                        // watching one spelling of the thing it was watching for.
                        text, @"(?:double|float)\.TryParse\s*\([^;]{0,300}?out\s+(?:var\s+|double\s+|float\s+)?(\w+)\s*\)"))
            {
                var name = match.Groups[1].Value;
                var window = text.Substring(
                    match.Index + match.Length,
                    Math.Min(400, text.Length - (match.Index + match.Length)));

                var guardedAgainstZeroOnly = Regex.IsMatch(window, $@"\b{Regex.Escape(name)}\s*(?:>=|>)\s*0\b");
                if (!guardedAgainstZeroOnly)
                    continue;

                // r574: an UPPER bound excludes infinity on its own, which is this test's own
                // documented rule ("where a natural bound exists, prefer it to an IsFinite call").
                // The original scan expressed that as a one-file allow-list; widening the parse
                // pattern surfaced more correctly-guarded two-sided sites, so the rule is encoded
                // here instead. "width > 0 && width <= 12" is right and must not be reported.
                if (Regex.IsMatch(window, $@"\b{Regex.Escape(name)}\s*(?:<=|<)\s*[\w.]"))
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

    /// <summary>
    /// r577: the second shape this scan can see cheaply. <see cref="Math.Clamp(double, double, double)"/>
    /// reads as "this value is now in range", and for Infinity it is -- but it PROPAGATES NaN, so a
    /// clamped parse still admits NaN. That has cost five separate rounds (r547, r548, r555, r567 and
    /// r577's two SVG gradient-offset spellings), which is enough repetitions to keep as a tripwire
    /// rather than to keep rediscovering.
    /// <para>
    /// This rule is deliberately narrow: it fires only where the parsed name is itself clamped
    /// nearby. A parse with NO guard at all is not reported -- most such sites are legitimate, so a
    /// blanket rule would be noise rather than a signal.
    /// </para>
    /// </summary>
    [Fact]
    public void NoParsedDoubleIsBoundedOnlyByAClamp()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in ProductionSources())
        {
            scanned++;
            var text = StripComments(File.ReadAllText(file));

            foreach (Match match in Regex.Matches(
                        text, @"(?:double|float)\.TryParse\s*\([^;]{0,300}?out\s+(?:var\s+|double\s+|float\s+)?(\w+)\s*\)"))
            {
                var name = match.Groups[1].Value;
                var window = text.Substring(
                    match.Index + match.Length,
                    Math.Min(400, text.Length - (match.Index + match.Length)));

                if (!Regex.IsMatch(window, $@"Math\.Clamp\s*\([^;]{{0,80}}\b{Regex.Escape(name)}\b"))
                    continue;

                if (window.Contains("IsFinite", StringComparison.Ordinal)
                    || window.Contains("IsInfinity", StringComparison.Ordinal)
                    || window.Contains("IsNaN", StringComparison.Ordinal))
                    continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line} (parsed '{name}')");
            }
        }

        scanned.Should().BeGreaterThan(1000, "the scan must actually be reading the three apps' sources");

        offenders.Should().BeEmpty(
            "Math.Clamp bounds infinity but passes NaN straight through, and .NET parses the literal " +
            "\"NaN\" happily -- a clamped parse still needs double.IsFinite before the clamp");
    }

    /// <summary>
    /// r579: the third rule, and the one that corrects a reading of this file's own doc comment.
    /// The summary above says "where a natural bound exists, prefer it to an IsFinite call" -- true
    /// for an ACCEPT form, where <c>x &gt; 0 &amp;&amp; x &lt;= 12</c> excludes Infinity AND NaN,
    /// since NaN fails an accept. It is NOT true for a two-sided REJECT form: <c>x is &lt; 0 or
    /// &gt; 100</c> (returning false) excludes Infinity, because <c>Infinity &gt; 100</c> is true,
    /// but admits NaN, because BOTH of its comparisons are false. The two shapes read alike and are
    /// not alike.
    /// <para>
    /// Two files had it: NumberFormatColorMapper's theme tint and AnimationPanePlanner's Smooth
    /// Start/End percentage. Both are reported by this rule, which fires only where a parse's ONLY
    /// screen is a two-sided reject range.
    /// </para>
    /// <para>
    /// Note also that "NaN" parses regardless of how narrow the NumberStyles are -- .NET checks the
    /// NaN/Infinity symbols independently of the style flags -- so a style set without
    /// AllowExponent or any special-value flag is not a screen either.
    /// </para>
    /// </summary>
    [Fact]
    public void NoParsedDoubleIsScreenedOnlyByATwoSidedRejectRange()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in ProductionSources())
        {
            scanned++;
            var text = StripComments(File.ReadAllText(file));

            foreach (Match match in Regex.Matches(
                        text, @"(?:double|float)\.TryParse\s*\([^;]{0,300}?out\s+(?:var\s+|double\s+|float\s+)?(\w+)\s*\)"))
            {
                var name = match.Groups[1].Value;
                var window = text.Substring(
                    match.Index + match.Length,
                    Math.Min(400, text.Length - (match.Index + match.Length)));

                var escaped = Regex.Escape(name);
                var isRejectRange =
                    Regex.IsMatch(window, $@"\b{escaped}\s+is\s*<[^;]{{0,40}}\bor\b[^;]{{0,40}}>")
                    || Regex.IsMatch(window, $@"\b{escaped}\s*<\s*[-\w.]+\s*\|\|\s*{escaped}\s*>")
                    || Regex.IsMatch(window, $@"\b{escaped}\s*>\s*[-\w.]+\s*\|\|\s*{escaped}\s*<");
                if (!isRejectRange)
                    continue;

                if (window.Contains("IsFinite", StringComparison.Ordinal)
                    || window.Contains("IsInfinity", StringComparison.Ordinal)
                    || window.Contains("IsNaN", StringComparison.Ordinal))
                    continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line} (parsed '{name}')");
            }
        }

        scanned.Should().BeGreaterThan(1000, "the scan must actually be reading the three apps' sources");

        offenders.Should().BeEmpty(
            "a two-sided REJECT range excludes infinity but admits NaN, since every comparison with " +
            "NaN is false -- add double.IsNaN to the reject, or express the screen as an ACCEPT " +
            "range, which excludes both");
    }
}
