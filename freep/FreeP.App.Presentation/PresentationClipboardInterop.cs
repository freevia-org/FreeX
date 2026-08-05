using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Portable formats exposed by both FreeP desktop hosts.</summary>
public static class PresentationClipboardFormats
{
    public const string Selection = "freex.freep.selection.v1";
    public const string OwnerToken = "freex.freep.owner-token.v1";
    public const string RichText = "freex.freep.rich-text.v1";

    // Native rich text names used by Avalonia's platform-format bridge.
    public const string WindowsRtf = "Rich Text Format";
    public const string LinuxRtf = "text/rtf";
    public const string WindowsXamlPackage = "XamlPackage";
    public const string LinuxXamlPackage = "application/xamlpackage";
}

/// <summary>Framework-neutral payload written to or read from a system clipboard.</summary>
public sealed record PresentationClipboardContent(
    byte[]? SelectionBytes = null,
    byte[]? PngBytes = null,
    string? Text = null,
    string? OwnerToken = null,
    byte[]? RichTextBytes = null,
    byte[]? XamlPackageBytes = null,
    byte[]? RtfBytes = null)
{
    public bool HasSelection => SelectionBytes is { Length: > 0 };
    public bool HasImage => PngBytes is { Length: > 0 };
    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasRichText => RichTextBytes is { Length: > 0 } || RtfBytes is { Length: > 0 };
    public bool HasXamlPackage => XamlPackageBytes is { Length: > 0 };
    public bool IsEmpty => !HasSelection && !HasImage && !HasText && !HasRichText && !HasXamlPackage;
}

/// <summary>Creates and reads the native FreeP selection clipboard format.</summary>
public static class PresentationClipboardSelectionCodec
{
    public static byte[] Serialize(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(shapes);

        if (shapes.Count == 0)
            return [];

        var clipboardSlide = SlideCloner.CloneSlide(slide);
        clipboardSlide.Shapes.Clear();
        foreach (var shape in shapes)
            clipboardSlide.Shapes.Add(SlideCloner.CloneShape(shape));

        // Keep only the selected slide content while retaining the source presentation's
        // theme/layout context. The writer reads these models but does not mutate them.
        var clipboardPresentation = new Presentation
        {
            SlideSizeCxEmu = presentation.SlideSizeCxEmu,
            SlideSizeCyEmu = presentation.SlideSizeCyEmu,
            NotesPageSizeCxEmu = presentation.NotesPageSizeCxEmu,
            NotesPageSizeCyEmu = presentation.NotesPageSizeCyEmu,
            Theme = presentation.Theme,
        };
        clipboardPresentation.Masters.AddRange(presentation.Masters);
        clipboardPresentation.Layouts.AddRange(presentation.Layouts);
        clipboardPresentation.Slides.Add(clipboardSlide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(clipboardPresentation, stream);
        return stream.ToArray();
    }

    public static IReadOnlyList<SlideShape> Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            return [];

        using var stream = new MemoryStream(bytes, writable: false);
        var presentation = PptxPackageReader.Read(stream);
        var slide = presentation.Slides.FirstOrDefault();
        return slide is null
            ? []
            : slide.Shapes.Select(SlideCloner.CloneShape).ToArray();
    }
}

public static class PresentationClipboardContentFactory
{
    public static PresentationClipboardContent? CreateSelection(
        EditingSession editor,
        Func<Presentation, Slide, IReadOnlyList<SlideShape>, byte[]> renderPng,
        string ownerToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(renderPng);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);

        var slide = editor.CurrentSlide;
        if (slide is null || editor.SelectedShapeIds.Count == 0)
            return null;

        var selected = editor.SelectedShapeIds
            .Select(id => FindShape(slide.Shapes, id))
            .Where(static shape => shape is not null)
            .Select(static shape => shape!)
            .ToArray();
        if (selected.Length == 0)
            return null;

        var content = CreateSelection(
            editor.Presentation,
            slide,
            selected,
            renderPng,
            ownerToken);
        return content.IsEmpty ? null : content;
    }

    public static PresentationClipboardContent CreateSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes,
        Func<Presentation, Slide, IReadOnlyList<SlideShape>, byte[]> renderPng,
        string ownerToken)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(renderPng);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);

        byte[]? selectionBytes = null;
        byte[]? pngBytes = null;
        try
        {
            selectionBytes = PresentationClipboardSelectionCodec.Serialize(
                presentation,
                slide,
                shapes);
        }
        catch
        {
            // Image/text fallbacks remain useful when native serialization fails.
        }

        try
        {
            pngBytes = renderPng(presentation, slide, shapes);
        }
        catch
        {
            // Native selection/text fallbacks remain useful when rendering fails.
        }

        return new PresentationClipboardContent(
            selectionBytes,
            pngBytes,
            ExtractText(shapes),
            ownerToken);
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    public static string? ExtractText(IEnumerable<SlideShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        var parts = new List<string>();
        foreach (var shape in shapes)
        {
            if (shape.TextBody is null)
                continue;

            var shapeText = string.Join(
                Environment.NewLine,
                shape.TextBody.Paragraphs.Select(paragraph =>
                    string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty))));
            if (!string.IsNullOrEmpty(shapeText))
                parts.Add(shapeText);
        }

        return parts.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}

public enum PresentationClipboardPasteSource
{
    NativeSelection,
    Image,
    RichText,
    XamlPackage,
    Text,
    Internal,
    Nothing,
}

public static class PresentationClipboardPastePlanner
{
    public static PresentationClipboardPasteSource Decide(
        bool hasNativeSelection,
        bool hasImage,
        bool hasText,
        bool internalHasData,
        bool ownCopyIsCurrent,
        bool hasRichText = false,
        bool hasXamlPackage = false)
    {
        if (ownCopyIsCurrent && internalHasData)
            return PresentationClipboardPasteSource.Internal;
        if (hasNativeSelection)
            return PresentationClipboardPasteSource.NativeSelection;
        if (hasImage)
            return PresentationClipboardPasteSource.Image;
        if (hasRichText)
            return PresentationClipboardPasteSource.RichText;
        if (hasXamlPackage)
            return PresentationClipboardPasteSource.XamlPackage;
        if (hasText)
            return PresentationClipboardPasteSource.Text;
        if (internalHasData)
            return PresentationClipboardPasteSource.Internal;
        return PresentationClipboardPasteSource.Nothing;
    }
}
