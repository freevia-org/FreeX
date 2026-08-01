namespace FreeW.Core.Model.Tests;

/// <summary>
/// Model-layer tests for the Design-tab depth additions (W23):
/// <list type="bullet">
/// <item>Extended <see cref="DocumentStyleSet.Catalog"/> (10 presets) and Reset to Default.</item>
/// <item>Custom paragraph spacing (<see cref="DocumentParagraphSpacingSet"/> "Custom" path).</item>
/// <item>Custom theme color scheme (<see cref="ThemeColorScheme"/> author path via <see cref="DocumentTheme.ApplyColors"/>).</item>
/// <item>Custom font pair (<see cref="DocumentFontSet"/> author path).</item>
/// <item><see cref="PageBorder.ArtId"/> field default and preservation.</item>
/// <item><see cref="WatermarkOptions.IsPicture"/> and image watermark fields.</item>
/// </list>
/// </summary>
public class DesignDepthModelTests
{
    // ── Style sets ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StyleSet_Catalog_HasTenPresetsIncludingExtended()
    {
        DocumentStyleSet.Catalog.Should().HaveCount(10);
        // Original four must remain first.
        DocumentStyleSet.Catalog[0].Name.Should().Be("Office");
        DocumentStyleSet.Catalog[1].Name.Should().Be("Simple");
        DocumentStyleSet.Catalog[2].Name.Should().Be("Elegant");
        DocumentStyleSet.Catalog[3].Name.Should().Be("Formal");
        // Six new presets follow.
        DocumentStyleSet.Catalog.Skip(4).Should().HaveCount(6);
    }

    [Theory]
    [InlineData("Lines (Simple)")]
    [InlineData("Minimalist")]
    [InlineData("Shadow")]
    [InlineData("Shaded")]
    [InlineData("Word 2003")]
    [InlineData("Word 2010")]
    public void StyleSet_NewPresets_FindByNameSucceeds(string name)
    {
        DocumentStyleSet.FindByName(name).Should().NotBeNull();
    }

    [Fact]
    public void StyleSet_Apply_NewPreset_SetsExpectedBodyFont()
    {
        var doc = TextDocument.CreateEmpty();
        var minimalist = DocumentStyleSet.FindByName("Minimalist")!;

        DocumentStyleSet.Apply(doc, minimalist);

        doc.DefaultRun.FontFamily.Should().Be("Arial");
        doc.Styles["Normal"].Run.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void StyleSet_ResetToDefault_AppliesOffice()
    {
        var doc = TextDocument.CreateEmpty();
        // First apply a non-default.
        DocumentStyleSet.Apply(doc, DocumentStyleSet.FindByName("Elegant")!);
        // Then reset.
        DocumentStyleSet.Apply(doc, DocumentStyleSet.Default);

        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.Styles["Title"].Run.ColorHex.Should().Be(DocumentStyleSet.Default.AccentColorHex);
    }

    // ── Custom paragraph spacing ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomParagraphSpacing_AppliesExactValues()
    {
        var doc = TextDocument.CreateEmpty();
        // Simulate what CustomParagraphSpacingDialog produces — a "Custom" spacing set.
        var custom = new DocumentParagraphSpacingSet("Custom", 6.0, 12.0, 1.25);

        DocumentParagraphSpacingSet.Apply(doc, custom);

        doc.DefaultParagraph.SpaceBeforePt.Should().Be(6.0);
        doc.DefaultParagraph.SpaceAfterPt.Should().Be(12.0);
        doc.DefaultParagraph.LineSpacing.Should().Be(1.25);
        doc.DefaultParagraph.LineRule.Should().Be(LineSpacingRule.Multiple);
    }

    [Fact]
    public void CustomParagraphSpacing_PreservesCustomStyleSpacing()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc, "Callout", "Normal",
            new RunFormatting { Bold = true },
            ParagraphFormatting.Default with { SpaceAfterPt = 18, SpaceAfterIsSet = true });

        var spacing = new DocumentParagraphSpacingSet("Custom", 0, 8, 1.0);
        DocumentParagraphSpacingSet.Apply(doc, spacing);

