using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentView;

/// <summary>
/// The footnote band is laid out the way Word sets it: the "footnote text" style's own size and single
/// line (stress008: Times New Roman 10pt, after 0) under a separator line box in the Normal font, and a
/// long note continues in the next pages' bands instead of taking over its reference page.
/// </summary>
public sealed class DocumentNoteRegionPlannerWordMetricsTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    private static TextDocument DocumentWithFootnoteStyle(params string[] noteTexts)
    {
        var document = TextDocument.CreateEmpty();
        document.DefaultRun = document.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        document.Styles["FootnoteText"] = new DocumentStyle
        {
            Id = "FootnoteText",
            Name = "footnote text",
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceAfterPt = 0,
                SpaceAfterIsSet = true,
                LineSpacing = 1,
                LineRule = LineSpacingRule.Multiple,
                LineSpacingIsSet = true,
            },
            Run = new RunFormatting { FontFamily = "Times New Roman", FontSizePt = 10 },
        };
        for (var index = 0; index < noteTexts.Length; index++)
        {
            var footnote = new Footnote(index + 1);
            footnote.Content.Add(new Paragraph(noteTexts[index]) { StyleId = "FootnoteText" });
            document.Footnotes[index + 1] = footnote;
        }
        return document;
    }

    [Fact]
    public void FootnoteRegion_UsesTheFootnoteStyleMetrics()
    {
        var document = DocumentWithFootnoteStyle("Philippe Aries, Geschichte des Todes", "Marianne Mischke, Berlin 1996");

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 2], 1, 600);

        plan.TextFontSizePt.Should().Be(10);
        plan.TextFontFamily.Should().Be("Times New Roman");
        plan.RowGapDip.Should().Be(0);
        var line = 10 * PxPerPoint * 1.15;
        plan.Rows.Should().AllSatisfy(row => row.EstimatedHeightDip.Should().BeApproximately(line, 0.01));
        plan.SeparatorHeightDip.Should().BeApproximately(11 * PxPerPoint * 1.22, 0.01);
        plan.EstimatedHeightDip.Should().Be(Math.Ceiling(plan.SeparatorHeightDip + 2 * line));
    }

    [Fact]
    public void FootnoteRegion_WithoutAFootnoteStyle_KeepsTheHistoricalEstimate()
    {
        var document = TextDocument.CreateEmpty();
        document.Footnotes[1] = new Footnote(1, "plain note");

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1], 1, 600);

        plan.TextFontSizePt.Should().Be(DocumentNoteRegionPlanner.NoteTextFontSizePt);
        plan.TextFontFamily.Should().BeNull();
        plan.SeparatorHeightDip.Should().Be(DocumentNoteRegionPlanner.DefaultSeparatorHeightDip);
    }

    [Fact]
    public void SequentialBands_ContinueALongNote_OnTheNextPage_InWordOrder()
    {
        var longNote = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"w{i:000}"));
        var document = DocumentWithFootnoteStyle(longNote, "second note");

        var bands = DocumentNoteRegionPlanner.BuildSequentialFootnoteBands(
            document,
            [([1], 120.0, 1), ([2], 400.0, 2)],
            600);

        bands.Should().HaveCount(2);
        bands[0].SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Initial);
        bands[0].Fragments.Should().ContainSingle().Which.EndsNote.Should().BeFalse();
        bands[0].EstimatedHeightDip.Should().BeLessThanOrEqualTo(120);
        bands[1].SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Continuation);
        bands[1].Fragments.Select(fragment => fragment.NoteId).Should().Equal(1, 2);
        bands[1].Fragments[0].Label.Should().BeNull("a continuation repeats no note number");
        var words = string.Join(" ", bands.SelectMany(band => band.Fragments).Where(f => f.NoteId == 1).Select(f => f.Text));
        words.Should().Be(longNote, "every word of the note appears once, in order");
    }
}
