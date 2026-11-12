using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class StyleGalleryPlannerTests
{
    [Fact]
    public void CustomEntries_ListGalleryFlaggedParagraphAndCharacterStyles_Only()
    {
        var document = TextDocument.CreateEmpty();
        var linked = StyleManager.CreateLinkedStyle(
            document, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);
        var character = StyleManager.CreateStyle(
            document, "Code", null, RunFormatting.Default, ParagraphFormatting.Default, null, StyleType.Character);
        StyleManager.CreateStyle(
            document, "Hidden", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: false);
        document.Styles["Grid"] = new DocumentStyle { Id = "Grid", Name = "Grid", Type = StyleType.Table };

        var entries = StyleGalleryPlanner.CustomEntries(document);

        entries.Should().Contain(new StyleGalleryEntry(linked.Id, "Callout", StyleType.Paragraph));
        entries.Should().Contain(new StyleGalleryEntry(character.Id, "Code", StyleType.Character));
        entries.Select(entry => entry.Name).Should().NotContain(["Callout Char", "Hidden", "Grid", "Normal"]);
        entries.Should().OnlyContain(entry => !StyleManager.IsBuiltIn(entry.Id));
    }

    private static TextDocument Reload(TextDocument document, bool stripQuickFormat)
    {
        using var stream = new MemoryStream();
        FreeW.Core.IO.DocxWriter.Write(document, stream);
        if (stripQuickFormat)
        {
            System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            using var zip = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true);
            var entry = zip.GetEntry("word/styles.xml")!;
            System.Xml.Linq.XDocument styles;
            using (var read = entry.Open())
                styles = System.Xml.Linq.XDocument.Load(read);
            System.Xml.Linq.Extensions.Remove(styles.Descendants(w + "qFormat"));
            entry.Delete();
            using var write = zip.CreateEntry("word/styles.xml").Open();
            styles.Save(write);
        }
        stream.Position = 0;
        return FreeW.Core.IO.DocxReader.Read(stream);
    }

    private static TextDocument DocumentWithShownAndHiddenStyles()
    {
        var document = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(document, "Shown", null, RunFormatting.Default, ParagraphFormatting.Default);
        StyleManager.CreateStyle(
            document, "Shown Code", null, RunFormatting.Default, ParagraphFormatting.Default, null, StyleType.Character);
        StyleManager.CreateLinkedStyle(document, "Shown Linked", null, RunFormatting.Default, ParagraphFormatting.Default);
        StyleManager.CreateStyle(
            document, "Hidden", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: false);
        return document;
    }

    [Fact]
    public void CustomEntries_NewBlankDocument_KeepsItsDefaultCatalogInTheGallery()
    {
        var document = TextDocument.CreateEmpty();

        var expected = document.Styles.Values
            .Where(style => style.Type == StyleType.Paragraph && !StyleManager.IsBuiltIn(style.Id))
            .Select(style => style.Id);

        StyleGalleryPlanner.CustomEntries(document).Select(entry => entry.Id).Should().Equal(expected);
    }

    [Fact]
    public void CustomEntries_WordAuthoredDocument_ShowsOnlyStylesFlaggedWithQFormat()
    {
        var reloaded = Reload(DocumentWithShownAndHiddenStyles(), stripQuickFormat: false);

        var names = StyleGalleryPlanner.CustomEntries(reloaded).Select(entry => entry.Name).ToArray();

        names.Should().Contain(["Shown", "Shown Code", "Shown Linked"]);
        names.Should().NotContain(["Hidden", "Shown Linked Char"]);
    }

    [Fact]
    public void CustomEntries_LegacyDocumentWithoutAnyQFormat_ShowsEveryCustomStyle()
    {
        var reloaded = Reload(DocumentWithShownAndHiddenStyles(), stripQuickFormat: true);

        var names = StyleGalleryPlanner.CustomEntries(reloaded).Select(entry => entry.Name).ToArray();

        names.Should().Contain(["Shown", "Shown Code", "Shown Linked", "Hidden"]);
        names.Should().NotContain("Shown Linked Char", "a linked pair is still one gallery entry");
    }

    [Fact]
    public void CustomEntries_LegacyDocumentResaved_KeepsVisibilityThroughQFormat()
    {
        var legacy = Reload(DocumentWithShownAndHiddenStyles(), stripQuickFormat: true);
        var hiddenId = legacy.Styles.Values.Single(style => style.Name == "Shown").Id;
        StyleManager.ModifyStyle(legacy, hiddenId, quickFormat: false);

        var resaved = Reload(legacy, stripQuickFormat: false);

        var names = StyleGalleryPlanner.CustomEntries(resaved).Select(entry => entry.Name).ToArray();
        names.Should().Contain(["Shown Code", "Shown Linked", "Hidden"]);
        names.Should().NotContain("Shown");
    }

    [Fact]
    public void Signature_ChangesWithCatalogEdits_AndIsStableOtherwise()
    {
        var document = TextDocument.CreateEmpty();
        var before = StyleGalleryPlanner.Signature(document);
        StyleGalleryPlanner.Signature(document).Should().Be(before);

        var style = StyleManager.CreateStyle(
            document, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var created = StyleGalleryPlanner.Signature(document);
        created.Should().NotBe(before);

        StyleManager.ModifyStyle(document, style.Id, name: "Aside");
        var renamed = StyleGalleryPlanner.Signature(document);
        renamed.Should().NotBe(created);

        StyleManager.ModifyStyle(document, style.Id, quickFormat: false);
        var hidden = StyleGalleryPlanner.Signature(document);
        hidden.Should().NotBe(renamed);

        StyleManager.ModifyStyle(document, style.Id, run: new RunFormatting { Bold = true });
        StyleGalleryPlanner.Signature(document).Should().NotBe(hidden);
    }

    [Fact]
    public void BuildContextMenu_OffersTheGalleryCommands_AndProtectsBuiltIns()
    {
        var document = TextDocument.CreateEmpty();
        var style = StyleManager.CreateStyle(
            document, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        var custom = StyleGalleryPlanner.BuildContextMenu(document, style.Id);
        var builtIn = StyleGalleryPlanner.BuildContextMenu(document, "Normal");

        custom.Select(item => item.Command).Should().Equal(Enum.GetValues<StyleGalleryContextCommand>());
        custom.Select(item => item.AutomationId).Should().OnlyHaveUniqueItems();
        custom.Should().OnlyContain(item => item.IsEnabled);
        custom[0].Label.Should().Be(StyleDefinitionDialogText.GalleryUpdateToMatch("Callout"));
        custom[3].Label.Should().Be(StyleDefinitionDialogText.GalleryDelete("Callout"));
        builtIn.Where(item => !item.IsEnabled).Select(item => item.Command).Should().Equal(
            StyleGalleryContextCommand.RemoveFromGallery,
            StyleGalleryContextCommand.Delete);
        StyleGalleryPlanner.BuildContextMenu(document, "Missing").Should().BeEmpty();
    }
}
