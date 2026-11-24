using FluentAssertions.Execution;

namespace FreeW.Core.Model.Tests;

public sealed class ShapeTextParagraphBookmarkLifecycleTests
{
    public static IEnumerable<object[]> SplitCases()
    {
        foreach (var grouped in new[] { false, true })
        foreach (var scenario in new[] { "crossing", "starts-at-cut", "ends-at-cut", "before", "after",
            "collapsed-cut", "collapsed-start", "collapsed-end", "from-prior", "to-following" })
            yield return [grouped, scenario];
    }

    [Theory]
    [MemberData(nameof(SplitCases))]
    public void SplitDistributesEachBoundaryOnceAndRestoresSourceAndNeighborIdentity(bool grouped, string scenario)
    {
        var source = ParagraphWithRuns("ab", "cd", "ef");
        source.BookmarkNames.AddRange(["legacy", "sibling"]);
        var start = Start("source", 1);
        var end = End("source", 2);
        var split = 3;
        BookmarkBoundary[] expectedFirst;
        BookmarkBoundary[] expectedSecond;
        switch (scenario)
        {
            case "crossing":
                expectedFirst = [start]; expectedSecond = [end with { RunIndex = 1 }]; break;
            case "starts-at-cut":
                split = 2; expectedFirst = []; expectedSecond = [start with { RunIndex = 0 }, end with { RunIndex = 1 }]; break;
            case "ends-at-cut":
                split = 4; expectedFirst = [start, end]; expectedSecond = []; break;
            case "before":
                split = 6; expectedFirst = [start, end]; expectedSecond = []; break;
            case "after":
                split = 0; expectedFirst = []; expectedSecond = [start, end]; break;
            case "collapsed-cut":
                split = 2; end = end with { RunIndex = 1 };
                expectedFirst = []; expectedSecond = [start with { RunIndex = 0 }, end with { RunIndex = 0 }]; break;
            case "collapsed-start":
                split = 0; start = start with { RunIndex = 0 }; end = end with { RunIndex = 0 };
                expectedFirst = []; expectedSecond = [start, end]; break;
            case "collapsed-end":
                split = 6; start = start with { RunIndex = 3 }; end = end with { RunIndex = 3 };
                expectedFirst = []; expectedSecond = [start with { RunIndex = 0 }, end with { RunIndex = 0 }]; break;
            case "from-prior":
                split = 2; end = End("from-prior", 1); expectedFirst = [end]; expectedSecond = []; break;
            case "to-following":
                split = 4; start = Start("to-following", 2); expectedFirst = []; expectedSecond = [start with { RunIndex = 0 }]; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (scenario != "from-prior") source.BookmarkBoundaries.Add(start);
        if (scenario != "to-following") source.BookmarkBoundaries.Add(end);
        var fixture = new Fixture(grouped, source);
        if (scenario == "from-prior") fixture.Prior.BookmarkBoundaries.Add(Start("from-prior", 0));
        if (scenario == "to-following") fixture.Following.BookmarkBoundaries.Add(End("to-following", 1));
        var sourceSnapshot = Snapshot(source);
        var priorSnapshot = Snapshot(fixture.Prior);
        var followingSnapshot = Snapshot(fixture.Following);
        var cut = split switch { 0 => (0, 0), 2 => (0, 2), 3 => (1, 1), 4 => (1, 2), 6 => (2, 2), _ => throw new InvalidOperationException() };

        fixture.Bus.Execute(new InsertShapeTextParagraphBreakCommand(0, 0, 1, cut.Item1, cut.Item2, fixture.ChildPath));
        var first = fixture.Leaf.TextParagraphs[1];
        var second = fixture.Leaf.TextParagraphs[2];
        AssertSplit();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            fixture.Bus.Undo().Should().BeTrue();
            fixture.Leaf.TextParagraphs.Should().HaveCount(3);
            fixture.Leaf.TextParagraphs[1].Should().BeSameAs(source);
            AssertSnapshot(source, sourceSnapshot);
            fixture.AssertNeighbors(priorSnapshot, followingSnapshot);
            fixture.Bus.Redo().Should().BeTrue();
            fixture.Leaf.TextParagraphs[1].Should().BeSameAs(first);
            fixture.Leaf.TextParagraphs[2].Should().BeSameAs(second);
            AssertSplit();
        }

        void AssertSplit()
        {
            using var assertions = new AssertionScope();
            fixture.Leaf.TextParagraphs.Should().HaveCount(4);
            first.Should().NotBeSameAs(source);
            second.Should().NotBeSameAs(source);
            first.PlainText.Should().Be("abcdef"[..split]);
            second.PlainText.Should().Be("abcdef"[split..]);
            first.BookmarkNames.Should().Equal("legacy", "sibling");
            second.BookmarkNames.Should().BeEmpty();
            first.BookmarkBoundaries.Should().Equal(expectedFirst);
            second.BookmarkBoundaries.Should().Equal(expectedSecond);
            first.BookmarkBoundaries.Count.Should().Be(expectedFirst.Length);
            second.BookmarkBoundaries.Count.Should().Be(expectedSecond.Length);
            AssertSnapshot(source, sourceSnapshot);
            fixture.AssertNeighbors(priorSnapshot, followingSnapshot);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SplitPreservesBoundaryOrderAfterZeroWidthRunsAtTheCut(bool grouped, bool collapsed)
    {
        var source = ParagraphWithRuns("ab", "", "cd");
        var start = Start("zero-width", 2);
        var end = End("zero-width", collapsed ? 2 : 3);
        source.BookmarkBoundaries.AddRange([start, end]);
        var fixture = new Fixture(grouped, source);
        var snapshot = Snapshot(source);

        fixture.Bus.Execute(new InsertShapeTextParagraphBreakCommand(0, 0, 1, 0, 2, fixture.ChildPath));
        AssertResult();
        fixture.Bus.Undo().Should().BeTrue();
        fixture.Leaf.TextParagraphs[1].Should().BeSameAs(source);
        AssertSnapshot(source, snapshot);
        fixture.Bus.Redo().Should().BeTrue();
        AssertResult();

        void AssertResult()
        {
            using var assertions = new AssertionScope();
            fixture.Leaf.TextParagraphs[1].BookmarkBoundaries.Should().BeEmpty();
            var tail = fixture.Leaf.TextParagraphs[2];
            tail.Runs.Select(run => run.Text).Should().Equal("", "cd");
            tail.BookmarkBoundaries.Should().Equal(start with { RunIndex = 1 }, end with { RunIndex = collapsed ? 1 : 2 });
            AssertSnapshot(source, snapshot);
            fixture.AssertMirror();
        }
    }

    public static IEnumerable<object[]> MergeCases()
    {
        foreach (var grouped in new[] { false, true })
        foreach (var scenario in new[] { "later-range", "crossing", "collapsed-seam", "later-zero-width" })
            yield return [grouped, scenario];
    }

    [Theory]
    [MemberData(nameof(MergeCases))]
    public void MergeRebasesLaterMarkersWithoutDroppingNamesOrCrossParagraphEndpoints(bool grouped, string scenario)
    {
        var first = ParagraphWithRuns("ab", "cd");
        var second = ParagraphWithRuns(scenario == "later-zero-width" ? "" : "ef", "gh");
        first.BookmarkNames.AddRange(["earlier", "shared"]);
        second.BookmarkNames.AddRange(["later", "shared"]);
        BookmarkBoundary[] expected;
        switch (scenario)
        {
            case "later-range":
                first.BookmarkBoundaries.AddRange([Start("earlier", 0), End("earlier", 1)]);
                second.BookmarkBoundaries.AddRange([Start("later", 0), End("later", 2)]);
                expected = [Start("earlier", 0), End("earlier", 1), Start("later", 2), End("later", 4)]; break;
            case "crossing":
                first.BookmarkBoundaries.Add(Start("span", 1));
                second.BookmarkBoundaries.Add(End("span", 1));
                expected = [Start("span", 1), End("span", 3)]; break;
            case "collapsed-seam":
                first.BookmarkBoundaries.AddRange([Start("left-point", 2), End("left-point", 2)]);
                second.BookmarkBoundaries.AddRange([Start("right-point", 0), End("right-point", 0)]);
                expected = [Start("left-point", 2), End("left-point", 2), Start("right-point", 2), End("right-point", 2)]; break;
            case "later-zero-width":
                second.BookmarkBoundaries.AddRange([Start("later-point", 1), End("later-point", 2)]);
                expected = [Start("later-point", 3), End("later-point", 4)]; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var fixture = new Fixture(grouped, first, second);
        var firstSnapshot = Snapshot(first);
        var secondSnapshot = Snapshot(second);
        var priorSnapshot = Snapshot(fixture.Prior);
        var followingSnapshot = Snapshot(fixture.Following);

        fixture.Bus.Execute(new MergeShapeTextParagraphWithPreviousCommand(0, 0, 2, fixture.ChildPath));
        var merged = fixture.Leaf.TextParagraphs[1];
        AssertMerged();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            fixture.Bus.Undo().Should().BeTrue();
            fixture.Leaf.TextParagraphs.Should().HaveCount(4);
            fixture.Leaf.TextParagraphs[1].Should().BeSameAs(first);
            fixture.Leaf.TextParagraphs[2].Should().BeSameAs(second);
            AssertSnapshot(first, firstSnapshot);
            AssertSnapshot(second, secondSnapshot);
            fixture.AssertNeighbors(priorSnapshot, followingSnapshot);
            fixture.Bus.Redo().Should().BeTrue();
            fixture.Leaf.TextParagraphs[1].Should().BeSameAs(merged);
            AssertMerged();
        }

        void AssertMerged()
        {
            using var assertions = new AssertionScope();
            fixture.Leaf.TextParagraphs.Should().HaveCount(3);
            merged.Should().NotBeSameAs(first);
            merged.Should().NotBeSameAs(second);
            merged.PlainText.Should().Be(first.PlainText + second.PlainText);
            merged.Runs.Should().HaveCount(4);
            merged.BookmarkNames.Should().Equal("earlier", "shared", "later");
            merged.BookmarkBoundaries.Should().Equal(expected);
            AssertSnapshot(first, firstSnapshot);
            AssertSnapshot(second, secondSnapshot);
            fixture.AssertNeighbors(priorSnapshot, followingSnapshot);
        }
    }

    private static Paragraph ParagraphWithRuns(params string[] texts)
    {
        var paragraph = new Paragraph();
        foreach (var (text, index) in texts.Select((text, index) => (text, index)))
            paragraph.Runs.Add(new Run(text, RunFormatting.Default with { Bold = index == 0, Italic = index == 1 }));
        return paragraph;
    }

    private static BookmarkBoundary Start(string key, int index) =>
        new(key, BookmarkBoundaryKind.Start, index, key + "-name", 1, 3, "next");
    private static BookmarkBoundary End(string key, int index) => new(key, BookmarkBoundaryKind.End, index);

    private sealed record ParagraphSnapshot(string Text, string[] Names, BookmarkBoundary[] Boundaries, Run[] Runs);
    private static ParagraphSnapshot Snapshot(Paragraph paragraph) =>
        new(paragraph.PlainText, paragraph.BookmarkNames.ToArray(), paragraph.BookmarkBoundaries.ToArray(), paragraph.Runs.ToArray());
    private static void AssertSnapshot(Paragraph paragraph, ParagraphSnapshot snapshot)
    {
        paragraph.PlainText.Should().Be(snapshot.Text);
        paragraph.BookmarkNames.Should().Equal(snapshot.Names);
        paragraph.BookmarkBoundaries.Should().Equal(snapshot.Boundaries);
        paragraph.Runs.Should().Equal(snapshot.Runs, (actual, expected) => ReferenceEquals(actual, expected));
    }

    private sealed class Fixture
    {
        public TextDocument Document { get; } = new();
        public Shape Leaf { get; } = Shape.TextBoxWith("", 120, 100);
        public Paragraph Prior { get; } = Neighbor("prior");
        public Paragraph Following { get; } = Neighbor("following");
        public Run Owner { get; }
        public DrawingGroup? Group { get; }
        public IReadOnlyList<int>? ChildPath { get; }
        public DocumentCommandBus Bus { get; }
        public Fixture(bool grouped, params Paragraph[] source)
        {
            Leaf.TextParagraphs.Clear();
            Leaf.TextParagraphs.Add(Prior);
            Leaf.TextParagraphs.AddRange(source);
            Leaf.TextParagraphs.Add(Following);
            if (grouped)
            {
                var inner = new DrawingGroup();
                inner.Children.Add(Shape.Preset(ShapeKind.Rectangle, 10, 10));
                inner.Children.Add(Leaf);
                Group = new DrawingGroup();
                Group.Children.Add(inner);
                Group.Children.Add(Shape.Preset(ShapeKind.Ellipse, 10, 10));
                Owner = Run.FromDrawingGroup(Group);
                ChildPath = [0, 1];
            }
            else Owner = Run.FromShape(Leaf);
            var host = new Paragraph();
            host.Runs.Add(Owner);
            Document.Blocks.Add(host);
            Bus = new DocumentCommandBus(new Context(Document));
        }
        public void AssertNeighbors(ParagraphSnapshot prior, ParagraphSnapshot following)
        {
            Leaf.TextParagraphs[0].Should().BeSameAs(Prior);
            Leaf.TextParagraphs[^1].Should().BeSameAs(Following);
            AssertSnapshot(Prior, prior);
            AssertSnapshot(Following, following);
            AssertMirror();
        }
        public void AssertMirror()
        {
            Owner.Text.Should().Be(Group is null ? Leaf.PlainText : string.Empty);
            if (Group is not null)
            {
                Owner.DrawingGroup.Should().BeSameAs(Group);
                Group.Children.Should().HaveCount(2);
                ((DrawingGroup)Group.Children[0]).Children[1].Should().BeSameAs(Leaf);
            }
        }
        private static Paragraph Neighbor(string name)
        {
            var paragraph = ParagraphWithRuns(name);
            paragraph.BookmarkNames.Add(name + "-legacy");
            paragraph.BookmarkBoundaries.AddRange([Start(name, 0), End(name, 1)]);
            return paragraph;
        }
    }
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
