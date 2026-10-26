using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r603: no integer parsed OUT OF A FILE FORMAT may use the ambient culture.
///
/// <para>Two-argument <c>int.TryParse(s, out v)</c> resolves to CurrentCulture with
/// <see cref="System.Globalization.NumberStyles.Integer"/>, so the accepted negative sign is that
/// culture's <c>NegativeSign</c>. 57 of .NET's 890 cultures -- every Arabic, Hebrew, Persian, Urdu,
/// Pashto, Kurdish and Sindhi locale -- spell it with an embedded direction mark (ar-SA is
/// U+061C '-', fa-IR is U+200E U+2212), and .NET does not forgive a bare ASCII hyphen for those.
/// OOXML, ODF and HTML all write ASCII '-', so on those machines every negative integer attribute
/// silently reverted to its default. Proven in FreeP, where a run tracking of spc="-150" read back
/// as 0 under fa-IR and ar-SA while de-DE, fr-FR, tr-TR and sv-SE all passed.</para>
///
/// <para>That last clause is why this is a source contract and not another culture case: the three
/// existing censuses (r391 FreeP, r392 FreeX, r396 FreeW) all vary the DECIMAL SEPARATOR, and no
/// separator culture can see a sign defect. A behavioural test only covers the attributes its
/// fixture happens to carry; this covers every parse site in every file-format reader, including
/// ones added later.</para>
///
/// <para>Scope is the IO assemblies only. Parsing a number a USER typed is a different question
/// with the opposite answer -- FreeX's delimited-text import deliberately parses under a chosen
/// culture, matching Excel -- so shell, dialog and command code is out of scope here.</para>
/// </summary>
public sealed class R603_FileFormatIntegerParsesAreCultureInvariantTests(ITestOutputHelper output)
{
    private static readonly string[] IoDirectories =
    [
        Path.Combine("src", "FreeX.Core.IO"),
        Path.Combine("freew", "FreeW.Core.IO"),
        Path.Combine("freep", "FreeP.Core.IO"),
        Path.Combine("shared", "Free.Shared.IO"),
        Path.Combine("shared", "Free.Shared.Drawing"),
    ];

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    private static IEnumerable<string> IoSources()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");

        foreach (var relative in IoDirectories)
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

                yield return file;
            }
        }
    }

    // The call, up to and including the "out" that ends its argument list. Argument text cannot
    // contain a ';' or a nested TryParse, which keeps the lazy span from running past the call.
    private static readonly Regex IntegerTryParse = new(
        @"\b(?:int|long|uint|short|ulong|ushort)\.TryParse\s*\((?<args>[^;]{0,300}?)\bout\s",
        RegexOptions.Compiled);

    [Fact]
    public void NoFileFormatIntegerIsParsedUnderTheAmbientCulture()
    {
        var offenders = new List<string>();
        var scanned = 0;
        var sites = 0;

        foreach (var file in IoSources())
        {
            scanned++;
            var text = StripComments(File.ReadAllText(file));

            foreach (Match match in IntegerTryParse.Matches(text))
            {
                sites++;
                var args = match.Groups["args"].Value;

                // An explicit provider of any kind is a deliberate choice; only the two-argument
                // form -- which silently inherits CurrentCulture -- is the defect.
                if (args.Contains("Culture", StringComparison.Ordinal)
                    || args.Contains("NumberStyles", StringComparison.Ordinal)
                    || args.Contains("NumberFormatInfo", StringComparison.Ordinal)
                    || args.Contains("Provider", StringComparison.Ordinal)
                    || args.Contains("provider", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}: {Regex.Replace(args.Trim(), @"\s+", " ")}");
            }
        }

        // Non-vacuity: the scan must actually have reached the readers and found parse sites, or an
        // empty offender list means the pattern stopped matching rather than the code being clean.
        Assert.True(scanned > 200, $"scanned only {scanned} IO sources; the locator or the directory list has drifted");
        Assert.True(sites > 200, $"matched only {sites} integer TryParse sites; the pattern has drifted");

        foreach (var offender in offenders.Take(40))
            output.WriteLine(offender);

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} file-format integer parses inherit CurrentCulture (of {sites} sites in " +
            $"{scanned} files); pass NumberStyles.Integer, CultureInfo.InvariantCulture:\n" +
            string.Join("\n", offenders.Take(40)));
    }
}
