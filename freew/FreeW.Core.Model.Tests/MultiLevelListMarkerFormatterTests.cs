namespace FreeW.Core.Model.Tests;

public sealed class MultiLevelListMarkerFormatterTests
{
    [Fact]
    public void MarkerSequence_UsesDecimalByDefault()
    {
        var markers = MultiLevelListMarkerFormatter.MarkerSequence([0, 1, 2, 1, 0]);

        markers.Should().Equal("1.", "1.1.", "1.1.1.", "1.2.", "2.");
    }

    [Fact]
    public void MarkerSequence_UsesPerLevelNumberFormats()
    {
        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1, 2, 1, 0],
            MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);

        markers.Should().Equal("1.", "1.a.", "1.a.i.", "1.b.", "2.");
    }

    /// <summary>
    /// R143 fix (freew-numbering-lvltext-discarded): a captured DOCX <c>w:lvlText</c> pattern must be
    /// honoured verbatim -- including a non-dot separator and a level whose pattern already encodes ALL
    /// its ancestor placeholders in one string (the real OOXML shape: level 1's own lvlText is
    /// <c>"%1.%2)"</c>, not built by concatenating each ancestor's independently-formatted "N." text) --
    /// instead of the previously hardcoded accumulated "N.N.N." pattern.
    /// </summary>
    [Fact]
    public void MarkerSequence_HonoursCapturedLvlTextPatternInsteadOfHardcodedDots()
    {
        IReadOnlyList<string?> levelTexts = ["%1)", "%1.%2)"];

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1, 1],
            MultiLevelListFormat.DecimalNumberFormats,
            levelTexts);

        markers.Should().Equal("1)", "1.1)", "1.2)");
    }

    /// <summary>
    /// Literal prefix/suffix text in a captured lvlText (e.g. legal "Article %1:" numbering) must be
    /// copied through untouched, not replaced by the old hardcoded ". " separator scheme.
    /// </summary>
    [Fact]
    public void MarkerSequence_LiteralTextInLvlTextIsPreservedVerbatim()
    {
        IReadOnlyList<string?> levelTexts = ["Article %1:"];

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 0],
            MultiLevelListFormat.DecimalNumberFormats,
            levelTexts);

        markers.Should().Equal("Article 1:", "Article 2:");
    }

    /// <summary>
    /// SIBLING / no-regression coverage: a level with no captured lvlText (null entry, or a shorter
    /// levelTexts list than the level being rendered) must still fall back to the classic dotted outline
    /// pattern this formatter has always produced -- FreeW's own "Define new Multilevel list" styles never
    /// populate levelTexts at all and must keep rendering unchanged.
    /// </summary>
    [Fact]
    public void MarkerSequence_NullLevelTextFallsBackToDottedPattern()
    {
        IReadOnlyList<string?> levelTexts = ["%1)", null];

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1],
            MultiLevelListFormat.DecimalNumberFormats,
            levelTexts);

        markers.Should().Equal("1)", "1.1.");
    }

    [Fact]
    public void FormatNumber_SupportsLettersAndRomans()
    {
        MultiLevelListMarkerFormatter.FormatNumber(27, ListNumberFormat.LowerLetter).Should().Be("aa");
        MultiLevelListMarkerFormatter.FormatNumber(4, ListNumberFormat.UpperRoman).Should().Be("IV");
        MultiLevelListMarkerFormatter.ToOoxmlToken(ListNumberFormat.LowerRoman).Should().Be("lowerRoman");
        MultiLevelListMarkerFormatter.FromOoxmlToken("upperLetter").Should().Be(ListNumberFormat.UpperLetter);
    }
}
