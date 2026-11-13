using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Dedup guards for two helpers that used to be copy-pasted across the sister apps.
///
/// <para>Roman numerals: seven private <c>(1000, "M"), (900, "CM")...</c> tables lived in FreeW/FreeP
/// and had quietly diverged on overflow (one clamped, one was uncapped and could build a
/// multi-megabyte string). They now call <c>Free.Shared.IO.NumeralFormatter</c>. FreeX's Excel
/// <c>ROMAN()</c> worksheet function (forms 0-4) is a different, domain-specific algorithm and is out
/// of scope, so <c>src/</c> is not scanned for this one.</para>
///
/// <para>Image headers: PNG/JPEG/GIF/BMP signature and dimension parsing lived in six places with
/// different truncation handling. It now lives in <c>Free.Shared.Drawing.ImageHeader</c>; production
/// code must call it rather than test magic bytes or walk IHDR/SOF itself.</para>
/// </summary>
public sealed class NumeralAndImageHeaderDedupGuardTests(ITestOutputHelper output)
{
    private static readonly Regex RomanTable = new(
        @"\(\s*1000\s*,\s*""[Mm]""\s*\)|""M""\s*,\s*""CM""\s*,\s*""D""|\{\s*1000\s*,\s*900\s*,\s*500\b|\[\s*1000\s*,\s*900\s*,\s*500\b",
        RegexOptions.Compiled);

    /// <summary>Local PNG/JPEG/GIF/BMP magic-byte or IHDR/SOF parsing.</summary>
    private static readonly Regex[] ImageParsing =
    [
        new(@"0x89\s*,\s*0x50\s*,\s*0x4E\s*,\s*0x47", RegexOptions.Compiled),
        new(@"\b137\s*,\s*80\s*,\s*78\s*,\s*71\b", RegexOptions.Compiled),
        new(@"\[\s*0\s*\]\s*[!=]=\s*0x89\b", RegexOptions.Compiled),
        new(@"0xFF\s*,\s*0xD8", RegexOptions.Compiled),
        new(@"\[\s*1\s*\]\s*[!=]=\s*0xD8\b", RegexOptions.Compiled),
        new(@"\[\s*1[2-5]\s*\]\s*[!=]=\s*\(byte\)\s*'[IHDR]'", RegexOptions.Compiled),
        new(@"\[\s*[0-5]\s*\]\s*[!=]=\s*\(byte\)\s*'G'", RegexOptions.Compiled),
        new(@"marker\s*!=\s*0xC4\s*&&\s*marker\s*!=\s*0xC8", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Files allowed to contain what the guards look for, each with a reason a reader can check.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["shared/Free.Shared.IO/NumeralFormatter.cs"] = "the one shared roman table",
        ["shared/Free.Shared.Drawing/ImageHeader.cs"] = "the one shared image header reader",
        ["freep/FreeP.App.Presentation/EditingSession.cs"] =
            "WRITES a 1x1 PNG (emits the signature); it parses nothing",
        ["freep/FreeP.App.Presentation/PresentationRasterPdfExporter.cs"] =
            "a literal blank-white PNG constant used as an export fallback; it parses nothing",
        ["shared/Free.Shared.Shell.Avalonia/AvaloniaPlatformClipboard.cs"] =
            "validates clipboard PNG bytes before building a bitmap; Free.Shared.Shell.Avalonia does not "
            + "reference Free.Shared.Drawing and gaining that edge was left out of this slice",
    };

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"//.*$", "", RegexOptions.Multiline);
    }

    private static IEnumerable<(string RelativePath, string Text)> ProductionSources(params string[] areas)
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var separator = Path.DirectorySeparatorChar;

        foreach (var area in areas)
        {
            var directory = Path.Combine(root, area);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                    || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                    || file.Contains($"{separator}tools{separator}", StringComparison.Ordinal)
                    || file.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("TestSupport", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("Fixtures", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                yield return (relative, StripComments(File.ReadAllText(file)));
            }
        }
    }

    [Fact]
    public void NoRomanNumeralTableLivesOutsideTheSharedFormatter()
    {
        var scanned = 0;
        var offenders = new List<string>();
        foreach (var (path, text) in ProductionSources("freew", "freep", "shared"))
        {
            scanned++;
            if (Allowed.ContainsKey(path))
                continue;

            if (RomanTable.IsMatch(text))
                offenders.Add(path);
        }

        scanned.Should().BeGreaterThan(300, "the production roots or the locator has drifted");
        offenders.Should().BeEmpty(
            "roman numerals come from Free.Shared.IO.NumeralFormatter (explicit range, caller-owned "
            + "out-of-range policy); a private table re-creates the divergent overflow handling it replaced:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheRomanTablePatternStillMatchesTheShapesItGuards()
    {
        // Non-vacuity: the guard is only meaningful while its pattern recognises the old spellings.
        RomanTable.IsMatch("(1000, \"M\"), (900, \"CM\")").Should().BeTrue();
        RomanTable.IsMatch("new[] { \"M\", \"CM\", \"D\", \"CD\" }").Should().BeTrue();
        RomanTable.IsMatch("new[] { 1000, 900, 500, 400 }").Should().BeTrue();
        RomanTable.IsMatch("var x = 1000; var y = \"M\";").Should().BeFalse();
    }

    [Fact]
    public void NoImageHeaderParsingLivesOutsideTheSharedReader()
    {
        var scanned = 0;
        var offenders = new List<string>();
        foreach (var (path, text) in ProductionSources("src", "freew", "freep", "shared"))
        {
            scanned++;
            if (Allowed.ContainsKey(path))
                continue;

            foreach (var pattern in ImageParsing)
            {
                var match = pattern.Match(text);
                if (match.Success)
                    offenders.Add($"{path}: {match.Value}");
            }
        }

        scanned.Should().BeGreaterThan(500, "the production roots or the locator has drifted");
        foreach (var offender in offenders)
            output.WriteLine("LOCAL IMAGE PARSING " + offender);

        offenders.Should().BeEmpty(
            "image signatures and pixel sizes come from Free.Shared.Drawing.ImageHeader (total on "
            + "truncated input, one SOF marker set); a local copy re-creates the drift it replaced. "
            + "If a file only WRITES these bytes, add it to Allowed with the reason:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheImageParsingPatternsStillMatchTheShapesTheyGuard()
    {
        var shapes = new[]
        {
            "[0x89, 0x50, 0x4E, 0x47, 0x0D]",
            "[137, 80, 78, 71, 13, 10, 26, 10]",
            "if (bytes[0] != 0x89)",
            "[0xFF, 0xD8, 0xFF]",
            "bytes[1] != 0xD8",
            "bytes[12] != (byte)'I'",
            "bytes[0] != (byte)'G'",
            "marker != 0xC4 && marker != 0xC8 && marker != 0xCC",
        };

        foreach (var shape in shapes)
            ImageParsing.Any(pattern => pattern.IsMatch(shape)).Should().BeTrue($"'{shape}' must stay detectable");

        ImageParsing.Any(pattern => pattern.IsMatch("new SrgbColor(0x89, 0x89, 0x89)")).Should().BeFalse();
    }
}
