using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for r143 finding "cell-styles/F1": the <see cref="CellStylePreset"/> enum
/// could not represent ~10 of Excel's standard built-in cell styles (Title, Heading 3/4,
/// Currency, Currency [0], Comma, Comma [0], Percent, Hyperlink, Followed Hyperlink) -
/// <see cref="CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey"/> and
/// <see cref="CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset,WorkbookTheme)"/> threw
/// <see cref="ArgumentOutOfRangeException"/> for all of them because their switches were
/// exhaustive over the (incomplete) enum. Before the fix, referencing any of the new enum
/// members below was a compile error - these tests exist to keep every missing style callable
/// and to pin down which "include in style" categories (Number/Alignment/Font/Border/Fill/
/// Protection) each new preset legitimately touches, matching Excel's own built-in style
/// definitions.
/// </summary>
public sealed class R143_CellStyleMissingBuiltinPresetsTests
{
    public static IEnumerable<object[]> AllNewPresets()
    {
        yield return [CellStylePreset.Heading3];
        yield return [CellStylePreset.Heading4];
        yield return [CellStylePreset.Title];
        yield return [CellStylePreset.Currency];
        yield return [CellStylePreset.Currency0];
        yield return [CellStylePreset.Comma];
        yield return [CellStylePreset.Comma0];
        yield return [CellStylePreset.Percent];
        yield return [CellStylePreset.Hyperlink];
        yield return [CellStylePreset.FollowedHyperlink];
    }

