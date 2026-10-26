using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r547: since .NET Core stopped throwing on overflow, <c>double.TryParse</c> returns TRUE with
/// +/-Infinity for a literal like <c>1e400</c>. FreeW's importers read lengths and numbers straight
/// out of untrusted documents and clipboard HTML with that call, so an ordinary-looking exponent --
/// no need for the literal text "Infinity" -- put a non-finite value into the document model.
///
/// <para>Each site already had a contract for input it cannot represent: return false, return null,
/// skip the declaration, or fall back to a default. The fix applies that existing contract to
/// non-finite rather than inventing a new policy, so an unusable length is ignored exactly as an
/// unparseable one always was.</para>
/// </summary>
public class R547_NonFiniteNumbersDoNotEnterTheModelTests
{
    [Theory]
    [InlineData("1e400pt")]
    [InlineData("-1e400pt")]
    [InlineData("1e400px")]
    [InlineData("1e400")]
    public void Overflowing_css_lengths_are_reported_as_unparseable(string css)
    {
        Assert.False(HtmlCssFormatting.TryParseLengthPt(css, out _));
    }

    [Theory]
    [InlineData("12pt", 12.0)]
    [InlineData("16px", 12.0)]
    public void Ordinary_css_lengths_still_parse(string css, double expected)
    {
        // Non-vacuity: the guard must reject only what it cannot represent. 16px x 0.75 = 12pt.
        Assert.True(HtmlCssFormatting.TryParseLengthPt(css, out var pt));
        Assert.Equal(expected, pt, 6);
    }

    [Fact]
    public void Imported_html_does_not_carry_an_infinite_font_size()
    {
        var html = "<html><body><p><span style=\"font-size:1e400pt\">x</span></p></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var run = HtmlFileAdapter.Filtered().Load(stream).Paragraphs.First().Runs.First();

        // The declaration is ignored, so the run inherits its size rather than holding Infinity.
        Assert.True(run.Formatting.FontSizePt is null || double.IsFinite(run.Formatting.FontSizePt.Value));
    }

    [Fact]
    public void Imported_html_still_honours_a_real_font_size()
    {
        var html = "<html><body><p><span style=\"font-size:18pt\">x</span></p></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var run = HtmlFileAdapter.Filtered().Load(stream).Paragraphs.First().Runs.First();

        Assert.Equal(18.0, run.Formatting.FontSizePt!.Value, 6);
    }

    [Fact]
    public void Odt_page_geometry_rejects_an_overflowing_length()
    {
        // Round-trips through the real adapter, then rewrites one attribute the way a hostile or
        // simply broken producer could, so the reader is exercised rather than a private helper.
        var adapter = OdtFileAdapter.Odt();
        using var saved = new MemoryStream();
        adapter.Save(TextDocument.CreateEmpty(), saved);

        var patched = RewriteEntry(saved.ToArray(), "styles.xml", xml =>
            System.Text.RegularExpressions.Regex.Replace(
                xml, "fo:page-width=\"[^\"]*\"", "fo:page-width=\"1e400cm\""));

        using var input = new MemoryStream(patched);
        var page = adapter.Load(input).Sections.First().Page;

        Assert.True(double.IsFinite(page.WidthPt), "an overflowing fo:page-width became " + page.WidthPt);
        Assert.True(page.WidthPt > 0, "rejecting the bad width must leave the default, not zero");
    }

    private static byte[] RewriteEntry(byte[] package, string entryName, Func<string, string> rewrite)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package, 0, package.Length);
        buffer.Position = 0;

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(entryName)!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = reader.ReadToEnd();

            var updated = rewrite(xml);
            Assert.NotEqual(xml, updated);

            entry.Delete();
            var replacement = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(replacement.Open());
            writer.Write(updated);
        }

        return buffer.ToArray();
    }
}
