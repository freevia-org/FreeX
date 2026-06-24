namespace Free.Shared.Theme.Tests;

/// <summary>
/// WS-G round 3: proves the new typography and metrics token members carry
/// the exact values captured from the WPF/Avalonia chrome baseline (2026-06-24).
/// </summary>
public sealed class ThemeTypographyMetricsRound3Tests
{
    // ── ThemeTypography new member: StatusBarText ─────────────────────────────

    [Fact]
    public void FreeX_StatusBarText_FontSize_Is_12()
    {
        // Baseline: WPF MainWindow.xaml:1134 FontSize="12" / Avalonia MainWindow.cs:3291 FontSize=12
        BrandThemes.FreeX.Typography.StatusBarText.SizePt.Should().Be(12.0);
    }

    [Fact]
    public void FreeX_StatusBarText_Weight_Is_Normal()
    {
        BrandThemes.FreeX.Typography.StatusBarText.Weight.Should().Be(ThemeFontWeight.Normal);
    }

    [Fact]
    public void FreeX_StatusBarText_FontFamily_Is_Empty()
    {
        // Both renderers omit an explicit font-family for status-bar text (inherits system default).
        BrandThemes.FreeX.Typography.StatusBarText.FontFamily.Should().BeEmpty();
    }

    // ── ThemeMetrics new members: StatusBarHeight + TitleBarCaptionHeight ────

    [Fact]
    public void FreeX_StatusBarHeight_Is_28()
    {
        // Baseline: Avalonia MainWindow.cs:3388 Height=28 / WPF implicit (Padding="8,3" + FontSize=12)
        BrandThemes.FreeX.Metrics.StatusBarHeight.Should().Be(28.0);
    }

    [Fact]
    public void FreeX_TitleBarCaptionHeight_Is_34()
    {
        // Baseline: WPF MainWindow.xaml:25 WindowChrome.CaptionHeight="34"
        // (Avalonia uses native OS title bar — value carried for documentation, not applied by Avalonia applier)
        BrandThemes.FreeX.Metrics.TitleBarCaptionHeight.Should().Be(34.0);
    }

    // ── All themes carry the new members (compile + runtime sanity) ───────────

    [Fact]
    public void AllThemes_HaveStatusBarTextToken()
    {
        foreach (var t in new[] { BrandThemes.FreeX, BrandThemes.FreeW, BrandThemes.FreeP, BrandThemes.FreeXMidnight })
        {
            t.Typography.StatusBarText.Should().NotBeNull(because: $"theme '{t.Name}' must have StatusBarText token");
            t.Typography.StatusBarText.SizePt.Should().BeGreaterThan(0, because: $"theme '{t.Name}' StatusBarText.SizePt must be positive");
        }
    }

    [Fact]
    public void AllThemes_HaveStatusBarHeightMetric()
    {
        foreach (var t in new[] { BrandThemes.FreeX, BrandThemes.FreeW, BrandThemes.FreeP, BrandThemes.FreeXMidnight })
        {
            t.Metrics.StatusBarHeight.Should().BeGreaterThan(0, because: $"theme '{t.Name}' StatusBarHeight must be positive");
        }
    }

    [Fact]
    public void AllThemes_HaveTitleBarCaptionHeightMetric()
    {
        foreach (var t in new[] { BrandThemes.FreeX, BrandThemes.FreeW, BrandThemes.FreeP, BrandThemes.FreeXMidnight })
        {
            t.Metrics.TitleBarCaptionHeight.Should().BeGreaterThan(0, because: $"theme '{t.Name}' TitleBarCaptionHeight must be positive");
        }
    }
}
