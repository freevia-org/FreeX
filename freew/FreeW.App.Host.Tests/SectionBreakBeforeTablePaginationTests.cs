using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A page-type section break directly followed by a table starts the table's page (the table, not the next
/// paragraph, opens the section). The paragraph-based break flagging used to also flag the next PARAGRAPH,
/// which sits after the table -- sending the section's trailing 1pt end-of-section mark to a page of its own
/// (stress004: a one-row-per-page title table plus a stranded mark, and a blank page after it).
/// </summary>
public sealed class SectionBreakBeforeTablePaginationTests
{
    [StaFact]
    public void ParagraphAfterATable_DoesNotStartANewPage_WhenTheTableOpenedTheSection()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage));
        doc.Blocks.Add(Table.Create(2, 2));
        doc.Blocks.Add(new Paragraph("end of section mark"));
        doc.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage));
        doc.Blocks.Add(new Paragraph("next section"));

        var view = new DocumentView();
        view.LoadModel(doc);
        var pages = PaginationEngine.ComputeBlockPageAssignment(view);

        pages[1].Should().Be(1, "the table opens the second section's page");
        pages[2].Should().Be(1, "the paragraph after the table shares the table's page");
        pages[4].Should().Be(2);
    }
}
