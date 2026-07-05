using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for Mark Citation (TA) fields — the hidden marks that record legal citations for a
/// Table of Authorities (Word's References &gt; Mark Citation). The mark serialises as a textless
/// <c>w:fldSimple</c> whose <c>w:instr</c> carries the TA instruction
/// (<c> TA \l "long" \s "short" \c N </c>); the reader recovers the <see cref="Citation"/>.
/// </summary>
public class TableOfAuthoritiesRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument CitationDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Brown v. Board of Education"));
        paragraph.Runs.Add(Run.CitationMark(new Citation(
            "Brown v. Board of Education, 347 U.S. 483 (1954)", CitationCategory.Cases, "Brown")));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void CitationMark_SurvivesRoundTrip()
    {
        var result = RoundTrip(CitationDocument());

        var citationRun = result.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Citation is not null);
        citationRun.Citation!.LongCitation.Should().Be("Brown v. Board of Education, 347 U.S. 483 (1954)");
        citationRun.Citation.ShortCitation.Should().Be("Brown");
        citationRun.Citation.Category.Should().Be(CitationCategory.Cases);
        // The mark carries no visible text (Word's hidden TA field).
        citationRun.Text.Should().BeEmpty();
    }

    [Fact]
    public void CitationMark_EmitsTaFldSimpleWithSwitches()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(CitationDocument(), stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        var instr = xml.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value;
        instr.Should().Contain("TA");
        instr.Should().Contain("\\l \"Brown v. Board of Education, 347 U.S. 483 (1954)\"");
        instr.Should().Contain("\\s \"Brown\"");
        instr.Should().Contain("\\c 1");
    }

    [Fact]
    public void CitationMark_WithoutShortForm_OmitsShortSwitch()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(new Citation("17 U.S.C. § 107", CitationCategory.Statutes)));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var instr = XDocument.Load(entry).Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value;

        instr.Should().NotContain("\\s");
        instr.Should().Contain("\\c 2");

        // And the category survives the read back.
        var result = RoundTrip(doc);
        var run = result.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Citation is not null);
        run.Citation!.Category.Should().Be(CitationCategory.Statutes);
        run.Citation.ShortCitation.Should().BeEmpty();
    }

    [Fact]
    public void TableOfAuthorities_BuildsFromBodyCitationMarksAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CitationMark(new Citation("Roe v. Wade, 410 U.S. 113 (1973)", CitationCategory.Cases)));
        p.Runs.Add(Run.CitationMark(new Citation("42 U.S.C. § 1983", CitationCategory.Statutes)));
        doc.Blocks.Add(p);

        // Reopen: the side-store is empty, so the table must be built from the body TA marks.
        var reopened = RoundTrip(doc);
        reopened.Citations.Should().BeEmpty();

        var table = TableOfAuthorities.Build(reopened).Select(x => x.PlainText).ToList();
        table.Should().Equal(
            TableOfAuthorities.HeadingText,
            "Cases",
            "Roe v. Wade, 410 U.S. 113 (1973)",
            "Statutes",
            "42 U.S.C. § 1983");
    }

    [Fact]
    public void TableOfAuthorities_ShortCitationAliasesAggregateAfterReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var full = new Paragraph();
        full.Runs.Add(Run.CitationMark(new Citation(
            "Brown v. Board of Education, 347 U.S. 483 (1954)",
            CitationCategory.Cases,
            "Brown")));
        doc.Blocks.Add(full);
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        var shortForm = new Paragraph();
        shortForm.Runs.Add(Run.CitationMark(new Citation("Brown", CitationCategory.Cases)));
        doc.Blocks.Add(shortForm);

        var reopened = RoundTrip(doc);
        reopened.Citations.Should().BeEmpty();

        var entry = TableOfAuthorities.Build(reopened)
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);

        entry.PlainText.Should().Be("Brown v. Board of Education, 347 U.S. 483 (1954)\t1, 2");
    }

    [Fact]
    public void TableOfAuthoritiesEntryTabLeader_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CitationMark(new Citation("Legal Services Corp. v. Velazquez", CitationCategory.Cases)));
        doc.Blocks.Add(p);
        doc.Blocks.AddRange(TableOfAuthorities.Build(doc, new ToaOptions { TabLeader = ToaTabLeader.Dashes }));

        var reopened = RoundTrip(doc);

        reopened.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Formatting.TabStops.Should().Equal(
                new TabStop(
                    TableOfAuthorities.DefaultEntryRightTabStopPt,
                    TabStopAlignment.Right,
                    TabLeader.Dashes));
    }
}
