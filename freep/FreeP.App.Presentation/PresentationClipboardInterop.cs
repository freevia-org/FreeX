using System.Text;
using System.Linq;
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

    /// <summary>
    /// True when the plain text is shaped like a grid rather than merely containing tabs.
    /// <para>
    /// r168: "contains a tab" is not the same question. Round 167 first treated every tab-containing
    /// paste as a table, which turned tab-indented code into cells; the correction required an image
    /// flavour alongside, which then dropped a FreeX range copy of more than 2000 cells, because
    /// FreeX omits the picture above that size and sends text alone. Both attempts asked about the
    /// payload's packaging. The reliable question is about the text: a copied range has several lines
    /// with the same number of fields, and its first column is not uniformly empty -- indentation is
    /// exactly what makes it uniformly empty.
    /// </para>
    /// </summary>
    public bool HasTabularText
    {
        get
        {
            if (!HasText || !Text!.Contains('\t'))
                return false;

            // r169: split on row boundaries only, not on every newline. FreeX quotes a cell whose
            // text wraps (Alt+Enter) and leaves the newline INSIDE the quotes, so a naive split
            // tore one row into pieces with mismatched field counts and the shape check rejected a
            // genuine range copy -- pasting it as the flat tab-riddled box this whole check exists
            // to prevent. Third distinct way this one branch has been wrong; the quoting rule was
            // in FreeX's serializer the entire time.
            var lines = SplitTabularRows(Text!);
            if (lines.Length < 2)
                return false;

            // r172 follow-up: count fields quote-aware too, not just rows. FreeX quotes a cell
            // whose text contains a literal tab (RequiresTsvQuoting treats tab like quote/CR/LF),
            // so a raw '\t' split saw an extra column in that one row, the counts disagreed, and
            // the whole range was rejected as non-tabular -- while ClipboardTablePlanner.SplitCells
            // would have reconstructed the cell correctly had it ever been handed the body. Same
            // splitter for both now.
            var rows = lines.Select(ClipboardTsvFields.SplitFields).ToArray();
            var columns = rows[0].Count;
            if (columns < 2 || rows.Any(row => row.Count != columns))
                return false;

            return rows.Any(row => row[0].Length > 0);
        }
    }

    /// <summary>
    /// Splits TSV text into rows, treating a newline inside a quoted cell as cell content rather
    /// than a row boundary -- which is how FreeX writes a wrapped-text cell.
    /// </summary>
    internal static string[] SplitTabularRows(string text)
    {
        var rows = new List<string>();
        var row = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                row.Append(c);
                continue;
            }

            if (!inQuotes && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                if (row.Length > 0)
                    rows.Add(row.ToString());
                row.Clear();
                continue;
            }

            row.Append(c);
        }

        if (row.Length > 0)
            rows.Add(row.ToString());

        return rows.ToArray();
    }

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
        bool hasXamlPackage = false,
        bool hasTabularText = false)
    {
        if (ownCopyIsCurrent && internalHasData)
            return PresentationClipboardPasteSource.Internal;
        if (hasNativeSelection)
            return PresentationClipboardPasteSource.NativeSelection;
        if (hasRichText)
            return PresentationClipboardPasteSource.RichText;
        if (hasXamlPackage)
            return PresentationClipboardPasteSource.XamlPackage;
        // freep-tables F1: tab-delimited standalone text (FreeX's cell-range Ctrl+C payload) is
        // structured content, same as RichText/XamlPackage -- it must win over an accompanying
        // flat image instead of collapsing the paste into an inert picture of the cells. Plain,
        // non-tabular text still loses to an image, unchanged from prior behavior.
        if (hasTabularText)
            return PresentationClipboardPasteSource.Text;
        if (hasImage)
            return PresentationClipboardPasteSource.Image;
        if (hasText)
            return PresentationClipboardPasteSource.Text;
        if (internalHasData)
            return PresentationClipboardPasteSource.Internal;
        return PresentationClipboardPasteSource.Nothing;
    }
}
