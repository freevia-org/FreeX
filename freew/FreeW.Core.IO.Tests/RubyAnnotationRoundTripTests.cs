using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class RubyAnnotationRoundTripTests
{
    [Fact]
    public void RubyRun_BaseTextFallbackTracksIncrementalBaseFragments()
    {
        var annotation = new RubyAnnotation();
        var run = Run.FromRuby(annotation);

        annotation.BaseFragments.Add(new RubyTextFragment("漢", RunFormatting.Default));
        annotation.BaseFragments.Add(new RubyTextFragment("字", RunFormatting.Default));

        run.Text.Should().Be("漢字");
    }

    [Fact]
    public void RubyAnnotation_PreservesFormattedBaseGuideAndProperties()
    {
        using var input = BuildPackage();
        var paragraph = DocxReader.Read(input).Paragraphs.Single();

        paragraph.PlainText.Should().Be("Read 漢字 now");
        var rubyRun = paragraph.Runs.Single(run => run.Ruby is not null);
        rubyRun.Text.Should().Be("漢字");
        rubyRun.Ruby!.Alignment.Should().Be(RubyAlignment.Right);
        rubyRun.Ruby.PhoneticSizeHalfPoints.Should().Be(16);
        rubyRun.Ruby.RaiseHalfPoints.Should().Be(8);
        rubyRun.Ruby.BaseFragments.Select(fragment => fragment.Text).Should().Equal("漢", "字");
        rubyRun.Ruby.BaseFragments[0].Formatting.Bold.Should().BeTrue();
        rubyRun.Ruby.BaseFragments[1].Formatting.ColorHex.Should().Be("#0070C0");
        rubyRun.Ruby.PhoneticFragments.Select(fragment => fragment.Text).Should().Equal("かんじ");
        rubyRun.Ruby.PhoneticFragments[0].Formatting.Italic.Should().BeTrue();

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(new TextDocument { Blocks = { paragraph } }, output);
            rewritten = output.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
        {
            var ruby = XDocument.Load(entry).Descendants(Ooxml.W + "ruby").Single();
            var rubyPr = ruby.Element(Ooxml.W + "rubyPr")!;
            rubyPr.Element(Ooxml.W + "rubyAlign")!.Attribute(Ooxml.W + "val")!.Value.Should().Be("right");
            rubyPr.Element(Ooxml.W + "hps")!.Attribute(Ooxml.W + "val")!.Value.Should().Be("16");
            rubyPr.Element(Ooxml.W + "hpsRaise")!.Attribute(Ooxml.W + "val")!.Value.Should().Be("8");
            ruby.Element(Ooxml.W + "rt")!.Descendants(Ooxml.W + "t").Select(text => text.Value).Should().Equal("かんじ");
            ruby.Element(Ooxml.W + "rubyBase")!.Descendants(Ooxml.W + "t").Select(text => text.Value).Should().Equal("漢", "字");
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten)).Paragraphs.Single();
        var rereadRuby = reread.Runs.Single(run => run.Ruby is not null).Ruby!;
        reread.PlainText.Should().Be("Read 漢字 now");
        rereadRuby.Alignment.Should().Be(RubyAlignment.Right);
        rereadRuby.PhoneticSizeHalfPoints.Should().Be(16);
        rereadRuby.RaiseHalfPoints.Should().Be(8);
        rereadRuby.BaseFragments.Select(fragment => fragment.Text).Should().Equal("漢", "字");
        rereadRuby.BaseFragments[0].Formatting.Bold.Should().BeTrue();
        rereadRuby.BaseFragments[1].Formatting.ColorHex.Should().Be("#0070C0");
        rereadRuby.PhoneticFragments.Select(fragment => fragment.Text).Should().Equal("かんじ");
        rereadRuby.PhoneticFragments[0].Formatting.Italic.Should().BeTrue();
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
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r><w:t>Read </w:t></w:r>
                      <w:ruby>
                        <w:rubyPr><w:rubyAlign w:val="right"/><w:hps w:val="16"/><w:hpsRaise w:val="8"/></w:rubyPr>
                        <w:rt><w:r><w:rPr><w:i/></w:rPr><w:t>かんじ</w:t></w:r></w:rt>
                        <w:rubyBase><w:r><w:rPr><w:b/></w:rPr><w:t>漢</w:t></w:r><w:r><w:rPr><w:color w:val="0070C0"/></w:rPr><w:t>字</w:t></w:r></w:rubyBase>
                      </w:ruby>
                      <w:r><w:t> now</w:t></w:r>
                    </w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
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
