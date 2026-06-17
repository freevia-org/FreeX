namespace FreeW.Core.Model.Tests;

public class DocumentThemeTests
{
    [Fact]
    public void Catalog_ContainsTheExpectedBuiltInThemes_InOrder()
    {
        DocumentTheme.Catalog.Select(t => t.Name)
            .Should().Equal("Office", "Slate", "Berlin", "Ion");
        DocumentTheme.Default.Name.Should().Be("Office");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        DocumentTheme.FindByName("slate").Should().BeSameAs(DocumentTheme.Catalog[1]);
        DocumentTheme.FindByName("Nope").Should().BeNull();
    }

    [Fact]
    public void Apply_Slate_RewritesTitleAndHeadingColours_AndFonts()
    {
        var doc = TextDocument.CreateEmpty();
        var slate = DocumentTheme.FindByName("Slate")!;

        DocumentTheme.Apply(doc, slate);

        // Title takes the primary accent colour and heading font.
        doc.Styles["Title"].Run.ColorHex.Should().Be(slate.PrimaryColorHex);
        doc.Styles["Title"].Run.FontFamily.Should().Be(slate.HeadingFont);

        // Heading 1 / 2 share the heading colour; Heading 3 gets the darker accent.
        doc.Styles["Heading1"].Run.ColorHex.Should().Be(slate.HeadingColorHex);
        doc.Styles["Heading2"].Run.ColorHex.Should().Be(slate.HeadingColorHex);
        doc.Styles["Heading3"].Run.ColorHex.Should().Be(slate.HeadingAccentColorHex);

        // All heading styles use the heading font.
        foreach (var id in new[] { "Heading1", "Heading2", "Heading3" })
            doc.Styles[id].Run.FontFamily.Should().Be(slate.HeadingFont);

        // Body font lands on Normal + the document default run.
        doc.Styles["Normal"].Run.FontFamily.Should().Be(slate.BodyFont);
        doc.DefaultRun.FontFamily.Should().Be(slate.BodyFont);
    }

    [Fact]
    public void Apply_PreservesNonFontFormatting_SizesAndWeights()
    {
        var doc = TextDocument.CreateEmpty();
        var beforeH1Size = doc.Styles["Heading1"].Run.FontSizePt;
        var beforeTitleBold = doc.Styles["Title"].Run.Bold;

        DocumentTheme.Apply(doc, DocumentTheme.FindByName("Berlin")!);

        // Only fonts/colours change — sizes and weights are untouched.
        doc.Styles["Heading1"].Run.FontSizePt.Should().Be(beforeH1Size);
        doc.Styles["Title"].Run.Bold.Should().Be(beforeTitleBold);
        doc.Styles["Title"].Run.Bold.Should().BeTrue();
    }

    [Fact]
    public void Apply_Office_IsASensibleBaseline_MatchingTheBuiltInDefaults()
    {
        // A fresh doc has the model's built-in style defaults. Applying "Office" should leave the
        // heading colours where they were and reset fonts to the default body/heading face (Calibri).
        var doc = TextDocument.CreateEmpty();

        DocumentTheme.Apply(doc, DocumentTheme.Default);

        doc.Styles["Heading1"].Run.ColorHex.Should().Be("#2F5496");
        doc.Styles["Heading3"].Run.ColorHex.Should().Be("#1F3864");
        doc.Styles["Title"].Run.ColorHex.Should().Be("#000000");
        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.Styles["Normal"].Run.FontFamily.Should().Be("Calibri");
    }

    [Fact]
    public void Apply_DoesNotTouchBodyTextRuns()
    {
        var doc = TextDocument.CreateEmpty();
        var paragraph = new Paragraph("Body text in the default run");
        doc.Blocks.Add(paragraph);
        var formattingBefore = paragraph.Runs[0].Formatting;

        DocumentTheme.Apply(doc, DocumentTheme.FindByName("Ion")!);

        // The run inherits its look through the style/default — the run's own formatting is unchanged.
        paragraph.Runs[0].Formatting.Should().BeSameAs(formattingBefore);
        paragraph.Runs[0].Formatting.FontFamily.Should().BeNull();
    }

