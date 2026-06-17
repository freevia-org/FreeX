using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reader coverage for legacy VML pictures (w:pict/v:shape/v:imagedata), used by older Word documents
/// instead of DrawingML. These were skipped by the DrawingML-only picture reader, so the images vanished.
/// </summary>
public class VmlImageReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Vns = "urn:schemas-microsoft-com:vml";
    private const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static void AddEntry(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static TextDocument ReadVmlDoc()
    {
        var body =
            $"<w:p><w:r><w:pict><v:shape style=\"width:60pt;height:40pt\">" +
            "<v:imagedata r:id=\"rId7\"/></v:shape></w:pict></w:r></w:p>";
        var documentXml =
            $"<w:document xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\" xmlns:v=\"{Vns}\"><w:body>{body}</w:body></w:document>";
        var relsXml =
            $"<Relationships xmlns=\"{RelNs}\">" +
            "<Relationship Id=\"rId7\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>" +
            "</Relationships>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "word/document.xml", Encoding.UTF8.GetBytes(documentXml));
            AddEntry(zip, "word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(relsXml));
            // The reader keeps the media bytes verbatim (decoding happens in the view), so any bytes do.
            AddEntry(zip, "word/media/image1.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Fact]
    public void VmlPicture_ReadsAsInlineImage()
    {
        var doc = ReadVmlDoc();
        var image = doc.Blocks.OfType<Paragraph>().First().Runs
            .Select(r => r.Image).FirstOrDefault(i => i is not null);

        Assert.NotNull(image);
        Assert.Equal(60, image!.WidthPt, 1);   // from the VML shape's CSS style
        Assert.Equal(40, image.HeightPt, 1);
        Assert.NotEmpty(image.Bytes);
    }
}
