using System.Linq;
using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A TOC entry ends "title, tab, PAGEREF page number". The right-aligned dot-leader tab must reserve the width of
/// the field's cached result exactly as it does for plain text; otherwise the leader runs the full distance to the
/// stop and the number wraps onto its own line (stress004: 99 pages instead of Word's 95).
/// </summary>
public sealed class TabLeaderBeforeFieldResultTests
{
    private const string PageRef = @" PAGEREF _Toc1 \h ";
    private static double LeaderWidth(Run pageNumber)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph("Entry\t")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(300, TabStopAlignment.Right, TabLeader.Dots)]
            }
        };
        paragraph.Runs.Add(pageNumber);
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<System.Windows.Documents.InlineUIContainer>()
            .Select(inline => inline.Child)
            .OfType<TabStopLeaderElement>()
            .Single().Width;
    }

    [StaFact]
    public void RightTab_ReservesWidthOfAPagerefResult_LikePlainText()
    {
        var plain = LeaderWidth(new Run("123"));
        var field = LeaderWidth(new Run("123")
        {
            ComplexField = new ComplexField(PageRef)
        });

        plain.Should().BeGreaterThan(0);
        field.Should().BeApproximately(plain, 0.01);
    }

    [StaFact]
    public void RightTab_LeaderStopsShortOfTheStop_WhenAFieldResultFollows()
    {
        var withoutNumber = LeaderWidth(new Run(string.Empty));
        var field = LeaderWidth(new Run("1234")
        {
            ComplexField = new ComplexField(PageRef)
        });

        field.Should().BeLessThan(withoutNumber - 5);
    }
}
