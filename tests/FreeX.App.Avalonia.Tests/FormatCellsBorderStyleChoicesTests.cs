using FreeX.App.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pure-data tests for <see cref="MainWindow.CreateFormatCellsBorderStyleListChoices"/>.
/// No Avalonia UI thread required — the method is a plain data factory.
///
/// Regression coverage for CF1: the per-edge style dropdown previously listed only 7 of 15
/// BorderStyle values (None/Thin/Medium/Thick/Dashed/Dotted/Double).  A cell edge carrying
/// Hair, MediumDashed, DashDot, MediumDashDot, DashDotDot, MediumDashDotDot, or SlantDashDot
/// would seed as "None", and a subsequent color-only edit on that edge was then silently lost.
/// </summary>
public sealed class FormatCellsBorderStyleChoicesTests
{
    [Fact]
    public void Choices_ProjectTheCanonicalPortableCatalog()
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();

        choices.Select(choice => choice.Label)
            .Should().Equal(FormatCellsBorderPalettePlanner.StyleChoices.Select(choice => choice.DisplayName));
        choices.Select(choice => choice.Value ?? BorderStyle.None)
            .Should().Equal(FormatCellsBorderPalettePlanner.StyleChoices.Select(choice => choice.Style));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates the seed step: find the choice whose Value matches <paramref name="style"/>.
    /// Returns null if no match — this is what caused the bug (SetEdgeStyle fell to index 0).
    /// </summary>
    private static MainWindow.FormatCellsNullableChoice<BorderStyle>? FindChoiceForStyle(
        IReadOnlyList<MainWindow.FormatCellsNullableChoice<BorderStyle>> choices,
        BorderStyle style)
    {
        foreach (var choice in choices)
        {
            if (choice.Value == style)
                return choice;
        }
        return null; // was the bug: SetEdgeStyle would fall through to index 0 ("None")
    }

    // ── Coverage: every non-None BorderStyle must appear ────────────────────────────────────────

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.Medium)]
    [InlineData(BorderStyle.Thick)]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.Dotted)]
    [InlineData(BorderStyle.Double)]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.MediumDashed)]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashDot)]
    [InlineData(BorderStyle.DashDotDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    [InlineData(BorderStyle.SlantDashDot)]
    public void AllNonNoneStyles_HaveChoiceEntry(BorderStyle style)
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();
        var match = FindChoiceForStyle(choices, style);

        match.Should().NotBeNull(
            because: $"{style} must have a choice entry so the seed step does not fall back to None");
        // match is not null (asserted above); .Value is BorderStyle? — verify it equals the style.
        (match!.Value == style).Should().BeTrue(
            because: "the choice value must round-trip the exact enum member");
    }

    // ── The "None" sentinel must be first with a null Value ──────────────────────────────────────

    [Fact]
    public void FirstChoice_IsNone_WithNullValue()
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();

        choices[0].Label.Should().Be("None");
        choices[0].Value.Should().BeNull();
    }

    // ── All 14 non-None enum values must be represented (no silently-dropped additions) ─────────

    [Fact]
    public void AllDefinedNonNoneBorderStyles_AreCovered()
    {
        var allNonNoneStyles = Enum.GetValues<BorderStyle>()
            .Where(s => s != BorderStyle.None)
            .ToHashSet();

        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();
        var coveredStyles = choices
            .Where(c => c.Value.HasValue)
            .Select(c => c.Value!.Value)
            .ToHashSet();

        coveredStyles.Should().BeEquivalentTo(allNonNoneStyles,
            because: "every BorderStyle value that XLSX can encode must be choosable in FormatCells");
    }

    // ── No duplicate enum values in the list ─────────────────────────────────────────────────────

    [Fact]
    public void NoStyle_IsDuplicated()
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();
        var nonNullValues = choices.Where(c => c.Value.HasValue).Select(c => c.Value!.Value).ToList();

        nonNullValues.Should().OnlyHaveUniqueItems(
            because: "duplicate entries would make the seed select an arbitrary match");
    }

    // ── Seed-then-read round-trip for previously-missing styles ──────────────────────────────────

    /// <summary>
    /// Regression test: before the fix, seeding Hair (or any other previously-missing style) on
    /// an edge would find no match and fall to index 0 ("None").  A color-only edit after that
    /// would read the style as null, so the style was silently cleared.
    ///
    /// After the fix: the seed finds the Hair entry, so the apply step reads back Hair,
    /// and a color-only round-trip preserves the style.
    /// </summary>
    [Theory]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.MediumDashed)]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashDot)]
    [InlineData(BorderStyle.DashDotDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    [InlineData(BorderStyle.SlantDashDot)]
    public void PreviouslyMissingStyle_SeedsToNonNoneChoice_NotNull(BorderStyle style)
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();

        // Simulate SetEdgeStyle: iterate and find by value.
        var seeded = FindChoiceForStyle(choices, style);

        seeded.Should().NotBeNull(
            because: $"SetEdgeStyle must find {style} and NOT fall back to index 0 (None); " +
                     $"if it fell to None, a color-only edit would see style=null and lose the edit");
        // seeded is not null (asserted above); .Value is BorderStyle? — verify it equals the style.
        (seeded!.Value == style).Should().BeTrue(
            because: "the seeded choice must carry the original style so Apply can write it back");
    }

    // ── Label sanity: no label is empty, all are unique ──────────────────────────────────────────

    [Fact]
    public void AllLabels_AreNonEmptyAndUnique()
    {
        var choices = MainWindow.CreateFormatCellsBorderStyleListChoices();

        choices.Should().AllSatisfy(c => c.Label.Should().NotBeNullOrWhiteSpace());
        choices.Select(c => c.Label).Should().OnlyHaveUniqueItems(
            because: "duplicate labels would confuse the user");
    }
}