    [Fact]
    public void Apply_IsDeterministic_RepeatedApplicationsYieldTheSameCatalog()
    {
        var theme = DocumentTheme.FindByName("Ion")!;

        var a = TextDocument.CreateEmpty();
        DocumentTheme.Apply(a, theme);

        var b = TextDocument.CreateEmpty();
        DocumentTheme.Apply(b, theme);
        DocumentTheme.Apply(b, theme); // applying twice must be idempotent

        foreach (var id in new[] { "Normal", "Title", "Heading1", "Heading2", "Heading3" })
        {
            b.Styles[id].Run.FontFamily.Should().Be(a.Styles[id].Run.FontFamily);
            b.Styles[id].Run.ColorHex.Should().Be(a.Styles[id].Run.ColorHex);
        }
        b.DefaultRun.FontFamily.Should().Be(a.DefaultRun.FontFamily);
    }

    [Fact]
    public void Apply_ThemedStyleFormatting_SurvivesAModelRoundTrip()
    {
        // Themes mutate the in-memory style catalog, which is what round-trips through styles.xml.
        // Assert at the model level: a themed style's changed run formatting is carried by the style
        // catalog (the unit that serialises), so copying the catalog preserves the theme.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });
        var ion = DocumentTheme.FindByName("Ion")!;
        DocumentTheme.Apply(doc, ion);

        // Simulate a write -> read by copying the style catalog into a fresh document (as a save/load
        // of styles.xml would). The themed run formatting must come across intact.
        var reloaded = new TextDocument();
        foreach (var (id, style) in doc.Styles)
            reloaded.Styles[id] = style;
        reloaded.DefaultRun = doc.DefaultRun;

        reloaded.Styles["Heading1"].Run.ColorHex.Should().Be(ion.HeadingColorHex);
        reloaded.Styles["Heading1"].Run.FontFamily.Should().Be(ion.HeadingFont);
        reloaded.Styles["Title"].Run.ColorHex.Should().Be(ion.PrimaryColorHex);
        reloaded.DefaultRun.FontFamily.Should().Be(ion.BodyFont);
    }

    [Fact]
    public void ColorScheme_DrivesAccents1To3FromThePalette_AndNormalisesHex()
    {
        var berlin = DocumentTheme.FindByName("Berlin")!;
        var scheme = berlin.ColorScheme;

        // accent1..3 are the theme's palette colours, normalised to bare uppercase RRGGBB (no '#').
        scheme.Accent1.Should().Be("C00000");
        scheme.Accent2.Should().Be("D2691E");
        scheme.Accent3.Should().Be("8B2500");

        // The dark/light pair and link colours are deterministic stock values.
        scheme.Dark1.Should().Be("000000");
        scheme.Light1.Should().Be("FFFFFF");
        scheme.Hyperlink.Should().Be("0563C1");
        scheme.FollowedHyperlink.Should().Be("954F72");
    }

    [Fact]
    public void TextDocument_DefaultsToOfficeTheme()
    {
        new TextDocument().Theme.Should().BeSameAs(DocumentTheme.Default);
    }

    [Theory]
    [InlineData("Office")]
    [InlineData("Slate")]
    [InlineData("Berlin")]
    [InlineData("Ion")]
    public void InferPreset_RecoversEachCatalogTheme_FromItsOwnSchemeAndFonts(string name)
    {
        var theme = DocumentTheme.FindByName(name)!;

        var inferred = DocumentTheme.InferPreset(theme.ColorScheme, theme.HeadingFont, theme.BodyFont);

        inferred.Should().BeSameAs(theme);
    }

    [Fact]
    public void InferPreset_FallsBackToOffice_ForAnUnrecognisedScheme()
    {
        var foreign = new ThemeColorScheme(
            "000000", "FFFFFF", "111111", "EEEEEE",
            "123456", "234567", "345678", "456789", "56789A", "6789AB",
            "0563C1", "954F72");

        DocumentTheme.InferPreset(foreign, "Arial", "Arial").Should().BeSameAs(DocumentTheme.Default);
    }
}
