using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Hovering a SmartArt Design gallery item previews the layout/colour/style on the selected SmartArt,
/// and moving off it reverts to what the document actually held. The preview must never leave the
/// hovered value behind -- that would silently change the document from a mouse-over.
/// <para>
/// The static <c>SmartArtGallery</c> helpers this test used to call were removed when the preview
/// logic moved to the shared <c>SmartArtDesignPreviews</c> session; the host now exposes the same
/// behaviour as instance methods on <see cref="DocumentView"/>. The assertions are unchanged -- only
/// the entry points are, so this still covers the revert-on-leave contract rather than the helper
/// that used to implement it.
/// </para>
/// </summary>
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
        editor.PreviewSelectedSmartArtLayout(layout);
        smartArt.LayoutId.Should().Be(layout.Id);
        smartArt.Kind.Should().Be(layout.Kind);
        editor.CancelSmartArtDesignPreview();
        smartArt.LayoutId.Should().Be("list1");
        smartArt.Kind.Should().Be(SmartArtKind.List);

        var color = SmartArtColorScheme.Catalog.First(scheme => scheme.Id != smartArt.ColorSchemeId);
        editor.PreviewSelectedSmartArtColorScheme(color);
        smartArt.ColorSchemeId.Should().Be(color.Id);
        editor.CancelSmartArtDesignPreview();
        smartArt.ColorSchemeId.Should().Be("colorful1");

        var style = SmartArtStyle.Catalog.First(candidate => candidate.Id != smartArt.StyleId);
        editor.PreviewSelectedSmartArtStyle(style);
        smartArt.StyleId.Should().Be(style.Id);
        editor.CancelSmartArtDesignPreview();
        smartArt.StyleId.Should().Be("flat1");
    }
}
