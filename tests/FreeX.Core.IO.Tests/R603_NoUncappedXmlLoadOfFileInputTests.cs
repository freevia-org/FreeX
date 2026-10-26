using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r603: the other spelling of r276.
///
/// <para>r276 fences the character cap by inspecting every hand-rolled
/// <c>new XmlReaderSettings</c> initializer. That is blind to code which never constructs settings
/// at all: <c>XDocument.Load(stream)</c> and <c>XElement.Load(stream)</c> build their own defaults,
/// which prohibit DTDs but carry NO character cap -- so a part with a tiny compressed size and an
/// enormous real one is unbounded at the point of parse, which is exactly the hazard r276 exists
/// for. WorkbookOpenSizeGuard cannot help: it validates the zip central directory's DECLARED
/// lengths, which an attacker controls outright.</para>
///
/// <para>Four production sites had it, in all three apps -- including both normalizers that r583/r584
/// had ALREADY been corrected for this contract. That correction landed on the scan path and not on
/// the document load beside it, and r276 could not say so. A contract that watches one spelling of
/// the thing it watches for is how the second spelling survives.</para>
///
/// <para>Loads from an embedded assembly resource are exempt and listed by name: the bytes ship
/// inside the binary, so they are not input. Everything else in these layers parses package parts.</para>
/// </summary>
public sealed class R603_NoUncappedXmlLoadOfFileInputTests(ITestOutputHelper output)
{
    private static readonly string[] Layers =
    [
        Path.Combine("src", "FreeX.Core.IO"),
        Path.Combine("freew", "FreeW.Core.IO"),
        Path.Combine("freep", "FreeP.Core.IO"),
        Path.Combine("freep", "FreeP.App.Presentation"),
        Path.Combine("shared", "Free.Shared.Opc"),
        Path.Combine("shared", "Free.Shared.IO"),
    ];

    // A stream that came from GetManifestResourceStream is not file input -- the bytes ship in the
    // assembly. Recorded as (file, count) so a new uncapped load in the same file is still reported.
    private static readonly Dictionary<string, int> EmbeddedResourceLoads = new(StringComparer.Ordinal)
    {
        ["DocxWriter.cs"] = 2,
    };

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    // XDocument/XElement LOAD whose first argument is not an XmlReader. A reader argument is the
    // capped form -- the reader carries the settings.
    //
    // Parse(string) is deliberately NOT matched, and that is a correction to this test's first
    // draft: it reported 87 sites, nearly all XElement.Parse over preserved native XML the model
    // already holds as a string. The memory was spent when that string was built; a cap at parse
    // time bounds nothing. The hazard r276 names is decompression AT the point of parse, which only
    // a stream reaches.
    private static readonly Regex UncappedLoad = new(
        @"\b(?:XDocument|XElement)\.Load\s*\(\s*(?<first>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled);

    [Fact]
    public void NoPackageLayerXmlLoadBypassesTheCharacterCap()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var offenders = new List<string>();
        var scanned = 0;
        var sites = 0;

        foreach (var relative in Layers)
        {
            var directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                scanned++;
                var name = Path.GetFileName(file);
                var text = StripComments(File.ReadAllText(file));
                var exempt = EmbeddedResourceLoads.TryGetValue(name, out var allowed) ? allowed : 0;
                var found = 0;

                foreach (Match match in UncappedLoad.Matches(text))
                {
                    sites++;
                    var first = match.Groups["first"].Value;

                    // A local named like a reader is the capped form.
                    if (first.Contains("reader", StringComparison.OrdinalIgnoreCase)
                        || first.Contains("xmlReader", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    found++;
                    if (found <= exempt)
                        continue;

                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{name}:{line}: {match.Value}(...)");
                }
            }
        }

        // Non-vacuity: the scan must actually have reached the layers and matched loads, or an empty
        // offender list means the pattern drifted rather than the code being clean.
        scanned.Should().BeGreaterThan(200, "the layer list or locator has drifted");
        sites.Should().BeGreaterThan(8, "the Load/Parse pattern has drifted");

        foreach (var offender in offenders)
            output.WriteLine(offender);

        offenders.Should().BeEmpty(
            $"XDocument/XElement Load and Parse build default settings with NO character cap, so a "
            + $"package part is unbounded at the point of parse. Create an XmlReader with "
            + $"SecureXmlReaderSettings.Create() and load from that. Found {offenders.Count} of "
            + $"{sites} load sites in {scanned} files:\n" + string.Join("\n", offenders));
    }
}
