using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// Pure helpers for merging the body blocks of one document into another — the model side of
/// "Insert Text from File", which opens a second .docx and drops its content at the caret. The clone
/// is deep enough that the inserted blocks are fully independent of the source: paragraphs get fresh
/// <see cref="Run"/> copies (carrying their formatting and run marks), tables get fresh rows/cells, so
/// editing the merged target never mutates the source and vice versa. The small immutable formatting
/// records (<see cref="RunFormatting"/>, <see cref="ParagraphFormatting"/>, <see cref="TableFormatting"/>,
/// <see cref="ContentControl"/>, <see cref="InlineImage"/> byte arrays) are shared by reference, which is
/// safe precisely because they are immutable/never reassigned through the cloned graph.
/// </summary>
public static class DocumentMerge
{
    private static readonly IReadOnlyDictionary<string, string> EmptyPartNameMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deep-clone the body blocks of <paramref name="source"/> so they can be inserted into another
    /// document without aliasing the source. Paragraphs and tables are copied; the source is left
    /// untouched. Returns a fresh list of fresh block instances, in document order.
    /// </summary>
    public static IReadOnlyList<Block> CloneBlocks(TextDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clones = new List<Block>(source.Blocks.Count);
        foreach (var block in source.Blocks)
            clones.Add(CloneBlock(block));
        return clones;
    }

