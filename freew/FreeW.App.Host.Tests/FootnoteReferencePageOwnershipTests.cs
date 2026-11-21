using System.Collections.Generic;
using System.Linq;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A footnote belongs to the page that carries its REFERENCE mark, which for a paragraph that splits
/// across pages is a later page than the one the paragraph starts on (Word).
/// </summary>
public sealed class FootnoteReferencePageOwnershipTests
{
    [StaFact]
    public void NoteOfAReferenceInTheSplitOffTail_BelongsToThePageShowingTheReference()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Footnotes[1] = new Footnote(1, "Short note body.");
        doc.Blocks.Add(new Paragraph("Opening paragraph."));
        var long1 = new Paragraph();
        long1.Runs.Add(new Run(string.Join(" ", Enumerable.Range(1, 900).Select(i => $"word{i:0000}"))));
        long1.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(long1);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var segments = new List<PaginationEngine.SegmentPagination>();
        PaginationEngine.ComputeBlockPageAssignment(editor, out var totalPages, segments);

        totalPages.Should().BeGreaterThan(1, "the paragraph must straddle a page boundary");
        var owners = segments.Where(s => s.PageFootnoteIds is { } ids && ids.Contains(1))
            .Select(s => s.PageOffset).ToList();
        owners.Should().Equal(new[] { totalPages - 1 }, "the reference is on the paragraph's last page");
        segments.Where(s => s.PageOffset < totalPages - 1).Select(s => s.FootnoteReserveDip)
            .Should().OnlyContain(reserve => reserve == 0);
    }
}
