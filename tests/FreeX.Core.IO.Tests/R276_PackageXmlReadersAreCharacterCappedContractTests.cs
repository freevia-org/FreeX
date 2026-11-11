using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r276: every XML reader built in the package-reading layer must carry the character cap, not just
/// the DTD prohibition.
///
/// <para>The codebase already decided this. <see cref="SecureXmlReaderSettings"/> sets all three
/// protections together, most of <c>FreeX.Core.IO</c> routes through it, and
/// <c>XlsxPivotCacheReader</c> carries a comment explaining precisely why the cap matters:
/// <c>WorkbookOpenSizeGuard</c> validates only the zip central directory's DECLARED entry lengths,
/// which an attacker controls outright, and never checks what the DeflateStream actually yields. A
/// part with a tiny compressed size and an enormous real one is therefore unbounded at the point of
/// parse.</para>
///
/// <para>Thirteen readers hand-rolled their own <see cref="XmlReaderSettings"/> and set
/// <c>DtdProcessing.Prohibit</c> without the cap. Twelve of them open a <c>ZipArchiveEntry</c>
/// straight from the workbook being opened. Streaming does not make them safe: a single colossal
/// text node or attribute is materialised as one string even in a pull-reader, which is exactly what
/// the cap bounds.</para>
///
/// <para>The zip-bomb class was fixed once before, in the pivot-cache path, and never fenced -- the
/// same shape as r275's culture bugs. This is the fence.</para>
/// </summary>
public sealed class R276_PackageXmlReadersAreCharacterCappedContractTests
{
    /// <summary>
    /// Proves the cap MECHANISM rejects an oversized document. It deliberately does not claim to
    /// prove each of the thirteen call sites -- the production cap is 64 MB, so exercising a real
    /// reader against it would need a 64 MB fixture. Coverage of the call sites is the contract
    /// below; this establishes that what the contract requires actually does something.
    /// </summary>
    [Fact]
    public void TheCharacterCapRejectsADocumentThatExceedsIt()
    {
        var payload = "<r>" + new string('x', 4096) + "</r>";
        using var reader = XmlReader.Create(
            new MemoryStream(Encoding.UTF8.GetBytes(payload)),
            SecureXmlReaderSettings.Create(maxCharactersInDocument: 512));

        var act = () =>
        {
            while (reader.Read())
            {
            }
        };

        act.Should().Throw<XmlException>(
            "without an enforced cap a crafted package part decompresses without bound at parse time");
    }

    [Fact]
    public void ADocumentInsideTheCapStillParses()
    {
        var payload = "<r>" + new string('x', 64) + "</r>";
        using var reader = XmlReader.Create(
            new MemoryStream(Encoding.UTF8.GetBytes(payload)),
            SecureXmlReaderSettings.Create(maxCharactersInDocument: 4096));

        var act = () =>
        {
            while (reader.Read())
            {
            }
        };

        act.Should().NotThrow("the cap must bound hostile input without rejecting ordinary parts");
    }

    // Production roots of all three apps plus the shared tier. Test projects, TestSupport, tools,
    // build output and docs are not production code.
    private static readonly string[] ProductionRoots = ["src", "freew", "freep", "shared"];

    // Files that may construct an XmlReaderSettings other than through SecureXmlReaderSettings,
    // each with its justification. Keep this list short; an entry needs a reason a package part or
    // user file can never reach it.
    private static readonly Dictionary<string, string> AllowedConstructions = new(StringComparer.Ordinal)
    {
        ["shared/Free.Shared.Opc/SecureXmlReaderSettings.cs"] =
            "the single hardened factory itself",
        ["src/FreeX.Core.Formula/BuiltInFunctions.TextAdvanced.cs"] =
            "FILTERXML evaluates a formula-argument string already held in memory (DtdProcessing is "
            + "Prohibit and XmlResolver is null); FreeX.Core.Formula does not reference Free.Shared.Opc",
    };

