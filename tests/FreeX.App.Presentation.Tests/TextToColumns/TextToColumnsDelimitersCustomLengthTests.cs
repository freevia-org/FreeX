using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// R25-text-import-wizard-2: the WPF "Other" delimiter textbox has no MaxLength, so a
/// multi-character custom delimiter (e.g. "-&gt;") must not be passed through verbatim --
/// otherwise TextToColumnsSplitter.IsDelimiter treats any string longer than one character as
/// a character SET, silently splitting on every character in it instead of the literal
/// sequence, and diverging from both the Avalonia dialog (MaxLength = 1) and real Excel
/// (which never allows more than one character in the Other box).
/// </summary>
public sealed class TextToColumnsDelimitersCustomLengthTests
{
    [Fact]
    public void DelimiterFor_Custom_MultiCharacterInput_TruncatesToFirstCharacter()
    {
        // Bug case: a 2-character "Other" delimiter must be reduced to its first character,
        // not passed through whole (which would later be mis-split as a character set).
        TextToColumnsDelimiters.DelimiterFor(TextToColumnsDelimiterKind.Custom, "->")
            .Should().Be("-");
    }

    [Fact]
    public void DelimiterFor_Custom_SingleCharacterInput_IsUnaffected()
    {
        // Sibling/already-working case: a genuine single-character custom delimiter must keep
        // working exactly as before -- this must not regress.
        TextToColumnsDelimiters.DelimiterFor(TextToColumnsDelimiterKind.Custom, "|")
            .Should().Be("|");
    }

    [Fact]
    public void CreatePlan_Custom_MultiCharacterInput_TruncatesToFirstCharacter()
    {
        // The dialog's real entry point (CreatePlan, used by both CreateResult overloads) must
        // also truncate, since it delegates to DelimiterFor internally.
        var plan = TextToColumnsDelimiters.CreatePlan(
            [TextToColumnsDelimiterKind.Custom],
            "->");

        plan.Delimiters.Should().Be("-");
        plan.PrimaryKind.Should().Be(TextToColumnsDelimiterKind.Custom);
    }

    [Fact]
    public void CreatePlan_ComboWithSingleCharacterCustom_StillConcatenatesAsBefore()
    {
        // Sibling case: combining Custom with other checked delimiter kinds must still
        // concatenate normally when the custom delimiter is already a single character.
        var plan = TextToColumnsDelimiters.CreatePlan(
            [TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Custom],
            "|");

        plan.Delimiters.Should().Be(",|");
    }
}
