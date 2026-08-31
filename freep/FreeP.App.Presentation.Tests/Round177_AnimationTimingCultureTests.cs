using System.Globalization;

using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 177. AnimationPanePlanner.FormatDuration writes the pane's duration and delay fields with
/// CultureInfo.CurrentCulture, but TryParseDuration/TryParseDelay parsed them with
/// CultureInfo.InvariantCulture only. On any comma-decimal locale the pane therefore displayed a
/// value it would then refuse to read back: opening Animation Pane on a 0.5s effect showed "0,5",
/// and clicking away without typing anything reported the field invalid and discarded the timing.
///
/// The fix accepts the current culture first and falls back to invariant, so values typed or pasted
/// in either form work. NumberStyles.Float excludes AllowThousands, which is what makes the fallback
/// safe: "1.5" cannot be misread as 15 by a culture whose group separator is "." -- it fails there
/// and falls through to invariant.
/// </summary>
public sealed class Round177_AnimationTimingCultureTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void WhatFormatDurationWrites_IsAlwaysReadableByTryParseDuration(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;

            var displayed = AnimationPanePlanner.FormatDuration(500);
            AnimationPanePlanner.TryParseDuration(displayed, out var ms)
                .Should().BeTrue(
                    $"the pane displays \"{displayed}\" in {cultureName} and must be able to read its " +
                    "own output back -- otherwise the field rejects a value the user never touched");
            ms.Should().Be(500);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void AnInvariantFormValue_IsStillAccepted_OnACommaDecimalCulture()
    {
        // Values reaching these fields are not always typed by hand on this machine -- a pasted
        // value, or one carried over from a document authored elsewhere, is written "0.5".
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            AnimationPanePlanner.TryParseDuration("0.5", out var ms).Should().BeTrue();
            ms.Should().Be(500);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ADotDecimalValue_IsNotMisreadAsAGroupSeparator_OnACommaDecimalCulture()
    {
        // The hazard the fallback has to avoid: if the current-culture attempt allowed thousands
        // separators, "1.5" in de-DE would parse as 15 -- a ten-fold wrong duration, silently.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            AnimationPanePlanner.TryParseDuration("1.5", out var ms).Should().BeTrue();
            ms.Should().Be(1500, "1.5 seconds, not 15");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void WhatFormatEasingWrites_IsAlwaysReadableByTryParseEasing(string cultureName)
    {
        // r180: the Smooth Start/End field is the sibling of the Duration/Delay fields above -- same
        // format-current / parse-invariant asymmetry, so on a comma-decimal locale the pane displayed
        // "12,345%" and then reported its own output invalid when the user tabbed away.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var displayed = AnimationPanePlanner.FormatEasing(12345);
            AnimationPanePlanner.TryParseEasing(displayed, out var value)
                .Should().BeTrue($"the pane shows \"{displayed}\" in {cultureName} and must read it back");
            value.Should().Be(12345);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void AnInvariantFormEasingValue_IsStillAccepted_OnACommaDecimalCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            AnimationPanePlanner.TryParseEasing("12.345%", out var value).Should().BeTrue();
            value.Should().Be(12345);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
