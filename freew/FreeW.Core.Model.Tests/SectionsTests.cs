namespace FreeW.Core.Model.Tests;

public class SectionsTests
{
    [Fact]
    public void Sections_WithNoBreaks_YieldsSingleSectionBackedByPage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("only"));

        doc.Sections.Should().HaveCount(1);
        // The single section's page settings are the document-wide Page (same instance).
        doc.Sections[0].Page.Should().BeSameAs(doc.Page);
    }

    [Fact]
    public void Sections_ReconstructsOrderedSectionsFromParagraphMarkers()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("section one")
        {
            SectionBreak = new Section(new PageSettings { MarginLeftPt = 10 }, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("section two")
        {
            SectionBreak = new Section(new PageSettings { MarginLeftPt = 20 }, SectionBreakKind.Continuous)
        });
        doc.Blocks.Add(new Paragraph("final section"));

        var sections = doc.Sections;

        sections.Should().HaveCount(3);
        sections[0].Page.MarginLeftPt.Should().Be(10);
        sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        sections[1].Page.MarginLeftPt.Should().Be(20);
        sections[1].BreakKind.Should().Be(SectionBreakKind.Continuous);
        // The trailing section is always the document-wide Page.
        sections[2].Page.Should().BeSameAs(doc.Page);
    }

    [Fact]
    public void Section_DefaultBreakKind_IsNextPage()
    {
        var section = new Section(new PageSettings());
        section.BreakKind.Should().Be(SectionBreakKind.NextPage);
    }

    [Fact]
    public void PageSettingsClone_CopiesAllFieldsAndIsIndependent()
    {
        var original = new PageSettings
        {
            WidthPt = 100,
            HeightPt = 200,
            MarginLeftPt = 11,
            MarginRightPt = 12,
            MarginTopPt = 13,
            MarginBottomPt = 14,
            Landscape = true,
            ColumnCount = 3,
            ColumnSpacingPt = 18,
            PageBorder = new PageBorder("#123456", 2.5),
            Watermark = "DRAFT",
            LineNumberMode = LineNumberMode.Continuous,
            LineNumberCountBy = 5,
            AutoHyphenation = true,
            VerticalAlignment = PageVerticalAlignment.Center,
            DifferentFirstPage = true
        };

        var clone = original.Clone();

        clone.Should().NotBeSameAs(original);
        clone.WidthPt.Should().Be(100);
        clone.HeightPt.Should().Be(200);
        clone.MarginLeftPt.Should().Be(11);
        clone.MarginRightPt.Should().Be(12);
        clone.MarginTopPt.Should().Be(13);
        clone.MarginBottomPt.Should().Be(14);
        clone.Landscape.Should().BeTrue();
        clone.ColumnCount.Should().Be(3);
        clone.ColumnSpacingPt.Should().Be(18);
        clone.PageBorder.Should().Be(new PageBorder("#123456", 2.5));
        clone.Watermark.Should().Be("DRAFT");
        clone.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        clone.LineNumberCountBy.Should().Be(5);
        clone.AutoHyphenation.Should().BeTrue();
        clone.VerticalAlignment.Should().Be(PageVerticalAlignment.Center);
        clone.DifferentFirstPage.Should().BeTrue();

        // Mutating the clone does not affect the original.
        clone.WidthPt = 999;
        original.WidthPt.Should().Be(100);
    }
}
