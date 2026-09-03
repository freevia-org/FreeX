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

    [Fact]
    public void EveryXmlReaderSettingsInThePackageLayerSetsBothProtections()
    {
        var root = RepositoryRoot();
        var layer = Path.Combine(root, "src", "FreeX.Core.IO");
        var offenders = new List<string>();
        var examined = 0;

        foreach (var file in Directory.EnumerateFiles(layer, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("new XmlReaderSettings", StringComparison.Ordinal))
                    continue;

                examined++;
                var initializer = Initializer(lines, i);

                var hasDtd = Regex.IsMatch(initializer, @"DtdProcessing\s*=\s*DtdProcessing\.(Prohibit|Ignore)");
                var hasCap = initializer.Contains("MaxCharactersInDocument", StringComparison.Ordinal);
                if (hasDtd && hasCap)
                    continue;

                offenders.Add(
                    $"{Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')}:{i + 1} -- "
                    + $"DtdProcessing={(hasDtd ? "set" : "MISSING")}, MaxCharactersInDocument={(hasCap ? "set" : "MISSING")}");
            }
        }

        examined.Should().BeGreaterThan(8,
            "the scan must find the hand-rolled settings objects; a collapsed count means the pattern "
            + "stopped matching and this passed while checking nothing");

        offenders.Should().BeEmpty(
            "prefer SecureXmlReaderSettings.Create(), which sets DtdProcessing, XmlResolver and the "
            + "character cap together. Setting only DtdProcessing hardens against entity attacks "
            + "while leaving the part unbounded, and the zip's declared sizes cannot be trusted to "
            + "bound it -- see the comment in XlsxPivotCacheReader.\n" + string.Join("\n", offenders));
    }

    private static string Initializer(string[] lines, int start)
    {
        var text = string.Empty;
        var depth = 0;
        var opened = false;
        for (var i = start; i < Math.Min(start + 15, lines.Length); i++)
        {
            text += lines[i];
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            if (lines[i].Contains('{', StringComparison.Ordinal))
                opened = true;
            if (opened && depth <= 0)
                break;
        }

        return text;
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
