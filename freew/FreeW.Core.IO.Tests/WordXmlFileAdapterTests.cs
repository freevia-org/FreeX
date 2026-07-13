using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Flat OPC (.xml) round-trips through the same engine as .docx via an in-memory transcode. Word 2003 WordML
/// shares the extension but is a different format; <see cref="WordXmlFileAdapter.Load"/> sniffs the root and
/// dispatches it to the read-only <see cref="Wordml2003Reader"/> (see <see cref="Wordml2003ReaderTests"/>),
/// while an unrecognised root still fails with a clear message.
/// </summary>
public class WordXmlFileAdapterTests
{
    private static TextDocument SampleDoc(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    [Fact]
    public void RoundTrip_PreservesBodyText()
    {
        var adapter = new WordXmlFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(SampleDoc("Flat OPC one", "Flat OPC two"), ms);
        ms.Position = 0;

        var text = adapter.Load(ms).Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToList();
        text.Should().Contain("Flat OPC one");
        text.Should().Contain("Flat OPC two");
    }

    [Fact]
    public void Save_ProducesFlatOpcPackage_WithMsoApplicationPi()
    {
        var adapter = new WordXmlFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(SampleDoc("hello"), ms);

        var xml = Encoding.UTF8.GetString(ms.ToArray());
        xml.Should().Contain("<pkg:package");
        xml.Should().Contain("mso-application");
        xml.Should().Contain("progid=\"Word.Document\"");
        xml.Should().Contain("/word/document.xml");
    }

    [Fact]
    public void Wordml2003Adapter_SaveProducesWordDocumentRootAndReloadsSupportedSubset()
    {
        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        adapter.Save(SampleDoc("WordML one", "WordML two"), ms);

        var xml = Encoding.UTF8.GetString(ms.ToArray());
        xml.Should().Contain("<w:wordDocument");
        xml.Should().Contain("http://schemas.microsoft.com/office/word/2003/wordml");

        ms.Position = 0;
        var text = adapter.Load(ms).Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToList();
        text.Should().Contain("WordML one");
        text.Should().Contain("WordML two");
    }

    [Fact]
    public void Load_DispatchesWord2003Wordml_ToReadOnlyReader()
    {
        const string wordml =
            "<?xml version=\"1.0\"?>" +
            "<w:wordDocument xmlns:w=\"http://schemas.microsoft.com/office/word/2003/wordml\">" +
            "<w:body><w:p><w:r><w:t>2003 body</w:t></w:r></w:p></w:body>" +
            "</w:wordDocument>";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wordml));

        var document = new WordXmlFileAdapter().Load(ms);

        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).Should().Contain("2003 body");
    }

    [Fact]
    public void Load_RejectsUnrecognisedRoot_WithClearMessage()
    {
        const string notWord = "<?xml version=\"1.0\"?><workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" />";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(notWord));

        var act = () => new WordXmlFileAdapter().Load(ms);
        act.Should().Throw<InvalidDataException>().WithMessage("*neither <pkg:package>*");
    }
}
