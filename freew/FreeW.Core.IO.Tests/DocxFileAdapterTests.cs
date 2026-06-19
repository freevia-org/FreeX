using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

public class DocxFileAdapterTests
{
    [Fact]
    public void RoundTrip_PreservesParagraphText()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello adapter"));
        document.Blocks.Add(new Paragraph("Second paragraph"));

        var adapter = new DocxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);

        var text = reloaded.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToList();
        text.Should().Contain("Hello adapter");
        text.Should().Contain("Second paragraph");
    }

    [Fact]
    public void Adapter_ExposesDocxOpenSaveFormat()
    {
        IDocumentFileAdapter adapter = new DocxFileAdapter();

        adapter.Formats.Should().ContainSingle();
        var format = adapter.Formats[0];
        format.Extension.Should().Be(".docx");
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeTrue();
        format.OpensAsTemplate.Should().BeFalse();
    }
}