    /// <summary>
    /// Clones source body blocks for insertion into <paramref name="target"/>, carrying every preserved package
    /// part reachable from a verbatim drawing or unresolved altChunk. Part-name collisions are resolved without
    /// overwriting the target package, and copied relationship parts are rewritten to their renamed descendants.
    /// </summary>
    public static IReadOnlyList<Block> CloneBlocksForInsertion(TextDocument target, TextDocument source)
    {
        ArgumentNullException.ThrowIfNull(target);
        var clones = CloneBlocks(source);
        var roots = EnumerateParagraphs(clones)
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.PreservedDrawing)
            .Where(drawing => drawing is not null)
            .SelectMany(drawing => drawing!.References)
            .Select(reference => reference.PreservedPartName)
            .Concat(clones.OfType<AltChunkBlock>().Select(altChunk => altChunk.PreservedPartName));
        var partNames = TransferPreservedPartGraph(target, source, roots);
        RewritePreservedDrawingReferences(clones, partNames);
        RewriteAltChunkPartNames(clones, partNames);
        return clones;
    }

    /// <summary>
    /// Insert <paramref name="blocks"/> into <paramref name="target"/>'s body starting at
    /// <paramref name="index"/> (clamped to the body), preserving their order. The blocks are inserted
    /// as-is (callers that need independence from another document pass the result of
    /// <see cref="CloneBlocks"/>).
    /// </summary>
    public static void InsertBlocksAt(TextDocument target, int index, IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(blocks);
        var at = Math.Clamp(index, 0, target.Blocks.Count);
        foreach (var block in blocks)
            target.Blocks.Insert(at++, block);
    }

    /// <summary>
    /// Deep-clone the body blocks of <paramref name="source"/> and insert them into
    /// <paramref name="target"/> at <paramref name="index"/> (clamped). The source is left untouched and
    /// the target receives independent copies. Returns the cloned blocks that were inserted.
    /// </summary>
    public static IReadOnlyList<Block> Merge(TextDocument target, int index, TextDocument source)
    {
        ArgumentNullException.ThrowIfNull(target);
        var clones = CloneBlocksForInsertion(target, source);
        InsertBlocksAt(target, index, clones);
        return clones;
    }

    /// <summary>Deep-clone a single body block (paragraph or table). Unknown block kinds are passed through.</summary>
    public static Block CloneBlock(Block block) => block switch
    {
        Paragraph p => CloneParagraph(p),
        Table t => CloneTable(t),
        AltChunkBlock altChunk => new AltChunkBlock(altChunk.PreservedPartName)
        {
            BlockContentControl = altChunk.BlockContentControl
        },
        _ => block
    };

    private static Paragraph CloneParagraph(Paragraph source)
    {
        var clone = new Paragraph
        {
            BlockContentControl = source.BlockContentControl,
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            DropCap = source.DropCap,
        };
        clone.BookmarkNames.AddRange(source.BookmarkNames);
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run));
        return clone;
    }

    private static Run CloneRun(Run source) => new(source.Text, source.Formatting)
    {
        Image = source.Image?.Clone(),
        Equation = source.Equation?.Clone(),
        WordArt = source.WordArt?.Clone(),
        SmartArt = source.SmartArt is { } smartArt ? SmartArtCommandCopy.Clone(smartArt) : null,
        Chart = source.Chart?.Clone(),
        EmbeddedObject = source.EmbeddedObject?.Clone(),
        Ruby = source.Ruby?.Clone(),
        PreservedDrawing = source.PreservedDrawing?.Duplicate(),
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        FieldKind = source.FieldKind,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        Revision = source.Revision,
        Control = source.Control, // immutable record — safe to share
        Citation = source.Citation, // immutable — safe to share
        CrossReference = source.CrossReference, // immutable record — safe to share
        ComplexField = source.ComplexField, // immutable record — safe to share
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml
    };

    private static Table CloneTable(Table source)
    {
        var clone = new Table
        {
            BlockContentControl = source.BlockContentControl,
            Formatting = source.Formatting,
            Borders = source.Borders
        };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow();
            foreach (var cell in row.Cells)
                rowClone.Cells.Add(CloneCell(cell));
            clone.Rows.Add(rowClone);
        }
        return clone;
    }

    private static TableCell CloneCell(TableCell source)
    {
        var clone = new TableCell
        {
            ShadingColorHex = source.ShadingColorHex,
            WidthPt = source.WidthPt,
            GridSpan = source.GridSpan,
            VerticalMerge = source.VerticalMerge
        };
        foreach (var paragraph in source.Paragraphs)
            clone.Paragraphs.Add(CloneParagraph(paragraph));
        return clone;
    }

    private static IReadOnlyDictionary<string, string> TransferPreservedPartGraph(
        TextDocument target,
        TextDocument source,
        IEnumerable<string> rootPartNames)
    {
        var sourceParts = source.Preserved.Parts
            .ToDictionary(part => part.PartName, StringComparer.OrdinalIgnoreCase);
        if (sourceParts.Count == 0)
            return EmptyPartNameMap;

        var roots = rootPartNames
            .Where(sourceParts.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roots.Count == 0)
            return EmptyPartNameMap;

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            var partName = queue.Dequeue();
            if (!selected.Add(partName) || !sourceParts.TryGetValue(partName, out var part))
                continue;

            var relsName = OpcPathHelper.GetRelationshipPartName(part.PartName);
            if (!sourceParts.TryGetValue(relsName, out var relsPart))
                continue;

            selected.Add(relsName);
            foreach (var targetPartName in ReadInternalRelationshipTargets(part.PartName, relsPart.Bytes))
                if (!selected.Contains(targetPartName))
                    queue.Enqueue(targetPartName);
        }

        var sourceOwnersByRelsPart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceName in selected)
        {
            var relsName = OpcPathHelper.GetRelationshipPartName(sourceName);
            if (selected.Contains(relsName) && sourceParts.ContainsKey(relsName))
                sourceOwnersByRelsPart.TryAdd(relsName, sourceName);
        }

        var targetParts = target.Preserved.Parts
            .ToDictionary(part => part.PartName, StringComparer.OrdinalIgnoreCase);
        var reservedNames = new HashSet<string>(targetParts.Keys, StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceName in selected
                     .Where(sourceName => !sourceOwnersByRelsPart.ContainsKey(sourceName))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePart = sourceParts[sourceName];
            var sourceRelsName = OpcPathHelper.GetRelationshipPartName(sourceName);
            var hasRels = sourceParts.TryGetValue(sourceRelsName, out var sourceRels)
                && selected.Contains(sourceRelsName);
            var canReuse = targetParts.TryGetValue(sourceName, out var existing)
                && existing.RelationshipType == sourcePart.RelationshipType
                && existing.PackageRelationshipType == sourcePart.PackageRelationshipType
                && existing.Bytes.AsSpan().SequenceEqual(sourcePart.Bytes)
                && (!hasRels || (targetParts.TryGetValue(sourceRelsName, out var existingRels)
                    && existingRels.Bytes.AsSpan().SequenceEqual(sourceRels!.Bytes)));
            if (canReuse)
            {
                names[sourceName] = existing!.PartName;
                if (hasRels)
                    names[sourceRelsName] = sourceRelsName;
                continue;
            }

            var targetName = AllocatePartName(sourceName, reservedNames, hasRels);
            names[sourceName] = targetName;
            reservedNames.Add(targetName);
            if (hasRels)
            {
                var targetRelsName = OpcPathHelper.GetRelationshipPartName(targetName);
                names[sourceRelsName] = targetRelsName;
                reservedNames.Add(targetRelsName);
            }
        }

        foreach (var sourceName in selected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var targetName = names[sourceName];
            if (targetParts.ContainsKey(targetName))
                continue;

            var sourcePart = sourceParts[sourceName];
            var bytes = sourceOwnersByRelsPart.TryGetValue(sourceName, out var sourceOwner)
                ? RewriteRelationshipTargets(sourcePart.Bytes, sourceOwner, names)
                : (byte[])sourcePart.Bytes.Clone();
            target.Preserved.Parts.Add(sourcePart with { PartName = targetName, Bytes = bytes });
            targetParts[targetName] = target.Preserved.Parts[^1];
        }

        foreach (var (extension, contentType) in source.Preserved.ContentTypeDefaults)
            target.Preserved.ContentTypeDefaults.TryAdd(extension, contentType);

        return names;
    }

    private static void RewritePreservedDrawingReferences(
        IReadOnlyList<Block> clones,
        IReadOnlyDictionary<string, string> partNames)
    {
        foreach (var paragraph in EnumerateParagraphs(clones))
            foreach (var run in paragraph.Runs)
                if (run.PreservedDrawing is { } drawing)
                    run.PreservedDrawing = new PreservedDrawing(
                        drawing.Xml,
                        drawing.References
                            .Select(reference => partNames.TryGetValue(reference.PreservedPartName, out var partName)
                                ? reference with { PreservedPartName = partName }
                                : reference)
                            .ToArray());
    }

    private static void RewriteAltChunkPartNames(
        IReadOnlyList<Block> clones,
        IReadOnlyDictionary<string, string> partNames)
    {
        if (clones is not IList<Block> mutableBlocks)
            return;

        for (var index = 0; index < mutableBlocks.Count; index++)
        {
            if (mutableBlocks[index] is not AltChunkBlock altChunk
                || !partNames.TryGetValue(altChunk.PreservedPartName, out var partName))
                continue;
            mutableBlocks[index] = new AltChunkBlock(partName)
            {
                BlockContentControl = altChunk.BlockContentControl
            };
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not Table table)
                continue;
            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;
        }
    }

    private static IEnumerable<string> ReadInternalRelationshipTargets(string ownerPartName, byte[] relsBytes)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(relsBytes), LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            yield break;
        }

        var baseDirectory = OpcPathHelper.GetPartDirectoryName(ownerPartName);
        foreach (var relationship in OpcRelationships.Load(document))
        {
            if (relationship.IsExternal || string.IsNullOrWhiteSpace(relationship.Target))
                continue;
            var target = OpcPathHelper.ResolveAbsolutePartName(baseDirectory, relationship.Target);
            if (target is not null)
                yield return target;
        }
    }

    private static byte[] RewriteRelationshipTargets(
        byte[] relsBytes,
        string sourceOwnerPartName,
        IReadOnlyDictionary<string, string> names)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(relsBytes), LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return (byte[])relsBytes.Clone();
        }

        var baseDirectory = OpcPathHelper.GetPartDirectoryName(sourceOwnerPartName);
        foreach (var relationship in document.Root?.Elements(OpcRelationships.Namespace + "Relationship") ?? [])
        {
            if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = relationship.Attribute("Target");
            if (target is null)
                continue;
            var sourceTarget = OpcPathHelper.ResolveAbsolutePartName(baseDirectory, target.Value);
            if (sourceTarget is null || !names.TryGetValue(sourceTarget, out var destinationTarget))
                continue;
            var relativeTarget = OpcPathHelper.GetRelativeZipPath(baseDirectory, destinationTarget);
            target.Value = OpcPathHelper.EscapeRelationshipPathSegments(relativeTarget);
        }

        using var output = new MemoryStream();
        document.Save(output, SaveOptions.DisableFormatting);
        return output.ToArray();
    }

    private static string AllocatePartName(
        string sourceName,
        IReadOnlySet<string> reservedNames,
        bool requiresRelationshipPart)
    {
        if (!reservedNames.Contains(sourceName)
            && (!requiresRelationshipPart || !reservedNames.Contains(OpcPathHelper.GetRelationshipPartName(sourceName))))
            return sourceName;

        var slash = sourceName.LastIndexOf('/');
        var prefix = slash < 0 ? string.Empty : sourceName[..(slash + 1)];
        var fileName = slash < 0 ? sourceName : sourceName[(slash + 1)..];
        var extension = Path.GetExtension(fileName);
        var stem = extension.Length == 0 ? fileName : fileName[..^extension.Length];
        for (var number = 1; ; number++)
        {
            var candidate = $"{prefix}{stem}-freew-import{number}{extension}";
            if (!reservedNames.Contains(candidate)
                && (!requiresRelationshipPart || !reservedNames.Contains(OpcPathHelper.GetRelationshipPartName(candidate))))
                return candidate;
        }
    }
}
