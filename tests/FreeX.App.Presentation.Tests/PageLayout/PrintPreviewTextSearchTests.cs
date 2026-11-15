using FluentAssertions;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewTextSearchTests
{
    [Fact]
    public void Find_MatchesOriginalUtf16SpanAndIgnoresOtherPaintKinds()
    {
        var painting = new PrintPreviewPagePainting(1, default,
        [
            Text("😀needle") with { Kind = PrintPreviewPaintKind.Image },
            Text("😀 NéEdLe!"),
        ]);

        var result = PrintPreviewTextSearch.Find(1, 0, _ => painting, "néedle", null, backward: false);

        result.Should().Be(new PrintPreviewTextMatch(0, 1, 3, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_RepeatsWithinRunsAcrossInstructionsAndPagesThenWraps(bool backward)
    {
        PrintPreviewPagePainting?[] paintings =
        [
            Page(1, "cat CAT", "cat"),
            null,
            Page(3, "cat"),
        ];
        var first = new PrintPreviewTextMatch(0, 0, 0, 3);
        var second = new PrintPreviewTextMatch(0, 0, 4, 3);
        var third = new PrintPreviewTextMatch(0, 1, 0, 3);
        var fourth = new PrintPreviewTextMatch(2, 0, 0, 3);
        PrintPreviewTextMatch[] expected = backward
            ? [third, second, first, fourth, third]
            : [first, second, third, fourth, first];

        PrintPreviewTextMatch? previous = null;
        foreach (var hit in expected)
        {
            var result = PrintPreviewTextSearch.Find(
                paintings.Length, previous?.PageIndex ?? 0, index => paintings[index], "CAT", previous, backward);
            result.Should().Be(hit);
            previous = result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_BuildsTheStartingPageOnceWhenWrappingBackToItsOtherMatches(bool backward)
    {
        var calls = new List<int>();
        PrintPreviewPagePainting? BuildPage(int pageIndex)
        {
            calls.Add(pageIndex);
            return pageIndex == 1 ? Page(2, "yes YES") : null;
        }

        var previous = new PrintPreviewTextMatch(1, 0, backward ? 0 : 4, 3);
        var result = PrintPreviewTextSearch.Find(4, 1, BuildPage, "yes", previous, backward);

        result.Should().Be(new PrintPreviewTextMatch(1, 0, backward ? 4 : 0, 3));
        int[] expectedVisits = backward ? [1, 0, 3, 2] : [1, 2, 3, 0];
        calls.Should().Equal(expectedVisits);
    }

    [Fact]
    public void Find_StopsBuildingPagesAfterTheFirstMatch()
    {
        var calls = new List<int>();
        PrintPreviewPagePainting? BuildPage(int pageIndex)
        {
            calls.Add(pageIndex);
            return pageIndex == 3 ? Page(4, "yes") : null;
        }

        PrintPreviewTextSearch.Find(5, 2, BuildPage, "yes", null, backward: false)
            .Should().Be(new PrintPreviewTextMatch(3, 0, 0, 3));
        calls.Should().Equal(2, 3);
    }

    [Fact]
    public void Find_VisitsOnlyOneCircuitWhenThereIsNoMatch()
    {
        var calls = new List<int>();
        PrintPreviewPagePainting? BuildPage(int pageIndex)
        {
            calls.Add(pageIndex);
            return pageIndex % 2 == 0 ? Page(pageIndex + 1, "unrelated") : null;
        }

        PrintPreviewTextSearch.Find(4, 2, BuildPage, "missing", null, backward: true).Should().BeNull();
        calls.Should().Equal(2, 1, 0, 3);
    }

    [Fact]
    public void Find_RestartsAfterExplicitPageNavigationOrAStaleMatch()
    {
        var previous = new PrintPreviewTextMatch(0, 0, 4, 3);
        PrintPreviewPagePainting BuildPage(int pageIndex) => Page(pageIndex + 1, "cat dog");

        PrintPreviewTextSearch.Find(2, 1, BuildPage, "cat", previous, backward: false)
            .Should().Be(new PrintPreviewTextMatch(1, 0, 0, 3));
        PrintPreviewTextSearch.Find(2, 0, BuildPage, "cat", previous, backward: false)
            .Should().Be(new PrintPreviewTextMatch(0, 0, 0, 3));
    }

    [Fact]
    public void Find_WrapsToTheSameMatchWhenItIsTheOnlyHit()
    {
        var previous = new PrintPreviewTextMatch(0, 0, 0, 2);

        PrintPreviewTextSearch.Find(1, 0, _ => Page(1, "aab"), "aa", previous, backward: false)
            .Should().Be(previous);
    }

    [Theory]
    [InlineData(0, "cat")]
    [InlineData(1, "")]
    public void Find_DoesNotBuildPagesWithoutAQueryOrPages(int pageCount, string query)
    {
        var result = PrintPreviewTextSearch.Find(
            pageCount, 0, _ => throw new InvalidOperationException("No page should be built"),
            query, null, backward: false);

        result.Should().BeNull();
    }

    private static PrintPreviewPagePainting Page(int number, params string[] runs) =>
        new(number, default, runs.Select(Text).ToArray());

    private static PrintPreviewPaintInstruction Text(string text) =>
        PrintPreviewPaintInstruction.TextRun(default, 200, text, default, PageTextAlignment.Left);
}
