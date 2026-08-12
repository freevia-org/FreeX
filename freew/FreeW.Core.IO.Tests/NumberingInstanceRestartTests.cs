using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Coverage for the numbering-INSTANCE identity fix in <c>DocxReader</c>: two body paragraphs whose
/// <c>w:numPr</c> reference DIFFERENT <c>w:numId</c> values must number as two independent lists (each
/// restarting at 1) even when neither carries an explicit <c>w:lvlOverride/w:startOverride</c> — the
/// overwhelmingly common case, since Word only emits that element for an explicit "Restart at 1", not for
/// the implicit fresh start every new numId already gets from its abstract level's declared start. Before
/// the fix, <c>DocxReader</c> resolved a paragraph's list identity purely from the numId -&gt; ListKind map
/// (<c>ReadNumbering</c>'s <c>KindByNumId</c>), so two DIFFERENT numIds mapping to the SAME abstract-derived
/// <see cref="ListKind"/> collapsed into one continuously-counted run: the render layer (<c>DocumentView</c>,
/// both the WPF and Avalonia hosts, which share this reader) only restarts a Number-kind list's counter when
/// <see cref="ParagraphFormatting.ListStartOverride"/> is set, and that was only ever populated from an
/// explicit XML override — so an unrelated second list continued 3./4. instead of restarting at 1./2.
/// </summary>
public sealed class NumberingInstanceRestartTests
{
    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    /// <summary>
    /// Builds a minimal-but-valid docx package with a numbering.xml that defines ONE abstract (decimal,
    /// start=1) shared by TWO independent w:num instances (numId 10 and numId 20) — mirroring how Word
    /// itself typically builds a brand-new, unrelated numbered list against the SAME "List Number" style's
    /// abstract definition. Neither w:num carries a w:lvlOverride/startOverride: Word does not need one to
    /// make numId 20 start fresh at 1 — a distinct numId is itself a fresh counter. The body interleaves an
    /// ordinary paragraph between the two lists so the "continue across an interruption" behavior (R132)
    /// is exercised too: list A's two items are interrupted by body text, matching how a genuinely
    /// continuing list looks in a real document.
    /// </summary>
    private static byte[] AuthorTwoIndependentNumberedListsPackage()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                </Relationships>
                """);

            // List A (numId 10): items 1-2, then an interrupting body paragraph, then item 3 of the SAME
            // list (continues across the interruption, per R132). Then List B (numId 20, a genuinely
            // different instance built off the SAME abstract) starts: it must restart at 1, not continue
            // List A's count at 4.
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>A one</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>A two</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Interrupting body text.</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>A three</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="20"/></w:numPr></w:pPr><w:r><w:t>B one</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="20"/></w:numPr></w:pPr><w:r><w:t>B two</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="5">
                    <w:multiLevelType w:val="hybridMultilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="10"><w:abstractNumId w:val="5"/></w:num>
                  <w:num w:numId="20"><w:abstractNumId w:val="5"/></w:num>
                </w:numbering>
                """);
        }
        return stream.ToArray();
    }

    [Fact]
    public void DistinctNumIds_SharingSameAbstract_SecondListGetsExplicitRestartAtOne()
    {
        var doc = ReadDoc(AuthorTwoIndependentNumberedListsPackage());
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        var aOne = paragraphs.Single(p => p.Runs.Any(r => r.Text == "A one"));
        var aTwo = paragraphs.Single(p => p.Runs.Any(r => r.Text == "A two"));
        var aThree = paragraphs.Single(p => p.Runs.Any(r => r.Text == "A three"));
        var bOne = paragraphs.Single(p => p.Runs.Any(r => r.Text == "B one"));
        var bTwo = paragraphs.Single(p => p.Runs.Any(r => r.Text == "B two"));

        aOne.Formatting.ListKind.Should().Be(ListKind.Number);
        bOne.Formatting.ListKind.Should().Be(ListKind.Number);

        // "A three" continues List A's numId 10 across the interruption: no forced restart (R132,
        // preserved by this fix — a same-numId re-encounter must NOT be treated as a new instance).
        aThree.Formatting.ListStartOverride.Should().BeNull(
            "the interrupting body paragraph does not end List A's numId 10 instance");

        // "B one" opens numId 20 — a genuinely different instance sharing the same abstract as List A.
        // The reader must surface this as an explicit restart at the abstract's declared start (1) so the
        // render layer (which only restarts on ListStartOverride) begins a new counter instead of
        // continuing List A's run at "4.".
        bOne.Formatting.ListStartOverride.Should().Be(1,
            "numId 20 is a distinct numbering instance from numId 10 and must restart at its own declared start");

        // "B two" continues numId 20 (no forced restart mid-instance).
        bTwo.Formatting.ListStartOverride.Should().BeNull("B two continues List B's own instance (numId 20)");

        // Sanity: the very FIRST Number-kind paragraph in the whole document must NOT get a forced
        // override — nothing has counted yet, so the natural default is already correct, and forcing one
        // here would pollute a round-tripped document with a needless dedicated restart w:num (see
        // PreservedNumberingRoundTripTests.FreeWAuthoredLists_RoundTripUnchanged_WithNoPreservedNumbering).
        aOne.Formatting.ListStartOverride.Should().BeNull(
            "the very first Number-kind paragraph has no prior instance to conflict with");
        aTwo.Formatting.ListStartOverride.Should().BeNull();
    }

    /// <summary>
    /// Sibling/no-regression coverage: a single list (one numId) interrupted repeatedly by body text must
    /// keep continuing rather than being mistaken for a new instance on every re-encounter — the R132
    /// behavior this fix must not break while fixing the multi-instance defect above.
    /// </summary>
    [Fact]
    public void SameNumId_RepeatedlyInterrupted_NeverForcesARestartAfterTheFirstItem()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
                </Types>
                """);
            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                </Relationships>
                """);
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>One</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Interrupt 1.</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>Two</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Interrupt 2.</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="10"/></w:numPr></w:pPr><w:r><w:t>Three</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="5">
                    <w:multiLevelType w:val="hybridMultilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="10"><w:abstractNumId w:val="5"/></w:num>
                </w:numbering>
                """);
        }

        var doc = ReadDoc(stream.ToArray());
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        var one = paragraphs.Single(p => p.Runs.Any(r => r.Text == "One"));
        var two = paragraphs.Single(p => p.Runs.Any(r => r.Text == "Two"));
        var three = paragraphs.Single(p => p.Runs.Any(r => r.Text == "Three"));

        one.Formatting.ListStartOverride.Should().BeNull("the first-ever occurrence of numId 10 has no prior instance to conflict with");
        two.Formatting.ListStartOverride.Should().BeNull("Two continues the same numId 10 instance");
        three.Formatting.ListStartOverride.Should().BeNull("Three also continues the same numId 10 instance, across a second interruption");
    }
}
