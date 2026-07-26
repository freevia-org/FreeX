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
        var existingBookmarkNames = BookmarkNamesIn(target);
        var allParagraphs = TransferAnnotations(target, source, clones);
        TransferStyles(target, source, clones, allParagraphs);
        RemapBookmarksAndInternalReferences(allParagraphs, existingBookmarkNames);
        var roots = allParagraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.PreservedDrawing)
            .Where(drawing => drawing is not null)
            .SelectMany(drawing => drawing!.References)
            .Select(reference => reference.PreservedPartName)
            .Concat(clones.OfType<AltChunkBlock>().Select(altChunk => altChunk.PreservedPartName));
        var partNames = TransferPreservedPartGraph(target, source, roots);
        RewritePreservedDrawingReferences(allParagraphs, partNames);
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
            TableStyleId = source.TableStyleId,
            Borders = source.Borders,
            PreferredWidthPt = source.PreferredWidthPt,
            Alignment = source.Alignment,
            IndentFromLeftPt = source.IndentFromLeftPt,
            TextWrapping = source.TextWrapping,
            DefaultCellMargins = source.DefaultCellMargins,
            CellSpacingPt = source.CellSpacingPt,
            AutoFit = source.AutoFit,
        };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow
            {
                HeightPt = row.HeightPt,
                HeightRule = row.HeightRule,
                AllowBreakAcrossPages = row.AllowBreakAcrossPages,
            };
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
            VerticalMerge = source.VerticalMerge,
            VerticalAlignment = source.VerticalAlignment,
            Margins = source.Margins,
            Borders = source.Borders,
            TextDirection = source.TextDirection,
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
        IEnumerable<Paragraph> paragraphs,
        IReadOnlyDictionary<string, string> partNames)
    {
        foreach (var paragraph in paragraphs)
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

    private static void TransferStyles(
        TextDocument target,
        TextDocument source,
        IReadOnlyList<Block> clones,
        IReadOnlyList<Paragraph> paragraphs)
    {
        var sourceStyles = new Dictionary<string, DocumentStyle>(source.Styles, StringComparer.OrdinalIgnoreCase);
        var styleIds = new List<string>();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddStyle(string? id)
        {
            if (string.IsNullOrEmpty(id) || !sourceStyles.TryGetValue(id, out var style) || !selected.Add(id))
                return;
            styleIds.Add(id);
            AddStyle(style.BasedOnStyleId);
            AddStyle(style.NextStyleId);
        }

        foreach (var paragraph in paragraphs)
            AddStyle(paragraph.StyleId);
        foreach (var table in clones.OfType<Table>())
            AddStyle(table.TableStyleId);
        if (styleIds.Count == 0)
            return;

        var usedIds = new HashSet<string>(target.Styles.Keys, StringComparer.OrdinalIgnoreCase);
        var styleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceId in styleIds)
            styleNames[sourceId] = usedIds.Add(sourceId)
                ? sourceId
                : AllocateStyleId(sourceId, usedIds);

        foreach (var sourceId in styleIds)
        {
            var sourceStyle = sourceStyles[sourceId];
            target.Styles[styleNames[sourceId]] = new DocumentStyle
            {
                Id = styleNames[sourceId],
                Name = sourceStyle.Name,
                Type = sourceStyle.Type,
                BasedOnStyleId = RemapStyleReference(sourceStyle.BasedOnStyleId, styleNames),
                NextStyleId = RemapStyleReference(sourceStyle.NextStyleId, styleNames),
                Run = sourceStyle.Run,
                Paragraph = sourceStyle.Paragraph,
                TableBorders = sourceStyle.TableBorders,
                PreservedNumbering = sourceStyle.PreservedNumbering,
            };
        }

        foreach (var paragraph in paragraphs)
            if (paragraph.StyleId is { } styleId && styleNames.TryGetValue(styleId, out var mappedStyleId))
                paragraph.StyleId = mappedStyleId;
        foreach (var table in clones.OfType<Table>())
            if (table.TableStyleId is { } styleId && styleNames.TryGetValue(styleId, out var mappedStyleId))
                table.TableStyleId = mappedStyleId;
    }

    private static string? RemapStyleReference(string? styleId, IReadOnlyDictionary<string, string> styleNames) =>
        styleId is not null && styleNames.TryGetValue(styleId, out var mappedStyleId)
            ? mappedStyleId
            : styleId;

    private static string AllocateStyleId(string sourceId, HashSet<string> usedIds)
    {
        for (var index = 1; ; index++)
        {
            var candidate = sourceId + "_FreeW" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (usedIds.Add(candidate))
                return candidate;
        }
    }

    private static HashSet<string> BookmarkNamesIn(TextDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in EnumerateParagraphs(document.Blocks).Concat(EnumerateAnnotationParagraphs(document)))
            foreach (var name in paragraph.BookmarkNames)
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
        return names;
    }

    private static void RemapBookmarksAndInternalReferences(
        IReadOnlyList<Paragraph> paragraphs,
        HashSet<string> usedBookmarkNames)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            for (var index = 0; index < paragraph.BookmarkNames.Count; index++)
            {
                var name = paragraph.BookmarkNames[index];
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!names.TryGetValue(name, out var mapped))
                {
                    mapped = usedBookmarkNames.Add(name)
                        ? name
                        : AllocateBookmarkName(name, usedBookmarkNames);
                    names[name] = mapped;
                }
                paragraph.BookmarkNames[index] = mapped;
            }
        }

        if (names.Count == 0)
            return;

        foreach (var paragraph in paragraphs)
            foreach (var run in paragraph.Runs)
            {
                if (run.HyperlinkAnchor is { } anchor && names.TryGetValue(anchor, out var mappedAnchor))
                    run.HyperlinkAnchor = mappedAnchor;
                if (run.CrossReference is { Kind: not CrossRefFieldKind.NoteRef } crossReference
                    && names.TryGetValue(crossReference.Target, out var mappedTarget))
                    run.CrossReference = crossReference with { Target = mappedTarget };
            }
    }

    private static string AllocateBookmarkName(string sourceName, HashSet<string> usedNames)
    {
        for (var index = 1; ; index++)
        {
            var suffix = "_FreeW" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prefixLength = Math.Max(1, 40 - suffix.Length);
            var candidate = sourceName.Length <= prefixLength
                ? sourceName + suffix
                : sourceName[..prefixLength] + suffix;
            if (usedNames.Add(candidate))
                return candidate;
        }
    }

    private static IReadOnlyList<Paragraph> TransferAnnotations(
        TextDocument target,
        TextDocument source,
        IReadOnlyList<Block> clones)
    {
        var paragraphs = EnumerateParagraphs(clones).ToList();
        var footnoteIds = new Dictionary<int, int>();
        var endnoteIds = new Dictionary<int, int>();
        var commentIds = new Dictionary<int, int>();
        var copiedCommentRoots = new HashSet<int>();
        var usedFootnotes = target.Footnotes.Keys.ToHashSet();
        var usedEndnotes = target.Endnotes.Keys.ToHashSet();
        var usedComments = target.Comments.Values.SelectMany(comment => comment.ThreadInOrder()).Select(comment => comment.Id).ToHashSet();

        for (var cursor = 0; cursor < paragraphs.Count; cursor++)
        {
            foreach (var run in paragraphs[cursor].Runs)
            {
                if (run.FootnoteId is { } footnoteId
                    && !footnoteIds.ContainsKey(footnoteId)
                    && source.Footnotes.TryGetValue(footnoteId, out var footnote))
                {
                    var mappedId = AllocateId(footnoteId, usedFootnotes, firstId: 1);
                    footnoteIds[footnoteId] = mappedId;
                    var clone = new Footnote(mappedId);
                    foreach (var content in footnote.Content)
                    {
                        var paragraph = CloneParagraph(content);
                        clone.Content.Add(paragraph);
                        paragraphs.Add(paragraph);
                    }
                    target.Footnotes[mappedId] = clone;
                }

                if (run.EndnoteId is { } endnoteId
                    && !endnoteIds.ContainsKey(endnoteId)
                    && source.Endnotes.TryGetValue(endnoteId, out var endnote))
                {
                    var mappedId = AllocateId(endnoteId, usedEndnotes, firstId: 1);
                    endnoteIds[endnoteId] = mappedId;
                    var clone = new Endnote(mappedId);
                    foreach (var content in endnote.Content)
                    {
                        var paragraph = CloneParagraph(content);
                        clone.Content.Add(paragraph);
                        paragraphs.Add(paragraph);
                    }
                    target.Endnotes[mappedId] = clone;
                }

                if (run.CommentId is not { } commentId
                    || !TryFindTopLevelComment(source, commentId, out var topComment)
                    || !copiedCommentRoots.Add(topComment.Id))
                    continue;

                foreach (var node in topComment.ThreadInOrder())
                    commentIds[node.Id] = AllocateId(node.Id, usedComments, firstId: 0);
                var copied = CloneComment(topComment, id => commentIds[id], paragraphs);
                target.Comments[copied.Id] = copied;
            }
        }

        foreach (var paragraph in paragraphs)
            foreach (var run in paragraph.Runs)
            {
                if (run.FootnoteId is { } footnoteId && footnoteIds.TryGetValue(footnoteId, out var mappedFootnote))
                    run.FootnoteId = mappedFootnote;
                if (run.EndnoteId is { } endnoteId && endnoteIds.TryGetValue(endnoteId, out var mappedEndnote))
                    run.EndnoteId = mappedEndnote;
                if (run.CommentId is { } commentId && commentIds.TryGetValue(commentId, out var mappedComment))
                    run.CommentId = mappedComment;
            }

        return paragraphs;
    }

    private static int AllocateId(int sourceId, HashSet<int> usedIds, int firstId)
    {
        if (sourceId >= firstId && usedIds.Add(sourceId))
            return sourceId;
        var candidate = Math.Max(firstId, usedIds.Count == 0 ? firstId : usedIds.Max() + 1);
        while (!usedIds.Add(candidate))
            candidate++;
        return candidate;
    }

    private static bool TryFindTopLevelComment(TextDocument source, int id, out Comment comment)
    {
        if (source.Comments.TryGetValue(id, out var direct))
        {
            comment = direct;
            return true;
        }

        var topLevel = source.Comments.Values.FirstOrDefault(candidate => candidate.ThreadInOrder().Any(node => node.Id == id));
        if (topLevel is not null)
        {
            comment = topLevel;
            return true;
        }

        comment = null!;
        return false;
    }

    private static Comment CloneComment(Comment source, Func<int, int> mapId, List<Paragraph> allParagraphs)
    {
        var clone = new Comment(mapId(source.Id))
        {
            Author = source.Author,
            Initials = source.Initials,
            DateXml = source.DateXml,
            Resolved = source.Resolved,
        };
        foreach (var content in source.Content)
        {
            var paragraph = CloneParagraph(content);
            clone.Content.Add(paragraph);
            allParagraphs.Add(paragraph);
        }
        foreach (var reply in source.Replies)
            clone.Replies.Add(CloneComment(reply, mapId, allParagraphs));
        return clone;
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

    private static IEnumerable<Paragraph> EnumerateAnnotationParagraphs(TextDocument document)
    {
        foreach (var footnote in document.Footnotes.Values)
            foreach (var paragraph in footnote.Content)
                yield return paragraph;
        foreach (var endnote in document.Endnotes.Values)
            foreach (var paragraph in endnote.Content)
                yield return paragraph;
        foreach (var comment in document.Comments.Values.SelectMany(comment => comment.ThreadInOrder()))
            foreach (var paragraph in comment.Content)
                yield return paragraph;
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
