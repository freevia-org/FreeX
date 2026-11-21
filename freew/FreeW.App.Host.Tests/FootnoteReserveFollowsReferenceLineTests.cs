using System.Collections.Generic;
using System.Linq;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The band reserved for a page's footnotes must belong to notes whose reference mark really sits on that page.
/// When a long paragraph starts on the page but the line holding its footnote mark is pushed to the next page, the
/// page must not keep the note band (a blank gap above the page's bottom margin, stress018 page 28).
/// </summary>
public sealed class FootnoteReserveFollowsReferenceLineTests
{
    [StaFact]
    public void PagesWithoutAReferenceMark_ReserveNoNoteBand()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var filler = string.Join(" ", Enumerable.Range(1, 90).Select(i => $"palabra{i:000}"));
        for (var i = 1; i <= 40; i++)
        {
            doc.Footnotes[i] = new Footnote(i, string.Join(" ", Enumerable.Range(1, 12).Select(n => $"nota{n:00}")));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run(filler));
            paragraph.Runs.Add(Run.FootnoteReference(i));
            doc.Blocks.Add(paragraph);
        }

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var segments = new List<PaginationEngine.SegmentPagination>();
        PaginationEngine.ComputeBlockPageAssignment(editor, out var totalPages, segments);

        totalPages.Should().BeGreaterThan(3);
        var dead = segments.Where(s => s.FootnoteReserveDip > 0 && (s.PageFootnoteIds is null || s.PageFootnoteIds.Count == 0))
            .Select(s => $"page {s.PageOffset} reserve {s.FootnoteReserveDip:0.#}").ToList();
        dead.Should().BeEmpty("a reserved band with no note on the page is dead space");
    }
}
