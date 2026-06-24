namespace Free.Shared.Theme.Tests;

/// <summary>
/// Byte-identical proof: every FreeX brand color must match ThemeResources.xaml exactly.
/// Reskin proof: FreeXMidnight colors differ from FreeX.
/// </summary>
public sealed class BrandThemesTests
{
    // ── Byte-identical check against ThemeResources.xaml ─────────────────────────────────

    [Fact] public void FreeX_Accent_Is_0F6D8C()               => BrandThemes.FreeX.Colors.Accent.ToHex().Should().Be("#0F6D8C");
    [Fact] public void FreeX_AccentDark_Is_17324D()           => BrandThemes.FreeX.Colors.AccentDark.ToHex().Should().Be("#17324D");
    [Fact] public void FreeX_AccentSoft_Is_E6F6FA()           => BrandThemes.FreeX.Colors.AccentSoft.ToHex().Should().Be("#E6F6FA");
    [Fact] public void FreeX_AccentPressed_Is_CCEAF2()        => BrandThemes.FreeX.Colors.AccentPressed.ToHex().Should().Be("#CCEAF2");
    [Fact] public void FreeX_TitleBar_Is_17324D()             => BrandThemes.FreeX.Colors.TitleBar.ToHex().Should().Be("#17324D");
    [Fact] public void FreeX_TitleBarHover_Is_0F6D8C()        => BrandThemes.FreeX.Colors.TitleBarHover.ToHex().Should().Be("#0F6D8C");
    [Fact] public void FreeX_TitleBarPressed_Is_10253A()      => BrandThemes.FreeX.Colors.TitleBarPressed.ToHex().Should().Be("#10253A");
    [Fact] public void FreeX_TitleBarDisabled_Is_8BA6B8()     => BrandThemes.FreeX.Colors.TitleBarDisabled.ToHex().Should().Be("#8BA6B8");
    [Fact] public void FreeX_TitleBarButtonBorder_Is_55FFFFFF() => BrandThemes.FreeX.Colors.TitleBarButtonBorder.ToHex().Should().Be("#55FFFFFF");
    [Fact] public void FreeX_RibbonButtonHover_Is_BEE6FD()   => BrandThemes.FreeX.Colors.RibbonButtonHover.ToHex().Should().Be("#BEE6FD");
    [Fact] public void FreeX_Text_Is_1F1F1F()                 => BrandThemes.FreeX.Colors.Text.ToHex().Should().Be("#1F1F1F");
    [Fact] public void FreeX_MutedText_Is_5F6368()            => BrandThemes.FreeX.Colors.MutedText.ToHex().Should().Be("#5F6368");
    [Fact] public void FreeX_SubtleText_Is_767676()           => BrandThemes.FreeX.Colors.SubtleText.ToHex().Should().Be("#767676");
    [Fact] public void FreeX_RibbonSurface_Is_FFFFFF()        => BrandThemes.FreeX.Colors.RibbonSurface.ToHex().Should().Be("#FFFFFF");
    [Fact] public void FreeX_ChromeSurface_Is_F7F8F8()        => BrandThemes.FreeX.Colors.ChromeSurface.ToHex().Should().Be("#F7F8F8");
    [Fact] public void FreeX_SheetSurface_Is_F3F3F3()         => BrandThemes.FreeX.Colors.SheetSurface.ToHex().Should().Be("#F3F3F3");
    [Fact] public void FreeX_StatusSurface_Is_17324D()        => BrandThemes.FreeX.Colors.StatusSurface.ToHex().Should().Be("#17324D");
    [Fact] public void FreeX_Border_Is_DADCE0()               => BrandThemes.FreeX.Colors.Border.ToHex().Should().Be("#DADCE0");
    [Fact] public void FreeX_BorderStrong_Is_C8CCD0()         => BrandThemes.FreeX.Colors.BorderStrong.ToHex().Should().Be("#C8CCD0");
    [Fact] public void FreeX_Danger_Is_C42B1C()               => BrandThemes.FreeX.Colors.Danger.ToHex().Should().Be("#C42B1C");
    [Fact] public void FreeX_White_Is_FFFFFF()                => BrandThemes.FreeX.Colors.White.ToHex().Should().Be("#FFFFFF");

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

    // ── Structural sanity ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AllThemes_HaveNonEmptyName()
    {
        foreach (var t in new[] { BrandThemes.FreeX, BrandThemes.FreeW, BrandThemes.FreeP, BrandThemes.FreeXMidnight })
        {
            t.Name.Should().NotBeNullOrWhiteSpace();
        }
    }
}
