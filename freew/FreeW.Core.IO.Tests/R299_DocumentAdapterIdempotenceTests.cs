using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r299: extends r298's idempotence property from FreeX's spreadsheet adapters to FreeW's document
/// adapters, where the reader and writer are entirely separate code.
///
/// <para>Five of six reproduce themselves byte-for-byte: DOCX, ODT, RTF, plain text and WordML. HTML
/// does not -- and the difference says something specific. A paragraph with <c>StyleId =
/// "Heading1"</c> and no direct formatting is written as <c>&lt;h1&gt;text&lt;/h1&gt;</c>; the reader
/// takes the bold that <c>h1</c> implies and materialises it as DIRECT run formatting, so the second
/// save emits <c>&lt;h1&gt;&lt;strong&gt;text&lt;/strong&gt;&lt;/h1&gt;</c>.</para>
///
/// <para>It converges: 326 bytes, then 343, then 343 forever. So this is a one-time normalisation
/// rather than the unbounded growth that would make repeated save-open cycles pathological -- which
/// is the first thing worth knowing about any non-idempotent format, and the reason these tests
/// assert the fixed point rather than just the inequality.</para>
///
/// <para>Deliberately not "fixed". For HTML written elsewhere, reading the implied bold PRESERVES
/// the author's appearance, which is the right call for an import path; it is only redundant for
/// HTML this adapter itself produced. Changing it would trade fidelity on foreign documents for
/// byte-stability on our own. The cost -- style-implied formatting becoming direct formatting, so a
/// later edit to Heading1 no longer unbolds the run -- is recorded here instead.</para>
/// </summary>
public sealed class R299_DocumentAdapterIdempotenceTests
{
    private static TextDocument Sample()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world"));
        document.Blocks.Add(new Paragraph("second paragraph") { StyleId = "Heading1" });
        return document;
    }

    private static byte[] Save(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Load(IDocumentFileAdapter adapter, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return adapter.Load(stream);
    }

    private static IDocumentFileAdapter Make(string key) => key switch
    {
        "docx" => new DocxFileAdapter(),
        "odt" => new OdtFileAdapter(),
        "rtf" => new RtfFileAdapter(),
        "txt" => new PlainTextFileAdapter(),
        "wordml" => new Wordml2003FileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    [InlineData("rtf")]
    [InlineData("txt")]
    [InlineData("wordml")]
    public void SavingTwiceProducesTheSameBytes(string key)
    {
        var adapter = Make(key);
        var first = Save(adapter, Sample());
        var second = Save(adapter, Load(adapter, first));

        second.Should().Equal(first,
            $"{key} must reproduce its own output: a differing second save means the load lost "
            + "something the save then wrote differently, or invented something that was not there");
    }

    /// <summary>
    /// HTML is the exception, and the property that matters is that it CONVERGES. Unbounded growth
    /// across repeated open/save cycles would be the real defect; a single normalisation is not.
    /// </summary>
    [Fact]
    public void HtmlReachesAFixedPointAfterOneRoundTrip()
    {
        var adapter = new HtmlFileAdapter();
        var document = Sample();
        var sizes = new List<int>();

        for (var i = 0; i < 4; i++)
        {
            var bytes = Save(adapter, document);
            sizes.Add(bytes.Length);
            document = Load(adapter, bytes);
        }

        sizes.Skip(1).Distinct().Should().ContainSingle(
            "the second save onwards must be stable. Growth that compounded would make every "
            + "open-and-save cycle inflate the file, which is the difference between a normalisation "
            + "and a defect");
    }

    /// <summary>
    /// The specific normalisation, named so it is a known cost rather than a mystery: the reader
    /// takes the bold implied by h1 and writes it back as direct formatting.
    /// </summary>
    [Fact]
    public void HtmlMaterialisesHeadingImpliedBoldAsDirectFormatting()
    {
        var adapter = new HtmlFileAdapter();
        var first = Save(adapter, Sample());
        var second = Save(adapter, Load(adapter, first));

        var firstText = System.Text.Encoding.UTF8.GetString(first);
        var secondText = System.Text.Encoding.UTF8.GetString(second);

        firstText.Should().NotContain("<strong>",
            "the source paragraph carries a heading STYLE and no direct bold");
        secondText.Should().Contain("<strong>",
            "reading h1 back materialises its implied bold onto the run. That preserves appearance "
            + "for HTML written elsewhere, which is why it is not simply removed -- but it does mean "
            + "a later edit to the Heading1 style no longer unbolds this text");
    }
}
