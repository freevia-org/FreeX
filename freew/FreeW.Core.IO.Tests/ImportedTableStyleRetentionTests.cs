using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class ImportedTableStyleRetentionTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void CustomTableStyle_PreservesReferenceAndConditionalBandsAcrossOutsideEdit()
    {
        using var source = BuildPackage();
        var document = DocxReader.Read(source);

        var table = document.Blocks[0].Should().BeOfType<Table>().Which;
        table.TableStyleId.Should().Be("CustomBlueGrid");
        var style = document.Styles["CustomBlueGrid"];
        style.Type.Should().Be(StyleType.Table);
        style.PreservedTableStyleXml.Should().Contain("firstRow").And.Contain("band1Horz");

        ((Paragraph)document.Blocks[1]).Runs[0].Text = "Edited outside";
        var first = Write(document);
        AssertPackage(first);

        var reopened = DocxReader.Read(new MemoryStream(first));
        reopened.Blocks[0].Should().BeOfType<Table>().Which.TableStyleId.Should().Be("CustomBlueGrid");
        reopened.Styles["CustomBlueGrid"].PreservedTableStyleXml.Should().Contain("band1Horz");

        var second = Write(reopened);
        AssertPackage(second);
    }

    private static void AssertPackage(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var document = ReadXml(zip, "word/document.xml");
        document.Descendants(W + "tblStyle").Single().Attribute(W + "val")!.Value.Should().Be("CustomBlueGrid");
        document.Descendants(W + "p").Last().Value.Should().Be("Edited outside");

        var styles = ReadXml(zip, "word/styles.xml");
        var customStyles = styles.Descendants(W + "style")
            .Where(element => element.Attribute(W + "styleId")?.Value == "CustomBlueGrid")
            .ToList();
        customStyles.Should().ContainSingle();
        var customStyle = customStyles[0];
        customStyle.Attribute(W + "customStyle")!.Value.Should().Be("1");
        customStyle.Element(W + "uiPriority")!.Attribute(W + "val")!.Value.Should().Be("73");
        customStyle.Element(W + "qFormat").Should().NotBeNull();

        var bands = customStyle.Elements(W + "tblStylePr")
            .ToDictionary(element => element.Attribute(W + "type")!.Value);
        bands.Keys.Should().BeEquivalentTo("firstRow", "band1Horz");
        bands["firstRow"].Descendants(W + "shd").Single().Attribute(W + "fill")!.Value.Should().Be("4472C4");
        bands["band1Horz"].Descendants(W + "shd").Single().Attribute(W + "fill")!.Value.Should().Be("DDEBF7");
    }

    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:tbl>
                      <w:tblPr>
                        <w:tblStyle w:val="CustomBlueGrid"/>
                        <w:tblLook w:firstRow="1" w:noHBand="0"/>
                      </w:tblPr>
                      <w:tr><w:tc><w:p><w:r><w:t>Styled cell</w:t></w:r></w:p></w:tc></w:tr>
                    </w:tbl>
                    <w:p><w:r><w:t>Outside paragraph</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            Add(zip, "word/styles.xml", """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                  <w:style w:type="table" w:customStyle="1" w:styleId="CustomBlueGrid">
                    <w:name w:val="Custom Blue Grid"/>
                    <w:uiPriority w:val="73"/>
                    <w:qFormat/>
                    <w:tblPr><w:tblBorders><w:top w:val="single" w:sz="8" w:color="4472C4"/></w:tblBorders></w:tblPr>
                    <w:tblStylePr w:type="firstRow"><w:rPr><w:b/></w:rPr><w:tcPr><w:shd w:val="clear" w:fill="4472C4"/></w:tcPr></w:tblStylePr>
                    <w:tblStylePr w:type="band1Horz"><w:tcPr><w:shd w:val="clear" w:fill="DDEBF7"/></w:tcPr></w:tblStylePr>
                  </w:style>
                </w:styles>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
