using System.Linq;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A list takes exactly the height of its items: WPF's auto list margin (one line above and below) used
/// to push every neighbour a line away, so lists cost page capacity Word does not charge.
/// </summary>
public sealed class ListSpacingPaginationTests
{
    [StaFact]
    public void ListBlock_HasNoAutoMargin_AndKeepsFullPageCapacity()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.MarginTopPt = 90.1;
        doc.Page.MarginBottomPt = 70.85;
        var tight = ParagraphFormatting.Default with { SpaceAfterPt = 0 };
        for (var i = 0; i < 100; i++)
        {
            var listItem = i is >= 10 and < 14;
            doc.Blocks.Add(new Paragraph("Line " + i)
            {
                Formatting = listItem ? tight with { ListKind = ListKind.Bullet } : tight
            });
        }

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var lists = editor.Document.Blocks.OfType<System.Windows.Documents.List>().ToList();
        lists.Should().ContainSingle();
        lists[0].Margin.Top.Should().Be(0);
        lists[0].Margin.Bottom.Should().Be(0);

        // 631.05pt of body at the 1.15 line pitch of 11pt Calibri (15.45pt) holds 40 lines whether or not
        // some of them are list items.
        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);
        assignment.Count(page => page == 0).Should().Be(40);
    }
}
