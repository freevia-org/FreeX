using System.IO;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip + recomputation coverage for the reference/numbering complex fields driven by F9 /
/// Update-Field: a <c>REF</c> cross-reference to a bookmark and a <c>SEQ</c> sequence counter survive a
/// save+reload as <c>w:fldChar</c>/<c>w:instrText</c> fields, and re-running <see cref="ComplexFieldEngine"/>
/// after editing the document recomputes their results against the reloaded model.
/// </summary>
public class ComplexFieldUpdateRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void RefAndSeqFields_SurviveRoundTrip_ThenRecomputeAfterTargetChanges()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // 0: a bookmarked target the REF points at.
        doc.Blocks.Add(new Paragraph("Section Alpha") { BookmarkName = "sec" });
        // 1: a REF to that bookmark (cached "Section Alpha").
        var refPara = new Paragraph();
        refPara.Runs.Add(Run.ComplexFieldRun(" REF sec ", "Section Alpha"));
        doc.Blocks.Add(refPara);
        // 2 & 3: two SEQ Figure counters (cached "1"/"2").
        var seq1 = new Paragraph();
        seq1.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "1"));
        doc.Blocks.Add(seq1);
        var seq2 = new Paragraph();
        seq2.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "2"));
        doc.Blocks.Add(seq2);

        var reloaded = RoundTrip(doc);

        // The fields survived as complex fields with their instructions intact.
        var paras = reloaded.Blocks.OfType<Paragraph>().ToList();
        paras[1].Runs.Single().ComplexField!.Instruction.Should().Be(" REF sec ");
        paras[2].Runs.Single().ComplexField!.Instruction.Should().Be(" SEQ Figure ");
        paras[3].Runs.Single().ComplexField!.Instruction.Should().Be(" SEQ Figure ");

        // Baseline recompute on the reloaded doc matches the document state.
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Section Alpha");
        ComplexFieldEngine.Recompute(reloaded, 2, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(reloaded, 3, 0).Should().Be("2");

        // Now edit the document: rename the REF target, and insert a new figure before the existing ones.
        var target = (Paragraph)reloaded.Blocks[0];
        target.Runs.Clear();
        target.Runs.Add(new Run("Section Beta"));

        var inserted = new Paragraph();
        inserted.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "?"));
        reloaded.Blocks.Insert(2, inserted); // pushes the original SEQ fields to indices 3 and 4

        // F9 recomputation reflects the edits: REF follows the new text; SEQ fields renumber 1,2,3.
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Section Beta");
        ComplexFieldEngine.Recompute(reloaded, 2, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(reloaded, 3, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(reloaded, 4, 0).Should().Be("3");
    }

    [Fact]
    public void IfField_SurvivesRoundTrip_ThenRecomputesFromBookmarkText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("125") { BookmarkName = "order" });
        var field = new Paragraph();
        field.Runs.Add(Run.ComplexFieldRun(
            " IF order >= 100 \"Thanks\" \"The minimum order is 100 units\" ",
            "stale"));
        doc.Blocks.Add(field);

        var reloaded = RoundTrip(doc);
        var run = ((Paragraph)reloaded.Blocks[1]).Runs.Single();
        run.ComplexField!.Instruction.Should().Be(
            " IF order >= 100 \"Thanks\" \"The minimum order is 100 units\" ");
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Thanks");

        var target = (Paragraph)reloaded.Blocks[0];
        target.Runs.Clear();
        target.Runs.Add(new Run("80"));
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("The minimum order is 100 units");
    }

    [Fact]
    public void ComplexNoteRef_SurvivesRoundTripAndRefreshesMarkerNumber()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var target = new Paragraph("Body");
        target.Runs.Add(Run.FootnoteReference(20));
        target.BookmarkNames.Add("_RefNote");
        target.BookmarkBoundaries.Add(new BookmarkBoundary("note", BookmarkBoundaryKind.Start, 1, "_RefNote"));
        target.BookmarkBoundaries.Add(new BookmarkBoundary("note", BookmarkBoundaryKind.End, 2));
        doc.Blocks.Add(target);
        doc.Footnotes[20] = new Footnote(20, "note");
        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" NOTEREF _RefNote \\p ", "stale") }
        });

        var reloaded = RoundTrip(doc);
        var run = ((Paragraph)reloaded.Blocks[1]).Runs.Single();
        run.ComplexField!.Instruction.Should().Be(" NOTEREF _RefNote \\p ");
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("1 above");
    }

    [Fact]
    public void DocPropertyAndDocVariableFields_SurviveRoundTripAndRefreshFromSerializedSources()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var custom = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var variant = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Current title";
        var metadataMoment = new DateTime(2026, 8, 6, 14, 5, 0);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(metadataMoment);
        doc.Properties.Created = new DateTimeOffset(metadataMoment, localOffset);
        doc.Properties.Modified = new DateTimeOffset(metadataMoment.AddDays(2), localOffset);
        doc.Properties.LastModifiedBy = "Ada Lovelace";
        doc.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "docVars",
                new System.Xml.Linq.XElement(
                    word + "docVar",
                    new System.Xml.Linq.XAttribute(word + "name", "Channel"),
                    new System.Xml.Linq.XAttribute(word + "val", "Beta"))));
        doc.Preserved.OriginalCustomProperties = new System.Xml.Linq.XElement(
            custom + "Properties",
            new System.Xml.Linq.XElement(
                custom + "property",
                new System.Xml.Linq.XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new System.Xml.Linq.XAttribute("pid", "2"),
                new System.Xml.Linq.XAttribute("name", "Team"),
                new System.Xml.Linq.XElement(variant + "lpwstr", "Research")));
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" DOCPROPERTY Title ", "stale title"),
                new Run(" | "),
                Run.ComplexFieldRun(" DOCPROPERTY Team ", "stale team"),
                new Run(" | "),
                Run.ComplexFieldRun(" DOCVARIABLE Channel ", "stale channel"),
                new Run(" | "),
                Run.ComplexFieldRun(" CREATEDATE \\@ \"yyyy-MM-dd\" ", "stale created"),
                new Run(" | "),
                Run.ComplexFieldRun(" SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ", "stale saved"),
                new Run(" | "),
                Run.ComplexFieldRun(" LASTSAVEDBY ", "stale owner")
            }
        });

        var reloaded = RoundTrip(doc);
        var runs = ((Paragraph)reloaded.Blocks.Single()).Runs;

        runs[0].ComplexField!.Instruction.Should().Be(" DOCPROPERTY Title ");
        runs[2].ComplexField!.Instruction.Should().Be(" DOCPROPERTY Team ");
        runs[4].ComplexField!.Instruction.Should().Be(" DOCVARIABLE Channel ");
        runs[6].ComplexField!.Instruction.Should().Be(" CREATEDATE \\@ \"yyyy-MM-dd\" ");
        runs[8].ComplexField!.Instruction.Should().Be(" SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ");
        runs[10].ComplexField!.Instruction.Should().Be(" LASTSAVEDBY ");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[0]).Should().Be("Current title");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[2]).Should().Be("Research");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[4]).Should().Be("Beta");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[6]).Should().Be("2026-08-06");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[8]).Should().Be("2026-08-08 14:05");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[10]).Should().Be("Ada Lovelace");
    }

    [Fact]
    public void ExtendedDocPropertyFields_SurviveRoundTripAndRefreshFromAppProperties()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Preserved.Parts.Add(new PreservedPart(
            Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesPartName,
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Application>Microsoft Word</Application>
                  <Company>Contoso Research</Company>
                  <Manager>Ada Lovelace</Manager>
                  <Template>Proposal.dotx</Template>
                </Properties>
                """),
            Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesContentType,
            PackageRelationshipType: Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesRelationshipType));
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" DOCPROPERTY Company ", "stale company"),
                Run.ComplexFieldRun(" DOCPROPERTY Manager ", "stale manager"),
                Run.ComplexFieldRun(" DOCPROPERTY Template ", "stale property template"),
                Run.ComplexFieldRun(" TEMPLATE ", "stale template"),
                Run.ComplexFieldRun(" TEMPLATE \\p ", @"C:\Templates\Proposal.dotx")
            }
        });

        var reloaded = RoundTrip(doc);
        var runs = ((Paragraph)reloaded.Blocks.Single()).Runs;

        runs.Select(run => run.ComplexField!.Instruction).Should().Equal(
            " DOCPROPERTY Company ",
            " DOCPROPERTY Manager ",
            " DOCPROPERTY Template ",
            " TEMPLATE ",
            " TEMPLATE \\p ");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[0]).Should().Be("Contoso Research");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[1]).Should().Be("Ada Lovelace");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[2]).Should().Be("Proposal.dotx");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[3]).Should().Be("Proposal.dotx");
        ComplexFieldEngine.Recompute(reloaded, 0, runs[4]).Should().Be(@"C:\Templates\Proposal.dotx");
        reloaded.Preserved.Parts.Should().ContainSingle(part =>
            part.PartName == Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesPartName);
    }

    [Fact]
    public void DocumentStatisticFields_SurviveBothFieldFormsAndRefreshFromCurrentStory()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello world."));
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("stale")
                {
                    ComplexField = new ComplexField(
                        " NUMCHARS ",
                        SimpleField: new SimpleFieldMetadata(IsDirty: true))
                }
            }
        });
        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" NUMWORDS ", "stale") }
        });

        var reloaded = RoundTrip(doc);
        var numChars = ((Paragraph)reloaded.Blocks[1]).Runs.Single();
        var numWords = ((Paragraph)reloaded.Blocks[2]).Runs.Single();

        numChars.ComplexField!.Instruction.Should().Be(" NUMCHARS ");
        numChars.ComplexField.SimpleField.Should().Be(new SimpleFieldMetadata(IsDirty: true));
        numWords.ComplexField!.Instruction.Should().Be(" NUMWORDS ");
        numWords.ComplexField.SimpleField.Should().BeNull();
        ComplexFieldEngine.Recompute(reloaded, 1, numChars).Should().Be("21");
        ComplexFieldEngine.Recompute(reloaded, 2, numWords).Should().Be("4");
    }
}
