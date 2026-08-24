using System.Text.RegularExpressions;

namespace Free.Shared.Theme.Tests;

/// <summary>
/// Byte-identical proof: every FreeX brand color must match ThemeResources.xaml exactly.
/// Reskin proof: FreeXMidnight colors differ from FreeX.
/// </summary>
public sealed class BrandThemesTests
{
    [Fact]
    public void Shared_wpf_neutral_fallbacks_match_authoritative_brand_theme_roles()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "shared", "Free.Shared.Ribbon.Wpf", "SharedChromeResources.xaml"));
        var colors = BrandThemes.FreeX.Colors;
        var expected = new Dictionary<string, string>
        {
            ["ChromeWhiteBrush"] = colors.White.ToHex(),
            ["ChromeTextBrush"] = colors.Text.ToHex(),
            ["ChromeMutedTextBrush"] = colors.MutedText.ToHex(),
            ["ChromeClosePressedBrush"] = colors.Danger.ToHex(),
        };

        foreach (var pair in expected)
        {
            var match = Regex.Match(xaml, $@"x:Key=""{pair.Key}""\s+Color=""(?<hex>#[0-9A-Fa-f]+)""");
            match.Success.Should().BeTrue(pair.Key);
            match.Groups["hex"].Value.Should().Be(pair.Value);
        }

        foreach (var theme in AllThemes())
        {
            theme.Colors.White.ToHex().Should().Be(colors.White.ToHex());
            theme.Colors.Text.ToHex().Should().Be(colors.Text.ToHex());
            theme.Colors.MutedText.ToHex().Should().Be(colors.MutedText.ToHex());
            theme.Colors.Danger.ToHex().Should().Be(colors.Danger.ToHex());
        }
    }

    [Fact]
    public void Ribbon_visual_palette_projects_from_theme_roles_without_local_palette_literals()
    {
        var palette = RibbonVisualPalette.FromTheme(BrandThemes.FreeP);
        palette.Surface.Should().Be(BrandThemes.FreeP.Colors.RibbonSurface);
        palette.Accent.Should().Be(BrandThemes.FreeP.Colors.Accent);
        palette.Divider.Should().Be(BrandThemes.FreeP.Colors.Border);
        palette.InlineDivider.Should().Be(BrandThemes.FreeP.Colors.RibbonInlineDivider);
        palette.GroupLabel.Should().Be(BrandThemes.FreeP.Colors.MutedText);
        palette.Hover.Should().Be(BrandThemes.FreeP.Colors.RibbonButtonHover);
        palette.HoverBorder.Should().Be(BrandThemes.FreeP.Colors.BorderStrong);
        palette.Checked.Should().Be(BrandThemes.FreeP.Colors.AccentPressed);
        palette.TabHover.Should().Be(BrandThemes.FreeP.Colors.AccentSoft);
        palette.TabStrip.Should().Be(BrandThemes.FreeP.Colors.ChromeSurface);
        palette.TabText.Should().Be(BrandThemes.FreeP.Colors.Text);
    }

    [Fact]
    public void Every_product_theme_owns_its_cross_platform_visual_assets()
    {
        var expected = new[]
        {
            (Theme: BrandThemes.FreeX, Id: "freex", Glyph: "X", BaseName: "FreeX"),
            (Theme: BrandThemes.FreeW, Id: "freew", Glyph: "W", BaseName: "FreeW"),
            (Theme: BrandThemes.FreeP, Id: "freep", Glyph: "P", BaseName: "FreeP"),
        };

        foreach (var item in expected)
        {
            item.Theme.VisualAssets.IconSetId.Should().Be(item.Id);
            item.Theme.VisualAssets.ProductGlyph.Should().Be(item.Glyph);
            item.Theme.VisualAssets.WindowsIconFileName.Should().Be($"{item.BaseName}.ico");
            item.Theme.VisualAssets.ScalableIconFileName.Should().Be($"{item.BaseName}.svg");
            item.Theme.VisualAssets.MacOsIconFileName.Should().Be($"{item.BaseName}.icns");
            item.Theme.VisualAssets.GetWpfPackUri($"{item.BaseName}.App.Host")
                .Should().Be($"pack://application:,,,/{item.BaseName}.App.Host;component/Resources/{item.BaseName}.ico");
        }

        BrandThemes.FreeXMidnight.VisualAssets.Should().BeSameAs(BrandThemes.FreeX.VisualAssets);
        BrandThemes.FreeWMidnight.VisualAssets.Should().BeSameAs(BrandThemes.FreeW.VisualAssets);
        BrandThemes.FreePMidnight.VisualAssets.Should().BeSameAs(BrandThemes.FreeP.VisualAssets);
        BrandThemes.FreeWMidnight.Colors.Accent.Should().Be(BrandThemes.FreeW.Colors.Accent);
        BrandThemes.FreePMidnight.Colors.Accent.Should().Be(BrandThemes.FreeP.Colors.Accent);
    }

    [Fact]
    public void Backstage_palette_is_owned_by_each_theme()
    {
        BrandThemes.FreeX.Colors.BackstageSidebar.ToHex().Should().Be("#10253A");
        BrandThemes.FreeW.Colors.BackstageSidebar.ToHex().Should().Be("#4B2F12");
        BrandThemes.FreeP.Colors.BackstageSidebar.ToHex().Should().Be("#4E213B");

        foreach (var theme in AllThemes())
        {
            theme.Colors.BackstageHover.Should().NotBe(default);
            theme.Colors.BackstageSelected.Should().NotBe(default);
            theme.Colors.BackstageSeparator.Should().NotBe(default);
            theme.Colors.BackstageLink.Should().NotBe(default);
        }
    }

    // ── Byte-identical check against ThemeResources.xaml ─────────────────────────────────

    [Fact] public void FreeX_Accent_Is_0F6D8C()               => BrandThemes.FreeX.Colors.Accent.ToHex().Should().Be("#0F6D8C");
    [Fact] public void FreeX_AccentDark_Is_17324D()           => BrandThemes.FreeX.Colors.AccentDark.ToHex().Should().Be("#17324D");
    [Fact] public void FreeX_AccentSoft_Is_E6F6FA()           => BrandThemes.FreeX.Colors.AccentSoft.ToHex().Should().Be("#E6F6FA");
    [Fact] public void FreeX_AccentPressed_Is_CCEAF2()        => BrandThemes.FreeX.Colors.AccentPressed.ToHex().Should().Be("#CCEAF2");
    [Fact] public void FreeX_TitleBar_Is_F3F4F6()             => BrandThemes.FreeX.Colors.TitleBar.ToHex().Should().Be("#F3F4F6");
    [Fact] public void FreeX_TitleBarForeground_Is_1F1F1F()   => BrandThemes.FreeX.Colors.TitleBarForeground.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeX_TitleBarHover_Is_E2E6EA()        => BrandThemes.FreeX.Colors.TitleBarHover.ToHex().Should().Be("#E2E6EA");
    [Fact] public void FreeX_TitleBarPressed_Is_D0D4D9()      => BrandThemes.FreeX.Colors.TitleBarPressed.ToHex().Should().Be("#D0D4D9");
    [Fact] public void FreeX_TitleBarDisabled_Is_767676()     => BrandThemes.FreeX.Colors.TitleBarDisabled.ToHex().Should().Be("#767676");
    [Fact] public void FreeX_TitleBarButtonBorder_Is_C8CCD0() => BrandThemes.FreeX.Colors.TitleBarButtonBorder.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeX_RibbonButtonHover_Is_BEE6FD()   => BrandThemes.FreeX.Colors.RibbonButtonHover.ToHex().Should().Be("#BEE6FD");
    [Fact] public void FreeX_Text_Is_1F1F1F()                 => BrandThemes.FreeX.Colors.Text.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeX_MutedText_Is_5F6368()            => BrandThemes.FreeX.Colors.MutedText.ToHex().Should().Be("#5F6368");
    [Fact] public void FreeX_SubtleText_Is_767676()           => BrandThemes.FreeX.Colors.SubtleText.ToHex().Should().Be("#767676");
    [Fact] public void FreeX_RibbonSurface_Is_FFFFFF()        => BrandThemes.FreeX.Colors.RibbonSurface.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeX_ChromeSurface_Is_F7F8F8()        => BrandThemes.FreeX.Colors.ChromeSurface.ToHex().Should().Be("#F7F8F8");
    [Fact] public void FreeX_SheetSurface_Is_F3F3F3()         => BrandThemes.FreeX.Colors.SheetSurface.ToHex().Should().Be("#F3F3F3");
    [Fact] public void FreeX_StatusSurface_Is_F3F4F6()        => BrandThemes.FreeX.Colors.StatusSurface.ToHex().Should().Be("#F3F4F6");
    [Fact] public void FreeX_StatusForeground_Is_1F1F1F()    => BrandThemes.FreeX.Colors.StatusForeground.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeX_Border_Is_DADCE0()               => BrandThemes.FreeX.Colors.Border.ToHex().Should().Be("#DADCE0");
    [Fact] public void FreeX_BorderStrong_Is_C8CCD0()         => BrandThemes.FreeX.Colors.BorderStrong.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeX_Danger_Is_C42B1C()               => BrandThemes.FreeX.Colors.Danger.ToHex().Should().Be("#C42B1C");
    [Fact] public void FreeX_White_Is_FFFFFF()                => BrandThemes.FreeX.Colors.White.ToHex().Should().Be("#FFFFFF");

    [Fact]
    public void OfficeProductThemes_ShareNeutralTitleBarChrome()
    {
        var baseline = BrandThemes.FreeX.Colors;

        foreach (var theme in new[] { BrandThemes.FreeW, BrandThemes.FreeP })
        {
            theme.Colors.TitleBar.Should().Be(baseline.TitleBar, theme.Name);
            theme.Colors.TitleBarForeground.Should().Be(baseline.TitleBarForeground, theme.Name);
            theme.Colors.TitleBarHover.Should().Be(baseline.TitleBarHover, theme.Name);
            theme.Colors.TitleBarPressed.Should().Be(baseline.TitleBarPressed, theme.Name);
            theme.Colors.TitleBarDisabled.Should().Be(baseline.TitleBarDisabled, theme.Name);
            theme.Colors.TitleBarButtonBorder.Should().Be(baseline.TitleBarButtonBorder, theme.Name);
        }
    }

    // ── Reskin proof ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FreeXMidnight_Accent_DiffersFromFreeX()
    {
        BrandThemes.FreeXMidnight.Colors.Accent
            .Should().NotBe(BrandThemes.FreeX.Colors.Accent);
    }

    [Fact]
    public void FreeXMidnight_TitleBar_DiffersFromFreeX()
    {
        BrandThemes.FreeXMidnight.Colors.TitleBar
            .Should().NotBe(BrandThemes.FreeX.Colors.TitleBar);
    }

    [Fact]
    public void FreeXMidnight_IconSetId_IsFreeX()
    {
        // Midnight reuses the same icon set as FreeX
        BrandThemes.FreeXMidnight.IconSetId.Should().Be("freex");
    }

    // ── FreeW owned amber/umber palette ────────────────────────────────────────────────

    [Fact] public void FreeW_Accent_Is_A26714()               => BrandThemes.FreeW.Colors.Accent.ToHex().Should().Be("#A26714");
    [Fact] public void FreeW_AccentDark_Is_4B2F12()           => BrandThemes.FreeW.Colors.AccentDark.ToHex().Should().Be("#4B2F12");
    [Fact] public void FreeW_AccentSoft_Is_FBF0DC()           => BrandThemes.FreeW.Colors.AccentSoft.ToHex().Should().Be("#FBF0DC");
    [Fact] public void FreeW_AccentPressed_Is_F3D8AB()        => BrandThemes.FreeW.Colors.AccentPressed.ToHex().Should().Be("#F3D8AB");
    [Fact] public void FreeW_TitleBar_Is_F3F4F6()             => BrandThemes.FreeW.Colors.TitleBar.ToHex().Should().Be("#F3F4F6");
    [Fact] public void FreeW_TitleBarForeground_Is_1F1F1F()   => BrandThemes.FreeW.Colors.TitleBarForeground.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeW_TitleBarHover_Is_E2E6EA()        => BrandThemes.FreeW.Colors.TitleBarHover.ToHex().Should().Be("#E2E6EA");
    [Fact] public void FreeW_TitleBarPressed_Is_D0D4D9()      => BrandThemes.FreeW.Colors.TitleBarPressed.ToHex().Should().Be("#D0D4D9");
    [Fact] public void FreeW_TitleBarDisabled_Is_767676()     => BrandThemes.FreeW.Colors.TitleBarDisabled.ToHex().Should().Be("#767676");
    [Fact] public void FreeW_TitleBarButtonBorder_Is_C8CCD0() => BrandThemes.FreeW.Colors.TitleBarButtonBorder.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeW_RibbonButtonHover_Is_F6E3C2()   => BrandThemes.FreeW.Colors.RibbonButtonHover.ToHex().Should().Be("#F6E3C2");
    [Fact] public void FreeW_Text_Is_1F1F1F()                 => BrandThemes.FreeW.Colors.Text.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeW_MutedText_Is_5F6368()            => BrandThemes.FreeW.Colors.MutedText.ToHex().Should().Be("#5F6368");
    [Fact] public void FreeW_SubtleText_Is_767676()           => BrandThemes.FreeW.Colors.SubtleText.ToHex().Should().Be("#767676");
    [Fact] public void FreeW_RibbonSurface_Is_FFFFFF()        => BrandThemes.FreeW.Colors.RibbonSurface.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeW_ChromeSurface_Is_F7F8F8()        => BrandThemes.FreeW.Colors.ChromeSurface.ToHex().Should().Be("#F7F8F8");
    [Fact] public void FreeW_SheetSurface_Is_F3F3F3()         => BrandThemes.FreeW.Colors.SheetSurface.ToHex().Should().Be("#F3F3F3");
    [Fact] public void FreeW_StatusSurface_Is_4B2F12()        => BrandThemes.FreeW.Colors.StatusSurface.ToHex().Should().Be("#4B2F12");
    [Fact] public void FreeW_StatusForeground_Is_FFFFFF()     => BrandThemes.FreeW.Colors.StatusForeground.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeW_Border_Is_DADCE0()               => BrandThemes.FreeW.Colors.Border.ToHex().Should().Be("#DADCE0");
    [Fact] public void FreeW_BorderStrong_Is_C8CCD0()         => BrandThemes.FreeW.Colors.BorderStrong.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeW_Danger_Is_C42B1C()               => BrandThemes.FreeW.Colors.Danger.ToHex().Should().Be("#C42B1C");
    [Fact] public void FreeW_White_Is_FFFFFF()                => BrandThemes.FreeW.Colors.White.ToHex().Should().Be("#FFFFFF");

    [Fact]
    public void FreeW_Accent_MatchesBackstageTokenAnchor()
    {
        BrandThemes.FreeW.Colors.Accent.ToHex().Should().Be("#A26714");
    }

    [Fact]
    public void FreeW_IconSetId_IsFreeW()
    {
        BrandThemes.FreeW.IconSetId.Should().Be("freew");
    }

    [Fact]
    public void FreeW_DiffersFromFreeX_OnAccent()
    {
        BrandThemes.FreeW.Colors.Accent.ToHex().Should().NotBe(BrandThemes.FreeX.Colors.Accent.ToHex());
    }

    // ── FreeP owned berry/plum palette ─────────────────────────────────────────────────

    [Fact] public void FreeP_Accent_Is_A23B72()               => BrandThemes.FreeP.Colors.Accent.ToHex().Should().Be("#A23B72");
    [Fact] public void FreeP_AccentDark_Is_4E213B()           => BrandThemes.FreeP.Colors.AccentDark.ToHex().Should().Be("#4E213B");
    [Fact] public void FreeP_AccentSoft_Is_F9E7F1()           => BrandThemes.FreeP.Colors.AccentSoft.ToHex().Should().Be("#F9E7F1");
    [Fact] public void FreeP_AccentPressed_Is_F1CDE0()        => BrandThemes.FreeP.Colors.AccentPressed.ToHex().Should().Be("#F1CDE0");
    [Fact] public void FreeP_TitleBar_Is_F3F4F6()             => BrandThemes.FreeP.Colors.TitleBar.ToHex().Should().Be("#F3F4F6");
    [Fact] public void FreeP_TitleBarForeground_Is_1F1F1F()   => BrandThemes.FreeP.Colors.TitleBarForeground.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeP_TitleBarHover_Is_E2E6EA()        => BrandThemes.FreeP.Colors.TitleBarHover.ToHex().Should().Be("#E2E6EA");
    [Fact] public void FreeP_TitleBarPressed_Is_D0D4D9()      => BrandThemes.FreeP.Colors.TitleBarPressed.ToHex().Should().Be("#D0D4D9");
    [Fact] public void FreeP_TitleBarDisabled_Is_767676()     => BrandThemes.FreeP.Colors.TitleBarDisabled.ToHex().Should().Be("#767676");
    [Fact] public void FreeP_TitleBarButtonBorder_Is_C8CCD0() => BrandThemes.FreeP.Colors.TitleBarButtonBorder.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeP_RibbonButtonHover_Is_F3D7E6()   => BrandThemes.FreeP.Colors.RibbonButtonHover.ToHex().Should().Be("#F3D7E6");
    [Fact] public void FreeP_Text_Is_1F1F1F()                 => BrandThemes.FreeP.Colors.Text.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeP_MutedText_Is_5F6368()            => BrandThemes.FreeP.Colors.MutedText.ToHex().Should().Be("#5F6368");
    [Fact] public void FreeP_SubtleText_Is_767676()           => BrandThemes.FreeP.Colors.SubtleText.ToHex().Should().Be("#767676");
    [Fact] public void FreeP_RibbonSurface_Is_FFFFFF()        => BrandThemes.FreeP.Colors.RibbonSurface.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeP_ChromeSurface_Is_F7F8F8()        => BrandThemes.FreeP.Colors.ChromeSurface.ToHex().Should().Be("#F7F8F8");
    [Fact] public void FreeP_SheetSurface_Is_F3F3F3()         => BrandThemes.FreeP.Colors.SheetSurface.ToHex().Should().Be("#F3F3F3");
    [Fact] public void FreeP_StatusSurface_Is_4E213B()        => BrandThemes.FreeP.Colors.StatusSurface.ToHex().Should().Be("#4E213B");
    [Fact] public void FreeP_StatusForeground_Is_FFFFFF()     => BrandThemes.FreeP.Colors.StatusForeground.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeP_Border_Is_DADCE0()               => BrandThemes.FreeP.Colors.Border.ToHex().Should().Be("#DADCE0");
    [Fact] public void FreeP_BorderStrong_Is_C8CCD0()         => BrandThemes.FreeP.Colors.BorderStrong.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeP_Danger_Is_C42B1C()               => BrandThemes.FreeP.Colors.Danger.ToHex().Should().Be("#C42B1C");
    [Fact] public void FreeP_White_Is_FFFFFF()                => BrandThemes.FreeP.Colors.White.ToHex().Should().Be("#FFFFFF");

    [Fact]
    public void FreeP_Accent_MatchesBackstageTokenAnchor()
    {
        BrandThemes.FreeP.Colors.Accent.ToHex().Should().Be("#A23B72");
    }

    [Fact]
    public void FreeP_IconSetId_IsFreeP()
    {
        BrandThemes.FreeP.IconSetId.Should().Be("freep");
    }

    [Fact]
    public void FreeP_DiffersFromFreeX_OnAccent()
    {
        BrandThemes.FreeP.Colors.Accent.ToHex().Should().NotBe(BrandThemes.FreeX.Colors.Accent.ToHex());
    }

    // ── Structural sanity ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AllThemes_HaveNonEmptyName()
    {
        foreach (var t in AllThemes())
        {
            t.Name.Should().NotBeNullOrWhiteSpace();
        }
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

    private static Theme[] AllThemes() =>
    [
        BrandThemes.FreeX,
        BrandThemes.FreeW,
        BrandThemes.FreeP,
        BrandThemes.FreeXMidnight,
        BrandThemes.FreeWMidnight,
        BrandThemes.FreePMidnight,
    ];
}
