using FluentAssertions;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentNoteNavigationPlannerTests
{
    [Theory]
    [InlineData(false, 15, 20)]
    [InlineData(false, 30, 10)]
    [InlineData(true, 25, 20)]
    [InlineData(true, 10, 30)]
    public void FindAdjacent_SelectsByDirectionAndWraps(
        bool previous,
        int caret,
        int expected)
    {
        DocumentNoteNavigationPlanner.TryFindAdjacent(
                new[] { 10, 20, 30 },
                marker => marker.CompareTo(caret),
                previous,
                out var target)
            .Should().BeTrue();

        target.Should().Be(expected);
    }

    [Fact]
    public void FindAdjacent_RejectsAnEmptyMarkerList()
    {
        DocumentNoteNavigationPlanner.TryFindAdjacent(
                Array.Empty<int>(),
                marker => marker,
                previous: false,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void FindMarkers_ProjectsBodyAndTableCellMarkersInDocumentOrder()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Before "),
                new Run("1") { FootnoteId = 1 },
            },
        });
        var table = new Table();
        var row = new TableRow();
        var spanningCell = new TableCell { GridSpan = 2 };
        spanningCell.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run("Cell "),
                new Run("2") { FootnoteId = 2 },
            },
        });
        row.Cells.Add(spanningCell);
        var secondCell = new TableCell();
        secondCell.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run("End "),
                new Run("3") { EndnoteId = 3 },
            },
        });
        row.Cells.Add(secondCell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        DocumentNoteNavigationPlanner.FindMarkers(document, footnote: true).Should().Equal(
            DocumentNoteMarkerPosition.Body(blockIndex: 0, paragraphOffset: 7),
            DocumentNoteMarkerPosition.TableCell(
                blockIndex: 1,
                rowIndex: 0,
                gridColumnIndex: 0,
                paragraphIndex: 0,
                paragraphOffset: 5));
        DocumentNoteNavigationPlanner.FindMarkers(document, footnote: false).Should().Equal(
            DocumentNoteMarkerPosition.TableCell(
                blockIndex: 1,
                rowIndex: 0,
                gridColumnIndex: 2,
                paragraphIndex: 0,
                paragraphOffset: 4));
    }

    [Fact]
    public void CompareDocumentOrder_OrdersOffsetsAndTableCellCoordinates()
    {
        var bodyCaret = DocumentNoteMarkerPosition.Body(0, 5);
        var laterBodyMarker = DocumentNoteMarkerPosition.Body(0, 8);
        DocumentNoteNavigationPlanner.CompareDocumentOrder(laterBodyMarker, bodyCaret)
            .Should().BePositive();

        var tableCaret = DocumentNoteMarkerPosition.TableCell(1, 0, 0, 0, 10);
        var nextParagraph = DocumentNoteMarkerPosition.TableCell(1, 0, 0, 1, 0);
        var nextGridCell = DocumentNoteMarkerPosition.TableCell(1, 0, 2, 0, 0);
        DocumentNoteNavigationPlanner.CompareDocumentOrder(nextParagraph, tableCaret)
            .Should().BePositive();
        DocumentNoteNavigationPlanner.CompareDocumentOrder(nextGridCell, nextParagraph)
            .Should().BePositive();
    }

    [Fact]
    public void AvaloniaAdapter_ConsumesSharedTableAwareMarkerPositions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("DocumentNoteNavigationPlanner.FindMarkers(_doc, footnote)");
        source.Should().Contain("DocumentNoteNavigationPlanner.CompareDocumentOrder(position, caret)");
        source.Should().Contain("destination.TableRowIndex!.Value");
        source.Should().Contain("destination.TableGridColumnIndex!.Value");
    }
}