    /// <summary>
    /// Any mention of the <c>XmlReaderSettings</c> type whose statement also constructs one,
    /// whatever the spelling: <c>new XmlReaderSettings</c>, <c>XmlReaderSettings x = new()</c>,
    /// <c>XmlReaderSettings Make() =&gt; new() {...}</c>, <c>static readonly XmlReaderSettings S = new()</c>.
    /// (<c>SecureXmlReaderSettings</c> never matches: the pattern requires a word boundary before the type.)
    /// </summary>
    private static readonly Regex Construction = new(
        @"\bnew\s+(?:System\.Xml\.)?XmlReaderSettings\b"
        + @"|(?<![\w.])(?:System\.Xml\.)?XmlReaderSettings\b[^;]*?(?:=>|=|\breturn\b)\s*new\s*\(",
        RegexOptions.Compiled);

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"//.*$", "", RegexOptions.Multiline);
    }

    [Theory]
    [InlineData("var s = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };", true)]
    [InlineData("var s = new System.Xml.XmlReaderSettings();", true)]
    [InlineData("private static readonly XmlReaderSettings S = new()\n{\n    DtdProcessing = DtdProcessing.Prohibit\n};", true)]
    [InlineData("XmlReaderSettings s = new();", true)]
    [InlineData("private static XmlReaderSettings Make() =>\n    new()\n    {\n    };", true)]
    [InlineData("private static XmlReaderSettings Make() { return new() { }; }", true)]
    [InlineData("var s = SecureXmlReaderSettings.Create();", false)]
    [InlineData("var s = Free.Shared.Opc.SecureXmlReaderSettings.Create(); s.IgnoreComments = true;", false)]
    [InlineData("private static XmlReaderSettings Make() { var s = SecureXmlReaderSettings.Create(); return s; }", false)]
    public void TheConstructionDetectorSeesEverySpelling(string source, bool expected)
    {
        Construction.IsMatch(source).Should().Be(expected);
    }

    [Fact]
    public void NoProductionCodeConstructsXmlReaderSettingsOutsideTheSecureFactory()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var scanned = 0;
        var secureCalls = 0;
        var allowedHits = new HashSet<string>(StringComparer.Ordinal);

        foreach (var layer in ProductionRoots)
        {
            var directory = Path.Combine(root, layer);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (IsNonProduction(relative))
                    continue;

                scanned++;
                var text = StripComments(File.ReadAllText(file));
                secureCalls += Regex.Matches(text, @"SecureXmlReaderSettings\.Create\(").Count;

                foreach (Match match in Construction.Matches(text))
                {
                    if (AllowedConstructions.ContainsKey(relative))
                    {
                        allowedHits.Add(relative);
                        continue;
                    }

                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{relative}:{line}");
                }
            }
        }

        scanned.Should().BeGreaterThan(1000, "the production roots or the locator have drifted");
        secureCalls.Should().BeGreaterThan(40,
            "the scan must see the many SecureXmlReaderSettings.Create() call sites; a collapsed count "
            + "means the scan stopped reading real code and this passed while checking nothing");
        allowedHits.Should().BeEquivalentTo(AllowedConstructions.Keys,
            "a stale allow-list entry hides a future regression in that file; remove entries that no longer construct");

        offenders.Should().BeEmpty(
            "construct XmlReaderSettings through SecureXmlReaderSettings.Create(), which sets "
            + "DtdProcessing, XmlResolver and the character cap together, then set any extra Ignore* "
            + "flags on the returned instance. A hand-rolled initializer (including target-typed new()) "
            + "easily omits the cap, and the zip's declared sizes cannot be trusted to bound a part -- "
            + "see the comment in XlsxPivotCacheReader.\n" + string.Join("\n", offenders));
    }

    private static bool IsNonProduction(string relativePath)
    {
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment is "obj" or "bin" or "tools" or "tests" or "docs" or "build" or "TestSupport"
                || segment.EndsWith(".Tests", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The two readers that previously had NO character cap (the feature inspector's scan settings
    /// and the worksheet preflight settings) now carry the full hardened profile. The cap value is
    /// not injectable without widening the public API, so this asserts the settings objects the
    /// readers are created from and, separately, that such settings reject an oversized part.
    /// </summary>
    [Fact]
    public void FormerlyUncappedReaderSettingsCarryTheFullHardenedProfile()
    {
        var all = new (string Name, XmlReaderSettings Settings)[]
        {
            ("XlsxFeatureInspector.ScanXmlSettings", XlsxFeatureInspector.CreateScanXmlSettings()),
            ("XlsxWorksheetMetadataPreserver.PreflightSettings", XlsxWorksheetMetadataPreserver.CreateWorksheetPreflightReaderSettings()),
        };

        foreach (var (name, settings) in all)
        {
            settings.DtdProcessing.Should().Be(DtdProcessing.Prohibit, name);
            settings.MaxCharactersInDocument.Should().Be(SecureXmlReaderSettings.DefaultMaxCharactersInDocument, name);
            settings.IgnoreComments.Should().BeTrue(name);
            settings.IgnoreProcessingInstructions.Should().BeTrue(name);
        }

        XlsxWorksheetMetadataPreserver.CreateWorksheetPreflightReaderSettings().IgnoreWhitespace.Should().BeTrue();

        // Mechanism check on a copy of the real settings with the cap lowered: an oversized part throws.
        var capped = XlsxFeatureInspector.CreateScanXmlSettings();
        capped.MaxCharactersInDocument = 512;
        using var reader = XmlReader.Create(
            new MemoryStream(Encoding.UTF8.GetBytes("<r>" + new string('x', 4096) + "</r>")),
            capped);
        var act = () =>
        {
            while (reader.Read())
            {
            }
        };
        act.Should().Throw<XmlException>();
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
