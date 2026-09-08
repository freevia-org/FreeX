using System.Globalization;
using FluentAssertions;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r551: r550 asserted that the app and presentation layers were safe because a parsed value "is
/// range-validated immediately after". Verifying that claim rather than repeating it showed it holds
/// for most planners and NOT for this one, which is the finding.
///
/// <para>The distinction is polarity. The accept form used elsewhere in this layer --
/// <c>width > 0 &amp;&amp; width &lt;= 12</c> -- rejects both Infinity and NaN, because NaN fails
/// every comparison and Infinity fails the upper bound. The reject form here --
/// <c>|| parsedSize &lt;= 0</c> -- rejects NEITHER: <c>NaN &lt;= 0</c> is false, and there is no
/// upper bound for Infinity to fail. The two spellings read as the same rule and are not.</para>
/// </summary>
public sealed class R551_DialogPlannersRejectNonFiniteInputTests
{
    private static FontDialogInput InputWith(string fontSize, string kerning = "") => new(
        FontFamilyText: "Calibri",
        FontSizeText: fontSize,
        ColorIndex: 0,
        Bold: false,
        Italic: false,
        Underline: false,
        Strikethrough: false,
        SmallCaps: false,
        AllCaps: false,
        Superscript: false,
        Subscript: false,
        CharacterSpacingText: "0",
        KerningMinSizeText: kerning,
        PositionText: "0",
        LigatureIndex: 0,
        StylisticSetText: "",
        NumberFormIndex: 0,
        NumberSpacingIndex: 0);

    private static bool TryBuild(FontDialogInput input) =>
        FontDialogPlanner.TryBuildResult(
            input, new RunFormatting(), CultureInfo.InvariantCulture, out _, out _);

    [Theory]
    [InlineData("1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_font_size_that_is_not_a_number_is_rejected(string text)
    {
        // Without the finite test this returned true and put Infinity or NaN into RunFormatting,
        // from which text layout reads it.
        TryBuild(InputWith(text)).Should().BeFalse();
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void A_kerning_threshold_that_is_not_a_number_is_rejected(string text)
    {
        TryBuild(InputWith("11", text)).Should().BeFalse();
    }

    [Theory]
    [InlineData("11")]
    [InlineData("0.5")]
    [InlineData("1638")]
    public void Ordinary_font_sizes_are_still_accepted(string text)
    {
        // Non-vacuity: a guard that rejected everything would satisfy the assertions above. 1638 is
        // Word's own maximum, so the upper end of the real range must survive.
        TryBuild(InputWith(text)).Should().BeTrue();
    }
}
