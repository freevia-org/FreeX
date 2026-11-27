using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Format-aware Find Next over the model (the request overload of FindReplaceDialogPlanner.FindNextMatch)
/// and the "is this span exactly one match" check Replace uses (<see cref="FindReplaceDialogPlanner.IsMatchAt"/>).
/// </summary>
public sealed class FindReplaceFormatNavigationTests
{
    private static readonly FindReplaceSearchOptions NoOptions = new();
    private static readonly RunFormatting Bold = new() { Bold = true };

    private static TextDocument Doc(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private static Paragraph Mixed() =>
        new() { Runs = { new Run("plain cat "), new Run("bold cat", Bold) } };

    [Fact]
    public void FindNext_WithBoldCriteria_SkipsPlainMatchesAndLandsOnTheBoldOne()
    {
        var document = Doc(Mixed());
        var request = new FindReplaceSearchRequest("cat", NoOptions, new FindFormat { Bold = true });

        var match = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0);

        match.Should().Be(new FindReplaceMatch(0, "plain cat bold ".Length, 3));
    }

    [Fact]
    public void FindNext_FormattingOnly_FindsTheFormattedStretchWithAnEmptyTerm()
    {
        var document = Doc(Mixed());
        var request = new FindReplaceSearchRequest(string.Empty, NoOptions, new FindFormat { Bold = true });

        var match = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0);

        match.Should().Be(new FindReplaceMatch(0, "plain cat ".Length, "bold cat".Length));
    }

    [Fact]
    public void FindNext_WithoutFormatting_BehavesLikeTheTermOverload()
    {
        var document = Doc(Mixed());
        var request = new FindReplaceSearchRequest("cat", NoOptions);

        FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0)
            .Should().Be(FindReplaceDialogPlanner.FindNextMatch(document, "cat", NoOptions, 0, 0));
    }

    [Fact]
    public void FindNext_WithFormattingWalksIntoTableCells()
    {
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph { Runs = { new Run("cell cat", Bold) } });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        var document = Doc(new Paragraph("plain cat"), table);
        var request = new FindReplaceSearchRequest("cat", NoOptions, new FindFormat { Bold = true });

        var inCell = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0);

        inCell.Should().NotBeNull();
        inCell!.Value.IsInTableCell.Should().BeTrue();
    }

    [Fact]
    public void FindNext_WithFormattingFallsBackToTheBoldHeaderWhenTheBodyHasNoBoldMatch()
    {
        var document = Doc(new Paragraph("plain cat"));
        document.Header = new HeaderFooter("header cat");
        document.Header.Paragraphs[0].Runs[0].Formatting = Bold;
        var request = new FindReplaceSearchRequest("cat", NoOptions, new FindFormat { Bold = true });

        var inHeader = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0);

        inHeader.Should().NotBeNull();
        inHeader!.Value.IsInHeaderFooter.Should().BeTrue();
    }

    [Fact]
    public void IsMatchAt_RequiresTheSpanToBeExactlyAMatchThatSatisfiesTheFormat()
    {
        var document = Doc(Mixed());
        var request = new FindReplaceSearchRequest("cat", NoOptions, new FindFormat { Bold = true });
        var boldCat = new FindReplaceMatch(0, "plain cat bold ".Length, 3);
        var plainCat = new FindReplaceMatch(0, "plain ".Length, 3);

        FindReplaceDialogPlanner.IsMatchAt(document, request, boldCat).Should().BeTrue();
        FindReplaceDialogPlanner.IsMatchAt(document, request, plainCat).Should().BeFalse();
        FindReplaceDialogPlanner.IsMatchAt(document, request, boldCat with { Length = 2 }).Should().BeFalse();
        FindReplaceDialogPlanner.IsMatchAt(document, request with { Format = null }, plainCat).Should().BeTrue();
    }
}
