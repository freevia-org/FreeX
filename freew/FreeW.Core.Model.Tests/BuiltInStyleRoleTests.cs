namespace FreeW.Core.Model.Tests;

public class BuiltInStyleRoleTests
{
    [Fact]
    public void RoleCatalog_HasTheExpectedPortableStylesAndValidHeadingMetadata()
    {
        BuiltInStyles.RoleCatalog
            .Select(descriptor => (descriptor.Id, descriptor.Role, descriptor.HeadingLevel))
            .Should().Equal(
                ("Normal", BuiltInStyleRole.Normal, null),
                ("Heading1", BuiltInStyleRole.Heading, 1),
                ("Heading2", BuiltInStyleRole.Heading, 2),
                ("Heading3", BuiltInStyleRole.Heading, 3),
                ("Heading4", BuiltInStyleRole.Heading, 4),
                ("Title", BuiltInStyleRole.Title, null),
                ("Subtitle", BuiltInStyleRole.Subtitle, null),
                ("Quote", BuiltInStyleRole.Quote, null));

        BuiltInStyles.RoleCatalog.Should().OnlyContain(descriptor =>
            descriptor.Type == StyleType.Paragraph && descriptor.Role != BuiltInStyleRole.None);
        BuiltInStyles.RoleCatalog
            .Where(descriptor => descriptor.Role != BuiltInStyleRole.Heading)
            .Should().OnlyContain(descriptor => descriptor.HeadingLevel == null);
    }

    [Fact]
    public void RoleCatalog_DoesNotDriftFromGalleryOrOutlineLookup()
    {
        BuiltInStyles.RoleCatalog.Should().Equal(
            BuiltInStyles.Gallery.Where(descriptor => descriptor.Role != BuiltInStyleRole.None));

        BuiltInStyles.FindByOutlineLevel(0)?.Id.Should().Be("Title");
        for (var level = 1; level <= 4; level++)
        {
            var descriptor = BuiltInStyles.FindByOutlineLevel(level);
            descriptor.Should().NotBeNull();
            descriptor!.Id.Should().Be($"Heading{level}");
            descriptor.HeadingLevel.Should().Be(level);
        }

        BuiltInStyles.FindByOutlineLevel(5).Should().BeNull(
            "Heading5 remains an outline-only style outside the portable formatting role catalog");
    }

    [Fact]
    public void CreateEmpty_SeedsEveryPortableRoleFromItsCanonicalDescriptor()
    {
        var document = TextDocument.CreateEmpty();

        foreach (var descriptor in BuiltInStyles.RoleCatalog)
        {
            document.Styles.Should().ContainKey(descriptor.Id);
            document.Styles[descriptor.Id].Should().BeEquivalentTo(descriptor.Create());
        }
    }

    [Fact]
    public void FormattingPolicies_TreatHeading4AsAHeadingWithoutTouchingGalleryOnlyStyles()
    {
        var document = TextDocument.CreateEmpty();
        BuiltInStyles.EnsureAllSeeded(document);
        var galleryOnlySnapshots = BuiltInStyles.Gallery
            .Where(descriptor => descriptor.Role == BuiltInStyleRole.None)
            .ToDictionary(
                descriptor => descriptor.Id,
                descriptor => (document.Styles[descriptor.Id].Run, document.Styles[descriptor.Id].Paragraph));
        var subtitleBeforeTheme = document.Styles["Subtitle"].Run;
        var quoteBeforeTheme = document.Styles["Quote"].Run;

        var slate = DocumentTheme.FindByName("Slate")!;
        DocumentTheme.Apply(document, slate);
        document.Styles["Heading4"].Run.FontFamily.Should().Be(slate.HeadingFont);
        document.Styles["Heading4"].Run.ColorHex.Should().Be(slate.HeadingAccentColorHex);
        document.Styles["Subtitle"].Run.Should().Be(subtitleBeforeTheme,
            "full theme application historically leaves Subtitle unchanged");
        document.Styles["Quote"].Run.Should().Be(quoteBeforeTheme,
            "full theme application historically leaves Quote unchanged");

        var cambria = DocumentFontSet.FindByName("Cambria")!;
        DocumentFontSet.Apply(document, cambria);
        document.Styles["Heading4"].Run.FontFamily.Should().Be(cambria.HeadingFont);

        var berlin = DocumentTheme.FindByName("Berlin")!;
        DocumentTheme.ApplyColors(document, berlin);
        document.Styles["Heading4"].Run.ColorHex.Should().Be(berlin.HeadingAccentColorHex);
        document.Styles["Heading4"].Run.FontFamily.Should().Be(cambria.HeadingFont);

        var relaxed = DocumentParagraphSpacingSet.FindByName("Relaxed")!;
        DocumentParagraphSpacingSet.Apply(document, relaxed);
        AssertSpacing(document.Styles["Heading4"].Paragraph, before: 0, after: 6, line: 1.5);

        var elegant = DocumentStyleSet.FindByName("Elegant")!;
        DocumentStyleSet.Apply(document, elegant);
        var heading4 = document.Styles["Heading4"];
        heading4.Run.FontFamily.Should().Be(elegant.HeadingFont);
        heading4.Run.FontSizePt.Should().Be(11);
        heading4.Run.Bold.Should().BeTrue();
        heading4.Run.Italic.Should().BeTrue();
        heading4.Run.ColorHex.Should().Be("#3F2A1F");
        heading4.Paragraph.SpaceBeforePt.Should().Be(6);
        heading4.Paragraph.SpaceAfterPt.Should().Be(2);

        foreach (var (styleId, snapshot) in galleryOnlySnapshots)
        {
            document.Styles[styleId].Run.Should().Be(snapshot.Run,
                $"gallery-only style '{styleId}' is outside portable role policies");
            document.Styles[styleId].Paragraph.Should().Be(snapshot.Paragraph,
                $"gallery-only style '{styleId}' is outside portable role policies");
        }
    }

    private static void AssertSpacing(ParagraphFormatting paragraph, double before, double after, double line)
    {
        paragraph.SpaceBeforePt.Should().Be(before);
        paragraph.SpaceAfterPt.Should().Be(after);
        paragraph.LineSpacing.Should().Be(line);
        paragraph.SpaceBeforeIsSet.Should().BeTrue();
        paragraph.SpaceAfterIsSet.Should().BeTrue();
        paragraph.LineSpacingIsSet.Should().BeTrue();
    }
}
