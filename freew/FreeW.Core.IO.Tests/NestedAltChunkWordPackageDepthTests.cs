using System.IO;
using System.IO.Compression;
using System.Text;
using Free.Shared.Opc;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// round-171 / shared-open-error-paths F1: <c>DocxReader.TryMaterializeAltChunk</c> resolves a body-level
/// altChunk whose target part is itself a "nested Word package" (the content type Word uses for a
/// document embedded inside another via Insert &gt; Object &gt; Text from File) by calling the public
/// <see cref="DocxReader.Read(Stream)"/> entry point recursively. Nothing bounded that recursion: a chain
/// of N nested Word packages, each one's own altChunk pointing at the next, drove the process straight
/// into an uncatchable <see cref="StackOverflowException"/> -- confirmed with a standalone probe host
/// (outside this repo, deleted after use) built against the unfixed DLL: depth=200 (265 KB) opened fine,
/// depth=1000 (1.51 MB) took down the whole probe process with "Stack overflow." and a repeating
/// DocxReader.Read -&gt; TryMaterializeAltChunk -&gt; AddBodyBlock stack trace. That reproduction cannot be
/// re-run in this xUnit host without killing the whole test run, so these tests instead pin the bounded
/// behaviour the fix introduces: the load must fail cleanly with a catchable, clearly worded exception the
/// moment the nesting exceeds the supported depth, well before the stack is ever at risk.
/// </summary>
public sealed class NestedAltChunkWordPackageDepthTests
{
    private const string NestedWordPackageContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    [Fact]
    public void Read_DeeplyNestedWordPackageAltChunkChain_FailsCleanlyInsteadOfOverflowingTheStack()
    {
        // 50 levels is comfortably past any real "document embedded in a document" use, and well past
        // the fix's cap -- but nowhere near the ~1000 levels the unbounded reader needed to actually
        // overflow the stack, because the fix must stop the recursion long before that point, not merely
        // survive further.
        using var chain = new MemoryStream(BuildNestedAltChunkChain(depth: 50));

        Action act = () => DocxReader.Read(chain);

        act.Should().Throw<WorkbookInvalidException>(
                "an uncatchable StackOverflowException must never be allowed to happen; the load has to " +
                "fail cleanly, with a catchable error, once the nesting exceeds the supported depth")
            .WithMessage("*nested Word*");
    }

    /// <summary>
    /// Sibling / no-regression coverage: an ordinary, shallow document-in-a-document embed (Word supports
    /// this natively via Insert &gt; Object &gt; Text from File, and it is realistic to nest it two or
    /// three levels deep) must keep opening and merging normally after the depth cap is added.
    /// </summary>
    [Fact]
    public void Read_ShallowNestedWordPackageAltChunkChain_StillMaterializesAllLevels()
    {
        using var chain = new MemoryStream(BuildNestedAltChunkChain(depth: 3));

        var document = DocxReader.Read(chain);

        // Each of the 3 wrapper levels contributes one "Level" paragraph before its own altChunk, plus
        // the innermost leaf paragraph -- all merged flat into one document, none left as an unresolved
        // AltChunkBlock.
        document.Blocks.Should().HaveCount(4);
        document.Blocks.Should().AllSatisfy(block => block.Should().BeOfType<Paragraph>());
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).Should().AllBe("Level");
    }

    /// <summary>
    /// Builds a chain of <paramref name="depth"/> nested Word packages: the outermost package's body has
    /// one paragraph followed by a body-level altChunk whose target is a full nested .docx package (a
    /// second, complete Word package embedded as a part), which itself has the same shape, and so on
    /// <paramref name="depth"/> times down to a plain leaf package with no altChunk at all.
    /// </summary>
    private static byte[] BuildNestedAltChunkChain(int depth)
    {
        var current = BuildDocxPackage(includeAltChunk: false, nestedPackage: null);
        for (var i = 0; i < depth; i++)
            current = BuildDocxPackage(includeAltChunk: true, nestedPackage: current);
        return current;
    }

    private static byte[] BuildDocxPackage(bool includeAltChunk, byte[]? nestedPackage)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            void AddBytes(string path, byte[] content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }

            var altChunkContentTypeDefault = includeAltChunk
                ? $"""<Default Extension="docx" ContentType="{NestedWordPackageContentType}"/>"""
                : string.Empty;
            AddText("[Content_Types].xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  {altChunkContentTypeDefault}
                  <Override PartName="/word/document.xml" ContentType="{NestedWordPackageContentType}"/>
                </Types>
                """);

            AddText("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            var documentRels = includeAltChunk
                ? $"""
                  <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                  <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rIdAltChunk" Type="{Ooxml.AltChunkRelType}" Target="afchunk.docx"/>
                  </Relationships>
                  """
                : """
                  <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                  <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                  """;
            AddText("word/_rels/document.xml.rels", documentRels);

            var altChunkElement = includeAltChunk ? """<w:altChunk r:id="rIdAltChunk"/>""" : string.Empty;
            AddText("word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Level</w:t></w:r></w:p>
                    {altChunkElement}
                  </w:body>
                </w:document>
                """);

            if (includeAltChunk && nestedPackage is not null)
                AddBytes("word/afchunk.docx", nestedPackage);
        }

        return stream.ToArray();
    }
}