        // Custom style spacing was explicitly set — but Apply() rewrites it for built-in styles
        // (Callout is not in the built-in list, so it should be untouched).
        doc.Styles["Callout"].Paragraph.SpaceAfterPt.Should().Be(18);
    }

    // ── Custom theme colors ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomThemeColors_ApplyColors_UpdatesThreeAccentSlots()
    {
        var doc = TextDocument.CreateEmpty();
        // Construct a custom theme with arbitrary accent colors.
        var customTheme = new DocumentTheme(
            "Custom", "Calibri", "Calibri",
            "#AABBCC", "#112233", "#DDEEFF");

        DocumentTheme.ApplyColors(doc, customTheme);

        doc.Theme.PrimaryColorHex.Should().Be("#AABBCC");
        doc.Theme.HeadingColorHex.Should().Be("#112233");
        doc.Theme.HeadingAccentColorHex.Should().Be("#DDEEFF");
        // Fonts must be preserved.
        doc.Theme.HeadingFont.Should().Be(DocumentTheme.Default.HeadingFont);
        doc.Theme.BodyFont.Should().Be(DocumentTheme.Default.BodyFont);
    }

    [Fact]
    public void CustomThemeColors_ThemeColorScheme_AllTwelveSlotsRoundTrip()
    {
        var scheme = new ThemeColorScheme(
            "111111", "222222", "333333", "444444",
            "555555", "666666", "777777", "888888",
            "999999", "AAAAAA", "BBBBBB", "CCCCCC");

        scheme.Dark1.Should().Be("111111");
        scheme.Light1.Should().Be("222222");
        scheme.Accent1.Should().Be("555555");
        scheme.Accent6.Should().Be("AAAAAA");
        scheme.Hyperlink.Should().Be("BBBBBB");
        scheme.FollowedHyperlink.Should().Be("CCCCCC");
    }

    [Fact]
    public void CustomThemeColors_InferPreset_CustomScheme_ReturnsMutatedCustomTheme()
    {
        // A scheme that doesn't match any built-in preset should return "Custom" with correct colors.
        var scheme = new ThemeColorScheme(
            "000000", "FFFFFF", "111111", "EEEEEE",
            "ABCDEF", "FEDCBA", "123456", "456789",
            "789ABC", "BCDEF0", "0563C1", "954F72");

        var inferred = DocumentTheme.InferPreset(scheme, "Calibri", "Calibri");

        inferred.Name.Should().Be("Custom");
        inferred.PrimaryColorHex.Should().Be("#ABCDEF");
        inferred.HeadingColorHex.Should().Be("#FEDCBA");
        inferred.HeadingAccentColorHex.Should().Be("#123456");
    }

    // ── Custom font pair ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomFontSet_Apply_UpdatesThemeAndStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var customFonts = new DocumentFontSet("Custom", "Garamond", "Verdana");

        DocumentFontSet.Apply(doc, customFonts);

        doc.Theme.HeadingFont.Should().Be("Garamond");
        doc.Theme.BodyFont.Should().Be("Verdana");
        doc.DefaultRun.FontFamily.Should().Be("Verdana");
        doc.Styles["Title"].Run.FontFamily.Should().Be("Garamond");
        doc.Styles["Normal"].Run.FontFamily.Should().Be("Verdana");
    }

    [Fact]
    public void CustomFontSet_FindByName_ArbitraryNameReturnsNull()
    {
        // A custom-named font set is not in the catalog.
        DocumentFontSet.FindByName("Custom").Should().BeNull();
        DocumentFontSet.FindByName("Garamond+Verdana").Should().BeNull();
    }

    // ── PageBorder ArtId ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PageBorder_ArtId_DefaultIsZero()
    {
        var border = new PageBorder();
        border.ArtId.Should().Be(0);
    }

    [Fact]
    public void PageBorder_ArtId_CanBeSet()
    {
        var border = new PageBorder("#FF0000", 2.0) { ArtId = 38 };
        border.ArtId.Should().Be(38);
        border.ColorHex.Should().Be("#FF0000");
    }

    [Fact]
    public void PageBorder_ArtId_WithRecord_DoesNotChangeLineStyle()
    {
        var border = new PageBorder("#000000", 1.0) { LineStyle = BorderLineStyle.Double, ArtId = 84 };
        var copy = border with { ArtId = 1 };
        copy.LineStyle.Should().Be(BorderLineStyle.Double);
        copy.ArtId.Should().Be(1);
    }

    [Fact]
    public void PageBorderArtStyles_CuratedMappingsUseWordIdsAndCanonicalTokens()
    {
        PageBorderArtStyles.Curated.Should().HaveCount(17);
        PageBorderArtStyles.Curated.Select(style => style.ArtId).Should().OnlyHaveUniqueItems();
        PageBorderArtStyles.Curated.Select(style => style.Token).Should().OnlyHaveUniqueItems();
        PageBorderArtStyles.TryGetById(1, out var apples).Should().BeTrue();
        apples.Should().Be(new PageBorderArtStyle(1, "apples", "Apples"));
        PageBorderArtStyles.TryGetByToken("people", out var people).Should().BeTrue();
        people.ArtId.Should().Be(84);
    }

    // ── WatermarkOptions image fields ─────────────────────────────────────────────────────────────

    [Fact]
    public void WatermarkOptions_IsPicture_FalseWhenNoBytesSet()
    {
        new WatermarkOptions("DRAFT").IsPicture.Should().BeFalse();
        new WatermarkOptions(string.Empty) { ImageBytes = null }.IsPicture.Should().BeFalse();
        new WatermarkOptions(string.Empty) { ImageBytes = [] }.IsPicture.Should().BeFalse();
    }

    [Fact]
    public void WatermarkOptions_IsPicture_TrueWhenImageBytesSet()
    {
        var opts = new WatermarkOptions(string.Empty) { ImageBytes = [0x89, 0x50, 0x4E, 0x47] };
        opts.IsPicture.Should().BeTrue();
    }

    [Fact]
    public void WatermarkOptions_ImageFields_DefaultToZeroAndNull()
    {
        var opts = new WatermarkOptions("DRAFT");
        opts.ImageBytes.Should().BeNull();
        opts.ScalePct.Should().Be(0);
    }

    [Fact]
    public void WatermarkOptions_PictureMode_AllFieldsPreserved()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var opts = new WatermarkOptions(string.Empty)
        {
            ImageBytes = bytes,
            ScalePct   = 200,
            Layout     = WatermarkLayout.Horizontal,
            Opacity    = 1.0,
        };

        opts.ImageBytes.Should().Equal(bytes);
        opts.ScalePct.Should().Be(200);
        opts.Layout.Should().Be(WatermarkLayout.Horizontal);
        opts.Opacity.Should().BeApproximately(1.0, 0.001);
    }
}
