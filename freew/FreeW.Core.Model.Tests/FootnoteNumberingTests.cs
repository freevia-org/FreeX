namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for <see cref="NoteNumberingOptions"/>, <see cref="NoteNumberFormat"/>,
/// <see cref="NoteNumberRestart"/> and the footnote/endnote numbering properties on
/// <see cref="TextDocument"/>.
/// </summary>
public class FootnoteNumberingTests
{
    // ── NoteNumberingOptions defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoteNumberingOptions_DefaultInstance_IsDefault()
    {
        var opts = new NoteNumberingOptions();
        opts.IsDefault.Should().BeTrue();
        opts.NumberFormat.Should().Be(NoteNumberFormat.Decimal);
        opts.StartAt.Should().Be(1);
        opts.NumberRestart.Should().Be(NoteNumberRestart.Continuous);
    }

    [Fact]
    public void NoteNumberingOptions_ChangedFormat_NotDefault()
    {
        var opts = new NoteNumberingOptions { NumberFormat = NoteNumberFormat.LowerRoman };
        opts.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void NoteNumberingOptions_ChangedStartAt_NotDefault()
    {
        var opts = new NoteNumberingOptions { StartAt = 2 };
        opts.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void NoteNumberingOptions_ChangedRestart_NotDefault()
    {
        var opts = new NoteNumberingOptions { NumberRestart = NoteNumberRestart.EachSection };
        opts.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void NoteNumberingOptions_AllDefault_IsDefault()
    {
        var opts = new NoteNumberingOptions
        {
            NumberFormat = NoteNumberFormat.Decimal,
            StartAt = 1,
            NumberRestart = NoteNumberRestart.Continuous
        };
        opts.IsDefault.Should().BeTrue();
    }

    // ── TextDocument exposes footnote/endnote numbering independently ─────────────────────────────

    [Fact]
    public void TextDocument_FootnoteNumbering_DefaultInstance()
    {
        var doc = new TextDocument();
        doc.FootnoteNumbering.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TextDocument_EndnoteNumbering_DefaultInstance()
    {
        var doc = new TextDocument();
        doc.EndnoteNumbering.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TextDocument_FootnoteAndEndnoteNumbering_AreIndependent()
    {
        var doc = new TextDocument();
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.Chicago;
        doc.FootnoteNumbering.StartAt = 3;

        // Endnote numbering should remain at default.
        doc.EndnoteNumbering.IsDefault.Should().BeTrue();
        doc.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.Decimal);
    }

    // ── NoteNumberFormat enum coverage ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NoteNumberFormat.Decimal)]
    [InlineData(NoteNumberFormat.LowerRoman)]
    [InlineData(NoteNumberFormat.UpperRoman)]
    [InlineData(NoteNumberFormat.LowerLetter)]
    [InlineData(NoteNumberFormat.UpperLetter)]
    [InlineData(NoteNumberFormat.Chicago)]
    public void NoteNumberFormat_AllValues_CanBeSetOnOptions(NoteNumberFormat format)
    {
        var opts = new NoteNumberingOptions { NumberFormat = format };
        opts.NumberFormat.Should().Be(format);
    }

    // ── NoteNumberRestart enum coverage ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NoteNumberRestart.Continuous)]
    [InlineData(NoteNumberRestart.EachSection)]
    [InlineData(NoteNumberRestart.EachPage)]
    public void NoteNumberRestart_AllValues_CanBeSetOnOptions(NoteNumberRestart restart)
    {
        var opts = new NoteNumberingOptions { NumberRestart = restart };
        opts.NumberRestart.Should().Be(restart);
    }
}
