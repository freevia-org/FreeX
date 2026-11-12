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
