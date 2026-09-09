using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r572: r551 fixed <see cref="FontDialogPlanner"/>'s font-size check and, in the same method,
/// guarded the KERNING path with an explicit <c>!double.IsFinite(parsedKerning)</c>. Twenty lines
/// above it, CHARACTER SPACING and POSITION go through a bare <c>TryParseRequiredDouble</c> helper
/// with no bound of any kind, so "NaN" and "1e400" typed into either box land in
/// <see cref="RunFormatting"/>, which text layout reads -- the exact consequence r551 recorded for
/// font size.
///
/// <para>This was found by re-checking what r551 CLEARED after r571 corrected r551's accept-form
/// rule. The correction obliges revisiting the clearances the old rule justified, and one guarded
/// path in a method says nothing about its neighbours.</para>
/// </summary>
public sealed class R572_FontDialogSpacingAndPositionAreFiniteTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static FontDialogInput InputWith(string spacing = "0", string position = "0") =>
        new(
            FontFamilyText: "Calibri",
            FontSizeText: "11",
            ColorIndex: 0,
            Bold: false,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            SmallCaps: false,
            AllCaps: false,
            Superscript: false,
            Subscript: false,
            CharacterSpacingText: spacing,
            KerningMinSizeText: "",
            PositionText: position,
            LigatureIndex: 0,
            StylisticSetText: "",
            NumberFormIndex: 0,
            NumberSpacingIndex: 0);

    private static bool TryBuild(FontDialogInput input, out RunFormatting? result) =>
        FontDialogPlanner.TryBuildResult(input, new RunFormatting(), Invariant, out result, out _);

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public void A_character_spacing_that_is_not_finite_is_rejected(string hostile)
    {
        var built = TryBuild(InputWith(spacing: hostile), out var result);

        (!built || result is null || double.IsFinite(result.CharacterSpacingPt))
            .Should().BeTrue("CharacterSpacingPt was " + result?.CharacterSpacingPt);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void A_position_that_is_not_finite_is_rejected(string hostile)
    {
        var built = TryBuild(InputWith(position: hostile), out var result);

        (!built || result is null || double.IsFinite(result.PositionPt))
            .Should().BeTrue("PositionPt was " + result?.PositionPt);
    }

    [Fact]
    public void Ordinary_spacing_and_position_are_still_accepted()
    {
        // Non-vacuity: a guard that rejected everything would satisfy the assertions above while
        // making the Font dialog impossible to use.
        TryBuild(InputWith(spacing: "1.5", position: "-3"), out var result).Should().BeTrue();
        result!.CharacterSpacingPt.Should().Be(1.5);
        result.PositionPt.Should().Be(-3);
    }

    [Fact]
    public void The_kerning_path_r551_guarded_still_rejects()
    {
        // Pins r551's fix in the same method, so the two paths cannot drift apart again.
        var input = InputWith() with { KerningMinSizeText = "NaN" };

        TryBuild(input, out _).Should().BeFalse();
    }
}
