using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression coverage for <see cref="Wordml2003Reader"/>'s run-formatting reader (the legacy Word 2003
/// "XML Document" import path). Mirrors <c>TypographyRoundTripTests.FontSizeAndKerning_ExplicitZero_IsPreservedNotDefaulted</c>
/// / <c>..._Absent_IsStillNull</c> in this project, which cover the analogous <see cref="DocxReader"/> path:
/// <c>w:sz</c> is a half-points attribute, and an explicit <c>w:val="0"</c> is a real (if degenerate) value
/// that must not be folded into "attribute absent".
/// </summary>
public class Wordml2003ReaderTests
{
    private static readonly XNamespace W = Wordml2003Reader.W;

    private static double? ReadFirstRunFontSizePt(string rPrInnerXml)
    {
        var xml =
            "<?xml version=\"1.0\"?>" +
            "<w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\">" +
            "<w:body><w:p><w:r><w:rPr>" + rPrInnerXml + "</w:rPr><w:t>x</w:t></w:r></w:p></w:body>" +
            "</w:wordDocument>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var document = Wordml2003Reader.Read(stream);
        var run = document.Blocks.OfType<Paragraph>().First().Runs.First();
        return run.Formatting.FontSizePt;
    }

    /// <summary>
    /// Regression: ReadRunFormatting used to compute half-points itself via
    /// <c>hp &gt; 0 ? hp / 2.0 : null</c>, which silently discarded an explicit
    /// <c>&lt;w:sz w:val="0"/&gt;</c> and fell back to the document default font size instead of the
    /// literal (if degenerate) explicit value of 0pt.
    /// </summary>
    [Fact]
    public void FontSize_ExplicitZero_IsPreservedNotDefaulted()
    {
        var fontSizePt = ReadFirstRunFontSizePt("<w:sz w:val=\"0\"/>");

        fontSizePt.Should().Be(0.0, "an explicit w:sz val=\"0\" is a real value, not an absent attribute");
    }

    /// <summary>
    /// Sibling no-regression: when w:sz is genuinely absent, the run must still resolve to a null
    /// (inherit the document default), not to 0.
    /// </summary>
    [Fact]
    public void FontSize_Absent_IsStillNull()
    {
        var fontSizePt = ReadFirstRunFontSizePt("<w:b/>");

        fontSizePt.Should().BeNull("w:sz was never written, so there is no explicit size to recover");
    }

    /// <summary>Non-degenerate control case: a genuine explicit size still round-trips correctly.</summary>
    [Fact]
    public void FontSize_ExplicitNonZero_ReadsAsHalfOfSzValue()
    {
        var fontSizePt = ReadFirstRunFontSizePt("<w:sz w:val=\"24\"/>");

        fontSizePt.Should().Be(12.0, "w:sz is in half-points");
    }
}
