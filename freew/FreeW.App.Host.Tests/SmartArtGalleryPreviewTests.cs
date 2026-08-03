using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class SmartArtGalleryPreviewTests
{
    [StaFact]
    public void LayoutColorAndStylePreviews_RevertOnLeave()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var smartArt = SmartArt.Create(SmartArtKind.List, ["One", "Two"]);
        smartArt.LayoutId = "list1";
        smartArt.ColorSchemeId = "colorful1";
        smartArt.StyleId = "flat1";
        editor.InsertSmartArt(smartArt);
        editor.CommitToModel();

        var layout = SmartArtLayoutPreset.Catalog.First(preset => preset.Id != smartArt.LayoutId);
        SmartArtGallery.PreviewLayout(editor, layout);
        smartArt.LayoutId.Should().Be(layout.Id);
        smartArt.Kind.Should().Be(layout.Kind);
        SmartArtGallery.EndPreview(editor);
        smartArt.LayoutId.Should().Be("list1");
        smartArt.Kind.Should().Be(SmartArtKind.List);

        var color = SmartArtColorScheme.Catalog.First(scheme => scheme.Id != smartArt.ColorSchemeId);
        SmartArtGallery.PreviewColor(editor, color);
        smartArt.ColorSchemeId.Should().Be(color.Id);
        SmartArtGallery.EndPreview(editor);
        smartArt.ColorSchemeId.Should().Be("colorful1");

        var style = SmartArtStyle.Catalog.First(candidate => candidate.Id != smartArt.StyleId);
        SmartArtGallery.PreviewStyle(editor, style);
        smartArt.StyleId.Should().Be(style.Id);
        SmartArtGallery.EndPreview(editor);
        smartArt.StyleId.Should().Be("flat1");
    }
}
