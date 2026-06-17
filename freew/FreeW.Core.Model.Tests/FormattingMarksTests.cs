namespace FreeW.Core.Model.Tests;

public class FormattingMarksTests
{
    [Fact]
    public void Annotate_AppendsPilcrow_AtParagraphEnd()
    {
        FormattingMarks.Annotate("Hello").Should().Be("Hello¶");
    }

    [Fact]
    public void Annotate_NullAndEmpty_ProduceJustThePilcrow()
    {
        FormattingMarks.Annotate(null).Should().Be("¶");
        FormattingMarks.Annotate(string.Empty).Should().Be("¶");
    }

    [Fact]
    public void Annotate_ReplacesSpacesWithMiddleDots()
    {
        FormattingMarks.Annotate("a b c").Should().Be("a·b·c¶");
    }

    [Fact]
    public void Annotate_ReplacesTabsWithArrows()
    {
        FormattingMarks.Annotate("a\tb").Should().Be("a→b¶");
    }

    [Fact]
    public void Annotate_LeavesOtherCharactersUntouched()
    {
        FormattingMarks.Annotate("X\tY Z").Should().Be("X→Y·Z¶");
    }

    [Fact]
    public void Glyphs_AreTheExpectedCodePoints()
    {
        FormattingMarks.Pilcrow.Should().Be('¶');
        FormattingMarks.SpaceDot.Should().Be('·');
        FormattingMarks.TabArrow.Should().Be('→');
    }

    [Fact]
    public void Annotate_DoesNotMutate_TheOriginalText()
    {
        // The annotation is display-only: it returns a new string and never alters its input. This
        // mirrors the editor invariant that formatting marks never enter the document model/text.
        const string original = "a b\tc";
        var annotated = FormattingMarks.Annotate(original);

        original.Should().Be("a b\tc");
        annotated.Should().NotContain(" ").And.NotContain("\t");
        annotated.Should().Be("a·b→c¶");
    }
}