    [Theory]
    [MemberData(nameof(AllNewPresets))]
    public void MissingBuiltinPreset_DiffAndLabelResourceKey_DoNotThrow(CellStylePreset preset)
    {
        // Before the fix, every one of these presets hit the "_ => throw new
        // ArgumentOutOfRangeException(...)" default arm in both switches - the gallery could
        // not resolve a diff or a localized label for any of Excel's Title/Heading 3-4/
        // Currency/Comma/Percent/Hyperlink family, so applying them was structurally impossible.
        var act1 = () => CellStyleDiffPlanner.GetCellStylePresetDiff(preset, WorkbookTheme.Office);
        var act2 = () => CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey(preset);
        var act3 = () => CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();

        CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset).Should().NotBeNullOrWhiteSpace();
        CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey(preset).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(CellStylePreset.Currency, "Currency")]
    [InlineData(CellStylePreset.Currency0, "Currency [0]")]
    [InlineData(CellStylePreset.Comma, "Comma")]
    [InlineData(CellStylePreset.Comma0, "Comma [0]")]
    [InlineData(CellStylePreset.Percent, "Percent")]
    [InlineData(CellStylePreset.Hyperlink, "Hyperlink")]
    [InlineData(CellStylePreset.FollowedHyperlink, "Followed Hyperlink")]
    [InlineData(CellStylePreset.Heading3, "Heading 3")]
    [InlineData(CellStylePreset.Heading4, "Heading 4")]
    [InlineData(CellStylePreset.Title, "Title")]
    public void MissingBuiltinPreset_DisplayName_MatchesExcelsOwnStyleName(CellStylePreset preset, string expected)
        => CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset).Should().Be(expected);

    [Theory]
    [InlineData(CellStylePreset.Currency)]
    [InlineData(CellStylePreset.Currency0)]
    [InlineData(CellStylePreset.Comma)]
    [InlineData(CellStylePreset.Comma0)]
    [InlineData(CellStylePreset.Percent)]
    public void NumberFormatPresets_TouchOnlyNumberCategory_LeaveAlignmentFontBorderFillProtectionUntouched(
        CellStylePreset preset)
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset);

        // "Number" category: must actually set a number format.
        diff.NumberFormat.Should().NotBeNullOrEmpty();

        // Every other "include in style" category must be left as null (unchanged) - Excel's
        // Currency/Comma/Percent cell styles do not touch alignment, font, borders, fill, or
        // protection at all.
        diff.HAlign.Should().BeNull("Alignment must not be touched");
        diff.VAlign.Should().BeNull("Alignment must not be touched");
        diff.WrapText.Should().BeNull("Alignment must not be touched");
        diff.IndentLevel.Should().BeNull("Alignment must not be touched");
        diff.TextRotation.Should().BeNull("Alignment must not be touched");

        diff.Bold.Should().BeNull("Font must not be touched");
        diff.Italic.Should().BeNull("Font must not be touched");
        diff.FontName.Should().BeNull("Font must not be touched");
        diff.FontSize.Should().BeNull("Font must not be touched");
        diff.FontColor.Should().BeNull("Font must not be touched");
        diff.FontThemeColor.Should().BeNull("Font must not be touched");
        diff.Underline.Should().BeNull("Font must not be touched");

        diff.BorderTop.Should().BeNull("Border must not be touched");
        diff.BorderBottom.Should().BeNull("Border must not be touched");
        diff.BorderLeft.Should().BeNull("Border must not be touched");
        diff.BorderRight.Should().BeNull("Border must not be touched");

        diff.FillColor.Should().BeNull("Fill must not be touched");
        diff.FillThemeColor.Should().BeNull("Fill must not be touched");
        diff.ClearFill.Should().BeNull("Fill must not be touched");

        diff.Locked.Should().BeNull("Protection must not be touched");
        diff.Hidden.Should().BeNull("Protection must not be touched");
    }

    [Fact]
    public void CurrencyPreset_AppliedToAPreStyledCell_OnlyChangesTheNumberFormat()
    {
        // A cell that already carries bold, a fill, a border and a different number format -
        // exactly what "Apply Currency to an already-formatted numeric column" looks like.
        var preStyled = new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(10, 20, 30),
            NumberFormat = "0.00",
            BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black),
            HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Right,
            Locked = false,
        };

        var result = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Currency).ApplyTo(preStyled);

        result.NumberFormat.Should().Be(HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode);
        // Everything else must survive untouched - Currency is a Number-only style.
        result.Bold.Should().BeTrue();
        result.FillColor.Should().Be(new CellColor(10, 20, 30));
        result.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        result.HorizontalAlignment.Should().Be(FreeX.Core.Model.HorizontalAlignment.Right);
        result.Locked.Should().BeFalse();
    }

    [Theory]
    [InlineData(CellStylePreset.Hyperlink, WorkbookThemeColorSlot.Hyperlink)]
    [InlineData(CellStylePreset.FollowedHyperlink, WorkbookThemeColorSlot.FollowedHyperlink)]
    public void HyperlinkPresets_SetThemeLinkedFontColorAndUnderline_TouchNothingElse(
        CellStylePreset preset, WorkbookThemeColorSlot expectedSlot)
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset);

        diff.Underline.Should().BeTrue();
        diff.FontThemeColor.Should().Be(new WorkbookThemeColorReference(expectedSlot));
        diff.FontColor.Should().BeNull("the color must be theme-linked, not a baked literal");

        diff.NumberFormat.Should().BeNull("Number must not be touched");
        diff.BorderTop.Should().BeNull("Border must not be touched");
        diff.BorderBottom.Should().BeNull("Border must not be touched");
        diff.FillColor.Should().BeNull("Fill must not be touched");
        diff.FillThemeColor.Should().BeNull("Fill must not be touched");
        diff.HAlign.Should().BeNull("Alignment must not be touched");
        diff.Locked.Should().BeNull("Protection must not be touched");
        diff.Hidden.Should().BeNull("Protection must not be touched");

        // The theme link must actually resolve to a different color when the theme changes,
        // proving it is live (not baked) - the same contract R33CellStyleThemePresetTests
        // established for the Accent tint presets.
        var themeA = WorkbookTheme.Office.WithColor(expectedSlot, new CellColor(1, 2, 3));
        var themeB = WorkbookTheme.Office.WithColor(expectedSlot, new CellColor(200, 210, 220));
        diff.FontThemeColor!.Value.Resolve(themeA).Should().Be(new CellColor(1, 2, 3));
        diff.FontThemeColor!.Value.Resolve(themeB).Should().Be(new CellColor(200, 210, 220));
    }

    [Fact]
    public void HeadingAndTitlePresets_ClearFillAndSetBold_MatchHeading1Heading2Pattern()
    {
        var heading3 = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading3);
        var heading4 = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading4);
        var title = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Title);

        heading3.Bold.Should().BeTrue();
        heading3.ClearFill.Should().BeTrue();
        heading3.BorderBottom.Should().NotBeNull();
        heading3.BorderBottom!.Value.Style.Should().Be(BorderStyle.Thin);

        heading4.Bold.Should().BeTrue();
        heading4.ClearFill.Should().BeTrue();
        // Heading 4 (unlike Heading 1-3) is not underscored by a border in Excel.
        heading4.BorderBottom.Should().BeNull();

        title.Bold.Should().BeTrue();
        title.FontSize.Should().Be(18);
        title.ClearFill.Should().BeTrue();
    }

    [Fact]
    public void SiblingCheck_ExistingBuiltinPresets_AreStillUnaffectedByTheNewMembers()
    {
        // Proves the fix (appending 10 new enum members + switch arms) did not disturb any
        // pre-existing preset's diff - same assertions R33CellStyleThemePresetTests already
        // makes for Good/Heading1, repeated here against a broader neighbourhood (Total,
        // LinkedCell, an Accent tint) as the "did not break neighbouring behaviour" sibling.
        var total = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Total);
        total.Bold.Should().BeTrue();
        total.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        total.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, CellColor.Black));

        var linkedCell = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.LinkedCell);
        linkedCell.FillColor.Should().Be(new CellColor(221, 235, 247));
        linkedCell.FontColor.Should().Be(new CellColor(5, 99, 193));
        linkedCell.Underline.Should().BeTrue();

        var accent1_20 = CellStyleDiffPlanner.GetCellStylePresetDiff(
            CellStylePreset.Accent1_20, WorkbookTheme.Office);
        accent1_20.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.8));

        // The full enum grew, but every previously-existing member is still present and
        // resolvable - the fix only appended, it never renumbered or removed anything.
        Enum.GetValues<CellStylePreset>().Should().HaveCount(43);
        Enum.IsDefined(CellStylePreset.Heading1).Should().BeTrue();
        Enum.IsDefined(CellStylePreset.Accent6_60).Should().BeTrue();
    }
}
