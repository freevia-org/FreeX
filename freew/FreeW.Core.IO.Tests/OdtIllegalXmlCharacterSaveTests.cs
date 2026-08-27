using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The .odt writer hands three separately-built documents -- content.xml, styles.xml and meta.xml --
/// to OpenDocumentPackageWriter, which serializes each through <c>XDocument.Save(XmlWriter)</c>. ODF
/// is XML 1.0, so one C0 control code or lone UTF-16 surrogate in run text or a document property
/// aborted the WHOLE File > Save As > OpenDocument Text with an ArgumentException and no file
/// written. The user lost the save, not the character.
/// <para>
/// None of this is guarded in <c>OdtFileAdapter</c> itself: the sanitize happens once inside
/// <c>OpenDocumentPackageWriter.WriteXmlEntry</c>, so it covers every part this adapter writes today
/// and any part a future ODF writer adds. These cases go through the real Save gesture and reload the
/// result, so a regression fails on the crash rather than on a substring assertion.
/// </para>
/// </summary>
public class OdtIllegalXmlCharacterSaveTests
{
    private const string Control = "\u000b";
    private const string LoneHighSurrogate = "\ud83d";

    [Fact]
    public void SaveAs_WithControlCharacterInParagraphText_SucceedsAndStripsTheCharacter()
    {
        var reloaded = SaveAndReload(DocOf("Total" + Control + "Revenue"));

        Lines(reloaded).Should().Contain("TotalRevenue");
    }

    [Fact]
    public void SaveAs_WithLoneSurrogateInParagraphText_SucceedsAndStripsTheCharacter()
    {
        var reloaded = SaveAndReload(DocOf("Q" + LoneHighSurrogate + "1"));

        Lines(reloaded).Should().Contain("Q1");
    }

    /// <summary>
    /// meta.xml is built from the document properties, a separate XDocument from content.xml -- the
    /// part-by-part sanitize has to cover it too, not just the body.
    /// </summary>
    [Fact]
    public void SaveAs_WithControlCharacterInDocumentTitle_SucceedsAndStripsTheCharacter()
    {
        var document = DocOf("Body");
        document.Properties.Title = "Annual" + Control + "Report";
        document.Properties.Subject = "Fiscal" + LoneHighSurrogate + " year";

        var reloaded = SaveAndReload(document);

        reloaded.Properties.Title.Should().Be("AnnualReport");
        reloaded.Properties.Subject.Should().Be("Fiscal year");
    }

    /// <summary>
    /// No-regression guard: sanitizing must leave text XML 1.0 can represent alone, including a
    /// well-formed surrogate PAIR -- the input a naive strip-above-the-BMP fix would corrupt.
    /// </summary>
    [Fact]
    public void SaveAs_WithOrdinaryText_RoundTripsUnchanged()
    {
        var document = DocOf("Total Revenue", "caf\u00e9 \ud83d\ude00 <&>");
        document.Properties.Title = "Annual Report \ud83d\ude00";

        var reloaded = SaveAndReload(document);

        Lines(reloaded).Should().Contain("Total Revenue");
        Lines(reloaded).Should().Contain("caf\u00e9 \ud83d\ude00 <&>");
        reloaded.Properties.Title.Should().Be("Annual Report \ud83d\ude00");
    }

    private static TextDocument DocOf(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static TextDocument SaveAndReload(TextDocument document)
    {
        using var saved = new MemoryStream();
        OdtFileAdapter.Odt().Save(document, saved);
        saved.Position = 0;
        return OdtFileAdapter.Odt().Load(saved);
    }

    private static string[] Lines(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();
}
