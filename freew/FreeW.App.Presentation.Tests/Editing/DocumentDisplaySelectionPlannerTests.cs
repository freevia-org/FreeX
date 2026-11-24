using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentDisplaySelectionPlannerTests
{
    [Theory]
    [InlineData("", "2026-11-24")]
    [InlineData("old", "2026-11-24")]
    [InlineData("very long cached result", "1")]
    public void LiteralSuffixMapsToItsOwnRunAfterDifferentlySizedFields(string cached, string display)
    {
        var field = Run.DateField(cached);
        var suffix = new Run(" suffix");
        var spans = new[] { Atomic(0, field, display), Literal(1, suffix) };

        var plan = DocumentDisplaySelectionPlanner.Plan(spans, display.Length + 1, display.Length + 4);

        plan.SelectedText.Should().Be("suf");
        plan.CanMutate.Should().BeTrue();
        var literal = plan.LiteralRanges.Should().ContainSingle().Subject;
        literal.Span.SourceRun.Should().BeSameAs(suffix);
        literal.Span.SourceRunIndex.Should().Be(1);
        literal.ModelStart.Should().Be(1);
        literal.ModelLength.Should().Be(3);
        plan.WholeAtomicRuns.Should().BeEmpty();
        field.Text.Should().Be(cached);
        field.FieldKind.Should().Be(RunFieldKind.Date);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("equation")]
    [InlineData("note")]
    public void WholeAtomicSelectionCarriesIdentityEvenWithoutCachedText(string kind)
    {
        var run = kind switch
        {
            "date" => Run.DateField(),
            "note" => Run.FootnoteReference(403),
            _ => new Run("") { Equation = new Equation() }
        };
        var display = kind == "date" ? "Nov 24" : kind == "note" ? "1" : "x+y";

        var plan = DocumentDisplaySelectionPlanner.Plan([Atomic(0, run, display)], 0, display.Length);

        plan.SelectedText.Should().Be(display);
        plan.CanMutate.Should().BeTrue();
        plan.WholeAtomicRuns.Should().ContainSingle().Which.SourceRun.Should().BeSameAs(run);
        plan.LiteralRanges.Should().BeEmpty();
        if (kind == "note")
            run.FootnoteId.Should().Be(403);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    public void PartialAtomicEndpointsRefuseMutationWithoutExpandingDisplaySelection(int anchor, int active)
    {
        var field = Run.DateField("cached");
        var suffix = new Run("x");
        var spans = new[] { Atomic(0, field, "DATE"), Literal(1, suffix) };

        var plan = DocumentDisplaySelectionPlanner.Plan(spans, anchor, active);

        plan.SelectedText.Should().Be("DATEx"[Math.Min(anchor, active)..Math.Max(anchor, active)]);
        plan.CanMutate.Should().BeFalse();
        plan.Refusal.Should().Be(DocumentDisplaySelectionRefusal.PartialAtomicRun);
        plan.LiteralRanges.Should().BeEmpty();
        plan.WholeAtomicRuns.Should().BeEmpty();
    }

    [Fact]
    public void RefusalDiscardsEarlierAcceptedSlicesSoCutCannotPartiallyCommit()
    {
        var spans = new[] { Literal(0, new Run("prefix")), Atomic(1, Run.DateField(), "DATE") };

        var plan = DocumentDisplaySelectionPlanner.Plan(spans, 0, 8);

        plan.SelectedText.Should().Be("prefixDA");
        plan.Refusal.Should().Be(DocumentDisplaySelectionRefusal.PartialAtomicRun);
        plan.LiteralRanges.Should().BeEmpty();
    }

    [Fact]
    public void EmptyDisplayRunsAreAlwaysPreservedInSourceOrder()
    {
        var hiddenField = Run.DateField("hidden cached text");
        var commentReference = new Run("") { IsCommentReference = true, CommentId = 7 };
        var emptyControl = new Run("") { Control = new ContentControl(ContentControlKind.PlainText) };
        var text = new Run("text");
        var spans = new[]
        {
            Atomic(0, hiddenField, ""), Literal(1, text),
            Atomic(2, commentReference, ""), Literal(3, emptyControl)
        };

        var plan = DocumentDisplaySelectionPlanner.Plan(spans, -20, 99);

        plan.DisplayStart.Should().Be(0);
        plan.DisplayEnd.Should().Be(4);
        plan.SelectedText.Should().Be("text");
        plan.PreservedZeroWidthRuns.Select(span => span.SourceRun)
            .Should().Equal(hiddenField, commentReference, emptyControl);
        plan.WholeAtomicRuns.Should().BeEmpty();
        plan.LiteralRanges.Should().ContainSingle().Which.Span.SourceRun.Should().BeSameAs(text);
        commentReference.CommentId.Should().Be(7);
        emptyControl.Control.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void ExplicitBoundaryMapSelectsTheWholeModelCrLfForOneDisplayBreak(int anchor, int active)
    {
        var run = new Run("a\r\nb");
        var span = new DocumentRunDisplaySpan(0, run, "a\nb", IsAtomic: false, DisplayToModelBoundaries: [0, 1, 3, 4]);

        var plan = DocumentDisplaySelectionPlanner.Plan([span], anchor, active);

        plan.SelectedText.Should().Be("\n");
        plan.CanMutate.Should().BeTrue();
        var literal = plan.LiteralRanges.Should().ContainSingle().Subject;
        literal.ModelStart.Should().Be(1);
        literal.ModelLength.Should().Be(2);
        run.Text.Substring(literal.ModelStart, literal.ModelLength).Should().Be("\r\n");
    }

    [Fact]
    public void LengthChangingLiteralProjectionRequiresAnExplicitMap()
    {
        var plan = DocumentDisplaySelectionPlanner.Plan(
            [new DocumentRunDisplaySpan(0, new Run("a\r\nb"), "a\nb", IsAtomic: false)], 1, 2);

        plan.Refusal.Should().Be(DocumentDisplaySelectionRefusal.MissingLiteralBoundaryMap);
        plan.CanMutate.Should().BeFalse();
        plan.LiteralRanges.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitlyUnmappedBoundaryRefusesAnExactMutation()
    {
        var span = new DocumentRunDisplaySpan(0, new Run("ab"), "a-b", IsAtomic: false, DisplayToModelBoundaries: [0, -1, 1, 2]);

        var plan = DocumentDisplaySelectionPlanner.Plan([span], 1, 3);

        plan.SelectedText.Should().Be("-b");
        plan.Refusal.Should().Be(DocumentDisplaySelectionRefusal.UnmappedLiteralBoundary);
        plan.CanMutate.Should().BeFalse();
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("e\u0301")]
    [InlineData("👩‍💻")]
    public void UnicodeSelectionUsesUtf16RangesWithoutChangingText(string element)
    {
        var run = new Run("a" + element + "b");

        var plan = DocumentDisplaySelectionPlanner.Plan([Literal(0, run)], 1 + element.Length, 1);

        plan.SelectedText.Should().Be(element);
        plan.CanMutate.Should().BeTrue();
        plan.LiteralRanges.Should().ContainSingle().Which.ModelLength.Should().Be(element.Length);
        run.Text.Should().Be("a" + element + "b");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InteriorSurrogateEndpointsCannotProducePartialModelCharacters(bool splitRuns)
    {
        var spans = splitRuns
            ? new[] { Literal(0, new Run("\uD83D")), Literal(1, new Run("\uDE00")) }
            : new[] { Literal(0, new Run("😀")) };

        var plan = DocumentDisplaySelectionPlanner.Plan(spans, 0, 1);

        plan.Refusal.Should().Be(DocumentDisplaySelectionRefusal.SplitSurrogatePair);
        plan.CanMutate.Should().BeFalse();
        plan.LiteralRanges.Should().BeEmpty();
    }

    [Fact]
    public void MixedSelectionKeepsAtomicIdentityAndLiteralRunLocalOffsets()
    {
        var left = new Run("left");
        var note = Run.FootnoteReference(403);
        var right = new Run("right");

        var plan = DocumentDisplaySelectionPlanner.Plan(
            [Literal(0, left), Atomic(1, note, "1"), Literal(2, right)], 7, 2);

        plan.SelectedText.Should().Be("ft1ri");
        plan.CanMutate.Should().BeTrue();
        plan.LiteralRanges.Select(slice => (slice.Span.SourceRunIndex, slice.ModelStart, slice.ModelLength))
            .Should().Equal((0, 2, 2), (2, 0, 2));
        plan.WholeAtomicRuns.Should().ContainSingle().Which.SourceRun.Should().BeSameAs(note);
    }

    [Fact]
    public void EmptyAndCollapsedSelectionsHaveNoMutation()
    {
        var empty = DocumentDisplaySelectionPlanner.Plan([], -10, 20);
        empty.SelectedText.Should().BeEmpty();
        empty.CanMutate.Should().BeFalse();
        empty.Refusal.Should().Be(DocumentDisplaySelectionRefusal.None);

        var collapsed = DocumentDisplaySelectionPlanner.Plan([Atomic(0, Run.DateField(), "DATE")], 2, 2);
        collapsed.SelectedText.Should().BeEmpty();
        collapsed.CanMutate.Should().BeFalse();
        collapsed.LiteralRanges.Should().BeEmpty();
        collapsed.WholeAtomicRuns.Should().BeEmpty();
    }

    [Fact]
    public void MalformedBoundaryMapsAndDuplicateRunIndexesAreRejected()
    {
        Action badLength = () => DocumentDisplaySelectionPlanner.Plan(
            [new DocumentRunDisplaySpan(0, new Run("ab"), "ab", IsAtomic: false, DisplayToModelBoundaries: [0, 2])], 0, 2);
        Action backwards = () => DocumentDisplaySelectionPlanner.Plan(
            [new DocumentRunDisplaySpan(0, new Run("abc"), "abc", IsAtomic: false, DisplayToModelBoundaries: [0, 2, 1, 3])], 0, 3);
        Action duplicate = () => DocumentDisplaySelectionPlanner.Plan(
            [Literal(0, new Run("a")), Literal(0, new Run("b"))], 0, 2);

        badLength.Should().Throw<ArgumentException>();
        backwards.Should().Throw<ArgumentException>();
        duplicate.Should().Throw<ArgumentException>();
    }

    private static DocumentRunDisplaySpan Literal(int index, Run run) => new(index, run, run.Text, IsAtomic: false);
    private static DocumentRunDisplaySpan Atomic(int index, Run run, string text) => new(index, run, text, IsAtomic: true);
}
