namespace FreeW.Core.Model.Tests;

public class StyleManagerTests
{
    [Fact]
    public void CreateStyle_AddsStyle_AndGeneratesIdFromName()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "My Heading", basedOnId: "Normal",
            new RunFormatting { Bold = true, FontSizePt = 14 },
            new ParagraphFormatting { Alignment = TextAlignment.Center });

        style.Id.Should().Be("MyHeading"); // spaces stripped
        style.Name.Should().Be("My Heading");
        style.BasedOnStyleId.Should().Be("Normal");
        style.Run.Bold.Should().BeTrue();
        style.Paragraph.Alignment.Should().Be(TextAlignment.Center);
        doc.Styles.Should().ContainKey("MyHeading");
        doc.Styles["MyHeading"].Should().BeSameAs(style);
    }

    [Fact]
    public void CreateStyle_TrimsName_AndIsTreatedAsParagraphStyle()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "  Spaced  ", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Name.Should().Be("Spaced");
        style.Id.Should().Be("Spaced");
        style.Type.Should().Be(StyleType.Paragraph);
    }

    [Fact]
    public void CreateStyle_GeneratesUniqueId_OnCollisionWithBuiltIn()
    {
        var doc = TextDocument.CreateEmpty();

        // "Normal" is a built-in id; a custom style named "Normal" must not clobber it.
        var style = StyleManager.CreateStyle(
            doc, "Normal", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Id.Should().Be("Normal2");
        doc.Styles["Normal"].Name.Should().Be("Normal");      // built-in untouched
        doc.Styles["Normal2"].Should().BeSameAs(style);
    }

    [Fact]
    public void CreateStyle_GeneratesUniqueId_AcrossRepeatedNames()
    {
        var doc = TextDocument.CreateEmpty();

        var a = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var b = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var c = StyleManager.CreateStyle(doc, "Call out!", null, RunFormatting.Default, ParagraphFormatting.Default);

        a.Id.Should().Be("Callout");
        b.Id.Should().Be("Callout2");
        c.Id.Should().Be("Callout3"); // "Call out!" compacts to "Callout" -> next free suffix
    }

    [Fact]
    public void CreateStyle_FallsBackToStyleId_WhenNameHasNoAlphanumerics()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(doc, "!!!", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Id.Should().Be("Style");
        style.Name.Should().Be("!!!");
    }

    [Fact]
    public void CreateStyle_IgnoresUnknownBasedOn()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "Floating", basedOnId: "DoesNotExist", RunFormatting.Default, ParagraphFormatting.Default);

        style.BasedOnStyleId.Should().BeNull();
    }

    [Fact]
    public void CreateStyle_RejectsEmptyName()
    {
        var doc = TextDocument.CreateEmpty();

        var act = () => StyleManager.CreateStyle(doc, "   ", null, RunFormatting.Default, ParagraphFormatting.Default);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ModifyStyle_UpdatesFormattingNameAndBasedOn_KeepingId()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        var updated = StyleManager.ModifyStyle(
            doc, created.Id,
            run: new RunFormatting { Italic = true },
            para: new ParagraphFormatting { Alignment = TextAlignment.Right },
            name: "Callout Box",
            basedOnId: "Heading1");

        updated.Should().NotBeNull();
        updated!.Id.Should().Be("Callout");          // id never changes
        updated.Name.Should().Be("Callout Box");
        updated.Run.Italic.Should().BeTrue();
        updated.Paragraph.Alignment.Should().Be(TextAlignment.Right);
        updated.BasedOnStyleId.Should().Be("Heading1");
        doc.Styles["Callout"].Should().BeSameAs(updated);
    }

    [Fact]
    public void ModifyStyle_ReturnsNull_ForUnknownStyle()
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.ModifyStyle(doc, "Nope", run: new RunFormatting { Bold = true }).Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_IgnoresUnknownBasedOn_AndSelfReference()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, created.Id, basedOnId: "Ghost")!.BasedOnStyleId.Should().Be("Normal");
        StyleManager.ModifyStyle(doc, created.Id, basedOnId: created.Id)!.BasedOnStyleId.Should().Be("Normal");
    }

    [Fact]
    public void ModifyStyle_CanClearBasedOn()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, created.Id, clearBasedOn: true)!.BasedOnStyleId.Should().BeNull();
    }

    [Fact]
    public void DeleteStyle_RemovesCustomStyle()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.DeleteStyle(doc, created.Id).Should().BeTrue();
        doc.Styles.Should().NotContainKey("Callout");
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Heading1")]
    [InlineData("Heading2")]
    [InlineData("Title")]
    [InlineData("Subtitle")]
    [InlineData("Quote")]
    [InlineData("Caption")]
    public void DeleteStyle_RefusesBuiltIn(string builtInId)
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.DeleteStyle(doc, builtInId).Should().BeFalse();
        doc.Styles.Should().ContainKey(builtInId);
    }

    [Fact]
    public void DeleteStyle_ReturnsFalse_ForUnknownStyle()
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.DeleteStyle(doc, "Nope").Should().BeFalse();
    }

    [Fact]
    public void IsBuiltIn_TrueForSeededStyles_FalseForCustom()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.IsBuiltIn("Normal").Should().BeTrue();
        StyleManager.IsBuiltIn(created.Id).Should().BeFalse();
    }
}
