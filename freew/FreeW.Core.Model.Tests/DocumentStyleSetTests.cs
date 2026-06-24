namespace FreeW.Core.Model.Tests;

public class DocumentStyleSetTests
{
    [Fact]
    public void Catalog_ContainsTheExpectedBuiltInStyleSets_InOrder()
    {
        // Original four presets, plus six added for Word parity, in order.
        DocumentStyleSet.Catalog.Select(s => s.Name)
            .Should().Equal(
                "Office", "Simple", "Elegant", "Formal",
                "Lines (Simple)", "Minimalist", "Shadow", "Shaded",
                "Word 2003", "Word 2010");
        DocumentStyleSet.Default.Name.Should().Be("Office");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        DocumentStyleSet.FindByName("elegant").Should().BeSameAs(DocumentStyleSet.Catalog[2]);
        DocumentStyleSet.FindByName("Nope").Should().BeNull();
    }

    [Fact]
    public void Apply_RewritesBuiltInStyleFormatting_AndDefaultRun()
    {
        var doc = TextDocument.CreateEmpty();
        var elegant = DocumentStyleSet.FindByName("Elegant")!;

        DocumentStyleSet.Apply(doc, elegant);

        doc.DefaultRun.FontFamily.Should().Be(elegant.BodyFont);
        doc.DefaultRun.FontSizePt.Should().Be(11);

        doc.Styles["Normal"].Run.FontFamily.Should().Be(elegant.BodyFont);
        doc.Styles["Title"].Run.FontFamily.Should().Be(elegant.HeadingFont);
        doc.Styles["Title"].Run.ColorHex.Should().Be(elegant.AccentColorHex);
        doc.Styles["Heading1"].Run.FontFamily.Should().Be(elegant.HeadingFont);
        doc.Styles["Heading1"].Run.ColorHex.Should().Be(elegant.AccentColorHex);
        doc.Styles["Heading3"].Run.ColorHex.Should().Be("#3F2A1F");
        doc.Styles["Quote"].Paragraph.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void Apply_PreservesStyleIdentityAndCustomStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var heading = doc.Styles["Heading1"];
        var custom = StyleManager.CreateStyle(
            doc,
            "Callout",
            "Normal",
            new RunFormatting { Bold = true, ColorHex = "#FF0000" },
            ParagraphFormatting.Default,
            nextStyleId: "Normal");

        DocumentStyleSet.Apply(doc, DocumentStyleSet.FindByName("Simple")!);

        doc.Styles["Heading1"].Should().BeSameAs(heading);
        doc.Styles["Heading1"].Id.Should().Be("Heading1");
        doc.Styles["Heading1"].Name.Should().Be("Heading 1");
        doc.Styles["Heading1"].BasedOnStyleId.Should().Be("Normal");
        doc.Styles["Callout"].Should().BeSameAs(custom);
        doc.Styles["Callout"].Run.ColorHex.Should().Be("#FF0000");
        doc.Styles["Callout"].NextStyleId.Should().Be("Normal");
    }

    [Fact]
    public void Apply_IsDeterministic_RepeatedApplicationsYieldTheSameCatalog()
    {
        var styleSet = DocumentStyleSet.FindByName("Formal")!;

        var a = TextDocument.CreateEmpty();
        DocumentStyleSet.Apply(a, styleSet);

        var b = TextDocument.CreateEmpty();
        DocumentStyleSet.Apply(b, styleSet);
        DocumentStyleSet.Apply(b, styleSet);

        foreach (var id in new[] { "Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote" })
        {
            b.Styles[id].Run.Should().Be(a.Styles[id].Run);
            b.Styles[id].Paragraph.Should().Be(a.Styles[id].Paragraph);
        }
        b.DefaultRun.Should().Be(a.DefaultRun);
    }
}
