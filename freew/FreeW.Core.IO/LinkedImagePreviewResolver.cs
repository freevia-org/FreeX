using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>Loads local link-only picture previews without changing their package serialization.</summary>
public static class LinkedImagePreviewResolver
{
    public const long MaxPreviewBytes = 64L * 1024L * 1024L;

    /// <summary>
    /// Resolves local file and relative filesystem targets against <paramref name="documentPath"/>.
    /// Network targets and failures remain unresolved. Returns the number of previews loaded.
    /// </summary>
    public static int ResolveLocalPreviews(TextDocument document, string documentPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        if (string.IsNullOrEmpty(baseDirectory))
            return 0;

        var resolved = 0;
        foreach (var image in EnumerateImages(document).Distinct<InlineImage>(ReferenceEqualityComparer.Instance))
        {
            if (image.Bytes.Length > 0
                || image.ResolvedLinkedImageBytes is { Length: > 0 }
                || image.LinkedImageTarget is not { Length: > 0 } target
                || ResolveLocalPath(target, baseDirectory) is not { } path
                || !TryReadPreview(path, out var bytes))
            {
                continue;
            }

            image.ResolvedLinkedImageBytes = bytes;
            resolved++;
        }

        return resolved;
    }

    private static string? ResolveLocalPath(string target, string baseDirectory)
    {
        try
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile || !string.IsNullOrEmpty(uri.Host) && !uri.IsLoopback)
                    return null;
                return Path.GetFullPath(uri.LocalPath);
            }

            var unescaped = Uri.UnescapeDataString(target).Replace('/', Path.DirectorySeparatorChar);
            if (unescaped.StartsWith("\\\\", StringComparison.Ordinal))
                return null;
            return Path.GetFullPath(unescaped, baseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return null;
        }
    }

    private static bool TryReadPreview(string path, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > MaxPreviewBytes)
                return false;
            bytes = File.ReadAllBytes(file.FullName);

            // r160-remediation: SECURITY. The path comes from a .docx's external linked-picture
            // relationship target, which is attacker-controlled content -- a document can point it
            // at any local file the user can read. Without this check those bytes were read and
            // stored verbatim in ResolvedLinkedImageBytes, which DisplayBytes hands straight to the
            // renderers and which the user may then save or forward: the same local-file disclosure
            // the HTML reader was hardened against, through a second door nobody had gated.
            //
            // InlineImage.HasRecognisedSignature is the one signature list, shared with
            // DetectFormat. Do NOT substitute DetectFormat here: it answers Png for unrecognised
            // data by design, so it can say what a file is but never whether it is an image.
            if (!InlineImage.HasRecognisedSignature(bytes))
            {
                bytes = [];
                return false;
            }

            return bytes.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static IEnumerable<InlineImage> EnumerateImages(TextDocument document)
    {
        foreach (var paragraph in EnumerateStoryParagraphs(document))
            foreach (var image in EnumerateParagraphImages(paragraph))
                yield return image;
    }

    private static IEnumerable<Paragraph> EnumerateStoryParagraphs(TextDocument document)
    {
        var seen = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var paragraph in document.Blocks.SelectMany(EnumerateParagraphs))
            if (seen.Add(paragraph))
                yield return paragraph;

        foreach (var section in document.Sections)
        {
            foreach (var content in new[]
                     {
                         section.HeadersFooters.Header, section.HeadersFooters.Footer,
                         section.HeadersFooters.EvenHeader, section.HeadersFooters.EvenFooter,
                         section.HeadersFooters.FirstHeader, section.HeadersFooters.FirstFooter
                     })
            {
                if (content is null)
                    continue;
                foreach (var paragraph in content.Paragraphs)
                    if (seen.Add(paragraph))
                        yield return paragraph;
            }
        }

        foreach (var paragraph in document.Footnotes.Values.SelectMany(note => note.Content)
                     .Concat(document.Endnotes.Values.SelectMany(note => note.Content))
                     .Concat(document.Comments.Values.SelectMany(comment => comment.ThreadInOrder()).SelectMany(comment => comment.Content)))
        {
            if (seen.Add(paragraph))
                yield return paragraph;
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;
    }

    private static IEnumerable<InlineImage> EnumerateParagraphImages(Paragraph paragraph)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.Image is { } image)
                yield return image;
            if (run.EmbeddedObject?.Icon is { } icon)
                yield return icon;
            if (run.Shape is { } shape)
                foreach (var nested in shape.TextParagraphs.SelectMany(EnumerateParagraphImages))
                    yield return nested;
            if (run.DrawingGroup is { } group)
                foreach (var nested in EnumerateGroupImages(group))
                    yield return nested;
        }
    }

    private static IEnumerable<InlineImage> EnumerateGroupImages(DrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            if (child is InlineImage image)
                yield return image;
            else if (child is Shape shape)
                foreach (var nested in shape.TextParagraphs.SelectMany(EnumerateParagraphImages))
                    yield return nested;
            else if (child is DrawingGroup nestedGroup)
                foreach (var nested in EnumerateGroupImages(nestedGroup))
                    yield return nested;
        }
    }
}
