using System.IO;
using System.IO.Compression;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The fontTable's w:family is read as a render-only hint: Word substitutes a missing face by its
/// generic family, so the renderer needs it to pick the same fallback face. It is never written back.
/// </summary>
public sealed class FontTableGenericFamilyReaderTests
{
    [Fact]
    public void FontTableFamilies_AreReadAsRenderOnlyHints()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("text"));
        using var stream = new MemoryStream();
        DocxWriter.Write(source, stream);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("word/fontTable.xml")?.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("word/fontTable.xml").Open());
            writer.Write(
                "<w:fonts xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
                + "<w:font w:name=\"B Lotus\"><w:charset w:val=\"B2\"/><w:family w:val=\"auto\"/></w:font>"
                + "<w:font w:name=\"Traditional Arabic\"><w:family w:val=\"roman\"/></w:font>"
                + "<w:font w:name=\"No Family\"><w:charset w:val=\"00\"/></w:font>"
                + "</w:fonts>");
        }

        stream.Position = 0;
        var document = DocxReader.Read(stream);

        document.FontGenericFamilies.Should().HaveCount(2);
        document.FontGenericFamilies["b lotus"].Should().Be("auto");
        document.FontGenericFamilies["Traditional Arabic"].Should().Be("roman");
        document.EmbeddedFonts.Should().BeEmpty();
    }
}
