namespace FreeW.Core.Model.Tests;

/// <summary>
/// r193: <see cref="DropCap.ApplyDropCap"/> split the leading run with <c>Text[..1]</c> -- one UTF-16
/// char. For any character outside the BMP that cuts a surrogate pair in half, leaving a lone high
/// surrogate in the cap run and a lone low surrogate at the head of the remainder. In this codebase a
/// lone surrogate in model text is XML-illegal and the sanitizer chokepoints abort the WHOLE save
/// when one reaches a writer, so the consequence is a document that cannot be saved at all -- not a
/// rendering glitch. Taking a grapheme cluster also keeps a base letter with its combining marks
/// together, which is what a drop cap should show.
/// </summary>
public class R193_DropCapTextElementTests
{
    private static Paragraph ParagraphStartingWith(string text) => new(text);

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }

    [Theory]
    // A non-BMP emoji, then the same emoji as the sole content, then an astral letter.
    [InlineData("\U0001F600 rest of the paragraph")]
    [InlineData("\U0001F600rest")]
    [InlineData("\U00010400bcd")]
    public void ApplyDropCap_OnANonBmpLeadingCharacter_NeverProducesALoneSurrogate(string text)
    {
        var paragraph = ParagraphStartingWith(text);

        DropCap.ApplyDropCap(paragraph);

        foreach (var run in paragraph.Runs)
        {
            HasLoneSurrogate(run.Text).Should().BeFalse(
                "a lone surrogate is XML-illegal and aborts the whole save; run text was '{0}'",
                run.Text);
        }

        // And the text must survive intact across the split.
        string.Concat(paragraph.Runs.Select(r => r.Text)).Should().Be(text);
    }

    [Fact]
    public void ApplyDropCap_OnANonBmpLeadingCharacter_PutsTheWholeCharacterInTheCapRun()
    {
        var paragraph = ParagraphStartingWith("\U0001F600rest");

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs[0].Text.Should().Be("\U0001F600", "the cap is the whole character");
        paragraph.Runs[1].Text.Should().Be("rest");
    }

    [Fact]
    public void ApplyDropCap_OnACombiningSequence_KeepsTheMarksWithTheirBaseLetter()
    {
        // "e" + combining acute: a drop cap of the bare "e" would drop the accent onto the body text.
        var paragraph = ParagraphStartingWith("école");

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs[0].Text.Should().Be("é");
        paragraph.Runs[1].Text.Should().Be("cole");
    }

    [Fact]
    public void ApplyDropCap_WhenTheRunIsExactlyOneNonBmpCharacter_EnlargesItInPlace()
    {
        // The single-element early return used to compare against Length == 1, which a surrogate
        // pair never satisfies -- so this fell through to the splitting path and produced an empty
        // remainder run plus a broken pair.
        var paragraph = ParagraphStartingWith("\U0001F600");

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Text.Should().Be("\U0001F600");
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyDropCap_OnOrdinaryAsciiText_IsUnchanged()
    {
        var paragraph = ParagraphStartingWith("Hello world");

        DropCap.ApplyDropCap(paragraph);

        paragraph.Runs[0].Text.Should().Be("H");
        paragraph.Runs[1].Text.Should().Be("ello world");
    }
}
