using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Verifies <see cref="AppVersionFormatter"/> independently of any app-specific wrapper,
/// covering both the FreeX mode (dropTrailingZeroPatch = true) and the FreeW mode (false).
/// </summary>
public sealed class AppVersionFormatterTests
{
    // ── FormatVersionText — FreeX mode (dropTrailingZeroPatch = true) ──────────────────

    [Fact]
    public void FormatVersionText_FreexMode_DropsTrailingZeroPatch()
    {
        AppVersionFormatter.FormatVersionText("0.5.0", dropTrailingZeroPatch: true)
            .Should().Be("Version 0.5 (Tester Release)");
        AppVersionFormatter.FormatVersionText("1.2.0", dropTrailingZeroPatch: true)
            .Should().Be("Version 1.2 (Tester Release)");
    }

    [Fact]
    public void FormatVersionText_FreexMode_PreservesNonZeroPatch()
    {
        AppVersionFormatter.FormatVersionText("0.8.42", dropTrailingZeroPatch: true)
            .Should().Be("Version 0.8.42 (Tester Release)");
        AppVersionFormatter.FormatVersionText("1.0.1", dropTrailingZeroPatch: true)
            .Should().Be("Version 1.0.1 (Tester Release)");
    }

    [Fact]
    public void FormatVersionText_FreexMode_StripsBuildMetadata()
    {
        AppVersionFormatter.FormatVersionText("0.8.42+abcdef12", dropTrailingZeroPatch: true)
            .Should().Be("Version 0.8.42 (Tester Release)");
    }

    [Fact]
    public void FormatVersionText_FreexMode_FallsBackOnNullOrEmpty()
    {
        // The default fallback "0.5.0" also has zero patch, so it gets compressed to "0.5"
        AppVersionFormatter.FormatVersionText(null, dropTrailingZeroPatch: true)
            .Should().Be("Version 0.5 (Tester Release)");
        AppVersionFormatter.FormatVersionText("", dropTrailingZeroPatch: true)
            .Should().Be("Version 0.5 (Tester Release)");
    }

    // ── FormatVersionText — FreeW mode (dropTrailingZeroPatch = false, default) ────────

    [Fact]
    public void FormatVersionText_FreewMode_PreservesFullThreePartVersion()
    {
        AppVersionFormatter.FormatVersionText("0.5.0")
            .Should().Be("Version 0.5.0 (Tester Release)");
        AppVersionFormatter.FormatVersionText("1.2.0")
            .Should().Be("Version 1.2.0 (Tester Release)");
    }

    [Fact]
    public void FormatVersionText_FreewMode_StripsBuildMetadata()
    {
        AppVersionFormatter.FormatVersionText("0.8.42+abcdef12")
            .Should().Be("Version 0.8.42 (Tester Release)");
    }

    [Fact]
    public void FormatVersionText_FreewMode_FallsBackOnNullOrEmpty()
    {
        // FreeW mode keeps "0.5.0" as-is (no trailing-zero drop)
        AppVersionFormatter.FormatVersionText(null)
            .Should().Be("Version 0.5.0 (Tester Release)");
        AppVersionFormatter.FormatVersionText("  ")
            .Should().Be("Version 0.5.0 (Tester Release)");
    }

    // ── FormatBuildVersionText ──────────────────────────────────────────────────────────

    [Fact]
    public void FormatBuildVersionText_IncludesBuildVersionWhenDifferent()
    {
        AppVersionFormatter.FormatBuildVersionText("0.8.42+abcdef12", "0.8.42.0")
            .Should().Be("Version 0.8.42 (build 0.8.42.0, Tester Release)");
    }

    [Fact]
    public void FormatBuildVersionText_OmitsBuildVersionWhenEqual()
    {
        AppVersionFormatter.FormatBuildVersionText("0.5.0", "0.5.0")
            .Should().Be("Version 0.5.0 (Tester Release)");
    }

    [Fact]
    public void FormatBuildVersionText_FallsBackOnNullInformationalVersion()
    {
        AppVersionFormatter.FormatBuildVersionText(null, "0.5.0")
            .Should().Be("Version 0.5.0 (Tester Release)");
    }

    // ── NormalizeVersionForDisplay ──────────────────────────────────────────────────────

    [Fact]
    public void NormalizeVersionForDisplay_StripsBuildMetadataAndTrimsWhitespace()
    {
        AppVersionFormatter.NormalizeVersionForDisplay("0.8.42+abcdef12").Should().Be("0.8.42");
        AppVersionFormatter.NormalizeVersionForDisplay("  0.5.0  ").Should().Be("0.5.0");
    }

    [Fact]
    public void NormalizeVersionForDisplay_FallsBackOnNullOrWhiteSpace()
    {
        AppVersionFormatter.NormalizeVersionForDisplay(null).Should().Be("0.5.0");
        AppVersionFormatter.NormalizeVersionForDisplay("").Should().Be("0.5.0");
        AppVersionFormatter.NormalizeVersionForDisplay("   ").Should().Be("0.5.0");
    }
}
