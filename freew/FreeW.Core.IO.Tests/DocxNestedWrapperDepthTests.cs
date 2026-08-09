using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reading a body block recurses one call frame per w:sdt / w:customXml wrapper level, and those
/// wrappers nest arbitrarily in the file format. Without a depth cap, a small crafted .docx
/// overflows the stack on open — and StackOverflowException is uncatchable, so it kills the process
/// instead of surfacing as a load error. Verified to abort the test host when the cap is removed.
/// </summary>
public sealed class DocxNestedWrapperDepthTests
{
    [Fact]
    public void Read_DeeplyNestedContentControls_OpensInsteadOfOverflowingTheStack()
    {
        using var docx = BuildDocxWithNestedWrappers("w:sdt", "w:sdtContent", 20_000);

        var document = DocxReader.Read(docx);

        document.Should().NotBeNull();
    }

    [Fact]
    public void Read_DeeplyNestedCustomXml_OpensInsteadOfOverflowingTheStack()
    {
        using var docx = BuildDocxWithNestedWrappers("w:customXml", null, 20_000);

        var document = DocxReader.Read(docx);

        document.Should().NotBeNull();
    }

    [Fact]
    public void Read_ShallowNestedContentControls_StillReadsTheWrappedText()
    {
        using var docx = BuildDocxWithNestedWrappers("w:sdt", "w:sdtContent", 3);

        DocxReader.Read(docx).PlainText.Should().Contain("wrapped");
    }

    private static MemoryStream BuildDocxWithNestedWrappers(string wrapper, string? contentWrapper, int depth)
    {
        var body = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            body.Append('<').Append(wrapper).Append('>');
            if (contentWrapper is not null)
                body.Append('<').Append(contentWrapper).Append('>');
        }

        body.Append("<w:p><w:r><w:t>wrapped</w:t></w:r></w:p>");

        for (var i = 0; i < depth; i++)
        {
            if (contentWrapper is not null)
                body.Append("</").Append(contentWrapper).Append('>');
            body.Append("</").Append(wrapper).Append('>');
        }

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);

            AddText(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            AddText(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """);

            AddText(
                zip,
                "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
                    + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                    + "<w:body>"
                    + body
                    + "</w:body></w:document>");
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddText(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
