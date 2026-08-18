using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DocumentAccessibilityNodeKind
{
    HeaderFooterStory,
    Footnotes,
    Endnotes,
    Footnote,
    Endnote,
    List,
    ListItem,
    TextRun,
    Paragraph,
    Heading,
    Table,
    TableRow,
    TableCell,
    Hyperlink,
    Image,
    Shape,
    Chart,
    WordArt,
    SmartArt,
    DrawingGroup,
    EmbeddedObject
}

public enum DocumentAccessibilityStoryKind
{
    Body,
    Header,
    Footer,
    EvenHeader,
    EvenFooter,
    FirstHeader,
    FirstFooter
}

/// <summary>
/// Toolkit-neutral semantic node consumed by accessibility renderers. Model coordinates are retained
/// solely so a renderer can attach bounds and interaction without rediscovering document semantics.
/// </summary>
public sealed record DocumentAccessibilityNode(
    string Id,
    DocumentAccessibilityNodeKind Kind,
    string Name,
    string? Value,
    string? HelpText,
    int BlockIndex,
    int RowIndex = -1,
    int ColumnIndex = -1,
    int ParagraphIndex = -1,
    int RunIndex = -1,
    int TextStart = -1,
    int TextLength = 0,
    string? HyperlinkTarget = null,
    bool IsInternalHyperlink = false,
    int HeadingLevel = -1,
    ListKind ListKind = ListKind.None,
    int ListLevel = -1,
    string? ListMarker = null,
    int ColumnSpan = 1,
    int RowSpan = 1,
    bool IsHeader = false,
    bool IsFloatingObject = false,
    IReadOnlyList<int>? ObjectPath = null,
    int SectionIndex = -1,
    DocumentAccessibilityStoryKind StoryKind = DocumentAccessibilityStoryKind.Body,
    IReadOnlyList<DocumentAccessibilityNode>? Children = null)
{
    public IReadOnlyList<DocumentAccessibilityNode> SemanticChildren => Children ?? [];
}

public sealed record DocumentAccessibilityTree(IReadOnlyList<DocumentAccessibilityNode> Children)
{
    public IReadOnlyDictionary<string, DocumentAccessibilityNode> ById { get; } =
        Flatten(Children).ToDictionary(node => node.Id, StringComparer.Ordinal);

    private static IEnumerable<DocumentAccessibilityNode> Flatten(IEnumerable<DocumentAccessibilityNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in Flatten(node.SemanticChildren))
                yield return descendant;
        }
    }
}

/// <summary>
/// Projects the shared document model into stable semantic accessibility nodes. It intentionally owns
/// no toolkit roles, geometry, focus, or platform event behavior; those remain thin renderer concerns.
/// </summary>
public static class DocumentAccessibilityNodePlanner
{
    public static DocumentAccessibilityTree Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var children = new List<DocumentAccessibilityNode>(document.Blocks.Count);
        var tableNumber = 0;
        var listMarkerSequence = new DocumentListMarkerSequencePlanner(
            document.MultiLevelList.NumberFormats);
        var preservedNumberingMarkers = PreservedNumberingMarkerPlanner.Build(document);

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            switch (document.Blocks[blockIndex])
            {
                case Paragraph { Formatting.ListKind: not ListKind.None }:
                {
                    var startBlockIndex = blockIndex;
                    var kind = ((Paragraph)document.Blocks[blockIndex]).Formatting.ListKind;
                    var items = new List<(Paragraph Paragraph, int BlockIndex, DocumentListMarkerPlan Marker)>();
                    while (blockIndex < document.Blocks.Count
                        && document.Blocks[blockIndex] is Paragraph { Formatting.ListKind: var itemKind } paragraph
                        && itemKind == kind
                        && (items.Count == 0
                            || kind != ListKind.Number
                            || !paragraph.Formatting.ListStartOverride.HasValue))
                    {
                        items.Add((paragraph, blockIndex, listMarkerSequence.Advance(paragraph)));
                        blockIndex++;
                    }
                    blockIndex--;

                    if (kind == ListKind.MultiLevel
                        && items.Count == 1
                        && string.Equals(items[0].Paragraph.StyleId, "Heading1", StringComparison.OrdinalIgnoreCase))
                    {
                        var item = items[0];
                        children.Add(BuildParagraph(
                            document,
                            item.Paragraph,
                            item.BlockIndex,
                            -1,
                            -1,
                            -1,
                            $"block:{item.BlockIndex}:paragraph",
                            $"Paragraph {item.BlockIndex + 1}",
                            item.Marker.MarkerText));
                        break;
                    }

                    var listItems = items.Select((item, itemIndex) =>
                    {
                        var paragraphNode = BuildParagraph(
                            document,
                            item.Paragraph,
                            item.BlockIndex,
                            -1,
                            -1,
                            -1,
                            $"block:{item.BlockIndex}:paragraph",
                            $"Paragraph {item.BlockIndex + 1}");
                        var accessibleValue = PrefixMarker(item.Marker.MarkerText, item.Paragraph.PlainText);
                        return new DocumentAccessibilityNode(
                            $"block:{item.BlockIndex}:list-item",
                            DocumentAccessibilityNodeKind.ListItem,
                            NameWithPreview($"List item {itemIndex + 1}", accessibleValue),
                            accessibleValue,
                            item.Marker.MarkerText is null ? null : $"List marker {item.Marker.MarkerText}",
                            item.BlockIndex,
                            ListKind: kind,
                            ListLevel: item.Marker.Level,
                            ListMarker: item.Marker.MarkerText,
                            Children: [paragraphNode]);
                    }).ToArray();
                    children.Add(new DocumentAccessibilityNode(
                        $"block:{startBlockIndex}:list:{kind.ToString().ToLowerInvariant()}",
                        DocumentAccessibilityNodeKind.List,
                        $"{ListKindName(kind)} list",
                        null,
                        $"{listItems.Length} list item{(listItems.Length == 1 ? string.Empty : "s")}",
                        startBlockIndex,
                        ListKind: kind,
                        Children: listItems));
                    break;
                }

                case Paragraph paragraph:
                    children.Add(BuildParagraph(
                        document,
                        paragraph,
                        blockIndex,
                        -1,
                        -1,
                        -1,
                        $"block:{blockIndex}:paragraph",
                        $"Paragraph {blockIndex + 1}",
                        preservedNumberingMarkers.TryGetValue(blockIndex, out var preservedMarker)
                            ? preservedMarker.Text
                            : null));
                    break;

                case Table table:
                    tableNumber++;
                    children.Add(BuildTable(document, table, blockIndex, tableNumber, $"block:{blockIndex}:table"));
                    break;
            }
        }

        var sections = document.Sections;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            AddHeaderFooterStories(document, children, sections[sectionIndex].HeadersFooters, sectionIndex);

        AddNoteStories(children, document);

        return new DocumentAccessibilityTree(children);
    }

    private static void AddNoteStories(
        ICollection<DocumentAccessibilityNode> children,
        TextDocument document)
    {
        AddStory(
            document.Footnotes,
            document.FootnoteNumbering,
            DocumentAccessibilityNodeKind.Footnotes,
            DocumentAccessibilityNodeKind.Footnote,
            "footnotes",
            "Footnotes");
        AddStory(
            document.Endnotes,
            document.EndnoteNumbering,
            DocumentAccessibilityNodeKind.Endnotes,
            DocumentAccessibilityNodeKind.Endnote,
            "endnotes",
            "Endnotes");

        void AddStory<TNote>(
            IReadOnlyDictionary<int, TNote> notes,
            NoteNumberingOptions numbering,
            DocumentAccessibilityNodeKind storyKind,
            DocumentAccessibilityNodeKind noteKind,
            string idPart,
            string label)
        {
            if (notes.Count == 0)
                return;

            // AV-NUMRESTART/WPF-NUMRESTART shared: honors NoteNumberRestart via the same authoritative
            // sequence calculator the note-region renderers use, instead of a hardcoded "StartAt + index"
            // continuous series that ignored a restart setting and drifted from what the document shows.
            var isFootnote = noteKind == DocumentAccessibilityNodeKind.Footnote;
            var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote);
            var noteNodes = notes
                .OrderBy(entry => entry.Key)
                .Select(entry =>
                {
                    var text = entry.Value switch
                    {
                        Footnote footnote => DocumentNoteRegionPlanner.ResolveVisiblePlainText(document, footnote.Content),
                        Endnote endnote => DocumentNoteRegionPlanner.ResolveVisiblePlainText(document, endnote.Content),
                        _ => string.Empty
                    };
                    var sequence = sequenceById.TryGetValue(entry.Key, out var resolvedSequence)
                        ? resolvedSequence
                        : Math.Max(1, numbering.StartAt);
                    var displayNumber = DocumentNoteRegionPlanner.ComputeDisplayNumber(sequence, numbering);
                    var value = PrefixMarker(displayNumber, text);
                    var singular = noteKind == DocumentAccessibilityNodeKind.Footnote
                        ? "Footnote"
                        : "Endnote";
                    return new DocumentAccessibilityNode(
                        $"{idPart}:{entry.Key}",
                        noteKind,
                        NameWithPreview($"{singular} {displayNumber}", text),
                        value,
                        $"{singular} {displayNumber}",
                        -1);
                })
                .ToArray();

            children.Add(new DocumentAccessibilityNode(
                $"story:{idPart}",
                storyKind,
                label,
                null,
                $"{noteNodes.Length} {label.ToLowerInvariant()}",
                -1,
                Children: noteNodes));
        }
    }

    private static void AddHeaderFooterStories(
        TextDocument document,
        ICollection<DocumentAccessibilityNode> children,
        SectionHeadersFooters stories,
        int sectionIndex)
    {
        AddStory(stories.Header, DocumentAccessibilityStoryKind.Header, "default-header", "Default header");
        AddStory(stories.Footer, DocumentAccessibilityStoryKind.Footer, "default-footer", "Default footer");
        AddStory(stories.EvenHeader, DocumentAccessibilityStoryKind.EvenHeader, "even-header", "Even-page header");
        AddStory(stories.EvenFooter, DocumentAccessibilityStoryKind.EvenFooter, "even-footer", "Even-page footer");
        AddStory(stories.FirstHeader, DocumentAccessibilityStoryKind.FirstHeader, "first-header", "First-page header");
        AddStory(stories.FirstFooter, DocumentAccessibilityStoryKind.FirstFooter, "first-footer", "First-page footer");

        void AddStory(
            HeaderFooter? story,
            DocumentAccessibilityStoryKind storyKind,
            string idPart,
            string label)
        {
            if (story is null || story.IsEmpty)
                return;

            var id = $"section:{sectionIndex}:story:{idPart}";
            IReadOnlyList<DocumentAccessibilityNode> storyChildren;
            if (story.Table is { } table)
            {
                storyChildren =
                [StampStory(BuildTable(document, table, -1, 1, $"{id}:table"), sectionIndex, storyKind)];
            }
            else
            {
                storyChildren = story.Paragraphs
                    .Select((paragraph, paragraphIndex) => StampStory(
                        BuildParagraph(
                            document,
                            paragraph,
                            -1,
                            -1,
                            -1,
                            paragraphIndex,
                            $"{id}:paragraph:{paragraphIndex}",
                            $"Paragraph {paragraphIndex + 1}"),
                        sectionIndex,
                        storyKind))
                    .ToArray();
            }

            children.Add(new DocumentAccessibilityNode(
                id,
                DocumentAccessibilityNodeKind.HeaderFooterStory,
                $"Section {sectionIndex + 1} {label}",
                story.PlainText,
                $"{label} for section {sectionIndex + 1}",
                -1,
                SectionIndex: sectionIndex,
                StoryKind: storyKind,
                Children: storyChildren));
        }
    }

    private static DocumentAccessibilityNode StampStory(
        DocumentAccessibilityNode node,
        int sectionIndex,
        DocumentAccessibilityStoryKind storyKind) =>
        node with
        {
            SectionIndex = sectionIndex,
            StoryKind = storyKind,
            Children = node.SemanticChildren
                .Select(child => StampStory(child, sectionIndex, storyKind))
                .ToArray()
        };

    private static DocumentAccessibilityNode BuildTable(
        TextDocument document,
        Table table,
        int blockIndex,
        int tableNumber,
        string id)
    {
        var columnCount = TableGridProjection.TableWidth(table);
        var rows = new List<DocumentAccessibilityNode>(table.Rows.Count);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var cells = new List<DocumentAccessibilityNode>(row.Cells.Count);
            foreach (var projected in TableGridProjection.ProjectRow(row))
            {
                var cell = projected.Cell;
                var gridColumn = projected.StartColumn;
                var span = projected.Span;
                if (cell.VerticalMerge == VerticalMergeState.Continue)
                    continue;

                var cellId = $"{id}:row:{rowIndex}:column:{gridColumn}";
                var content = new List<DocumentAccessibilityNode>(cell.NestedTables.Count + cell.Paragraphs.Count);
                for (var nestedIndex = 0; nestedIndex < cell.NestedTables.Count; nestedIndex++)
                {
                    content.Add(BuildTable(
                        document,
                        cell.NestedTables[nestedIndex],
                        blockIndex,
                        nestedIndex + 1,
                        $"{cellId}:nested:{nestedIndex}"));
                }
                for (var paragraphIndex = 0; paragraphIndex < cell.Paragraphs.Count; paragraphIndex++)
                {
                    content.Add(BuildParagraph(
                        document,
                        cell.Paragraphs[paragraphIndex],
                        blockIndex,
                        rowIndex,
                        gridColumn,
                        paragraphIndex,
                        $"{cellId}:paragraph:{paragraphIndex}",
                        $"Paragraph {paragraphIndex + 1}"));
                }

                var rowSpan = cell.VerticalMerge == VerticalMergeState.Restart
                    ? CountVerticalSpan(table, rowIndex, gridColumn)
                    : 1;

                cells.Add(new DocumentAccessibilityNode(
                    cellId,
                    DocumentAccessibilityNodeKind.TableCell,
                    NameWithPreview($"Row {rowIndex + 1}, column {gridColumn + 1}", cell.PlainText),
                    cell.PlainText,
                    $"Table cell spanning {span} column{(span == 1 ? string.Empty : "s")} and {rowSpan} row{(rowSpan == 1 ? string.Empty : "s")}",
                    blockIndex,
                    rowIndex,
                    gridColumn,
                    ColumnSpan: span,
                    RowSpan: rowSpan,
                    IsHeader: table.Formatting.HeaderRow && rowIndex == 0,
                    Children: content));
            }

            rows.Add(new DocumentAccessibilityNode(
                $"{id}:row:{rowIndex}",
                DocumentAccessibilityNodeKind.TableRow,
                $"Row {rowIndex + 1}",
                null,
                null,
                blockIndex,
                rowIndex,
                Children: cells));
        }

        return new DocumentAccessibilityNode(
            id,
            DocumentAccessibilityNodeKind.Table,
            $"Table {tableNumber}, {table.Rows.Count} rows, {columnCount} columns",
            null,
            null,
            blockIndex,
            Children: rows);
    }

    private static DocumentAccessibilityNode BuildParagraph(
        TextDocument document,
        Paragraph paragraph,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        string id,
        string label,
        string? accessibleMarker = null)
    {
        var children = new List<DocumentAccessibilityNode>();
        var textOffset = 0;
        for (var runIndex = 0; runIndex < paragraph.Runs.Count;)
        {
            var run = paragraph.Runs[runIndex];
            var runId = $"{id}:run:{runIndex}";
            var target = run.HyperlinkUrl ?? run.HyperlinkAnchor;
            if (!string.IsNullOrWhiteSpace(target))
            {
                var groupStart = runIndex;
                var groupTextStart = textOffset;
                var groupText = new System.Text.StringBuilder();
                var linkChildren = new List<DocumentAccessibilityNode>();
                var tooltip = run.HyperlinkTooltip;
                var url = run.HyperlinkUrl;
                var anchor = run.HyperlinkAnchor;
                while (runIndex < paragraph.Runs.Count)
                {
                    var candidate = paragraph.Runs[runIndex];
                    if (!string.Equals(candidate.HyperlinkUrl, url, StringComparison.Ordinal)
                        || !string.Equals(candidate.HyperlinkAnchor, anchor, StringComparison.Ordinal)
                        || !string.Equals(candidate.HyperlinkTooltip, tooltip, StringComparison.Ordinal))
                        break;
                    groupText.Append(candidate.Text);
                    if (HasAccessibleText(candidate))
                    {
                        linkChildren.Add(BuildTextRun(
                            document,
                            paragraph,
                            id,
                            blockIndex,
                            rowIndex,
                            columnIndex,
                            paragraphIndex,
                            runIndex,
                            textOffset,
                            candidate));
                    }
                    linkChildren.AddRange(BuildRunObjects(
                        id, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, candidate));
                    textOffset += candidate.Text.Length;
                    runIndex++;
                }

                var visibleText = groupText.ToString();
                var imageName = linkChildren.Select(child => child.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                var name = FirstNonBlank(visibleText, imageName, tooltip, target) ?? "Hyperlink";
                children.Add(new DocumentAccessibilityNode(
                    $"{id}:run:{groupStart}:hyperlink",
                    DocumentAccessibilityNodeKind.Hyperlink,
                    name,
                    visibleText,
                    FirstNonBlank(tooltip, target),
                    blockIndex,
                    rowIndex,
                    columnIndex,
                    paragraphIndex,
                    groupStart,
                    groupTextStart,
                    visibleText.Length,
                    target,
                    url is null,
                    Children: linkChildren));
                continue;
            }

            if (HasAccessibleText(run))
            {
                children.Add(BuildTextRun(
                    document,
                    paragraph,
                    id,
                    blockIndex,
                    rowIndex,
                    columnIndex,
                    paragraphIndex,
                    runIndex,
                    textOffset,
                    run));
            }
            children.AddRange(BuildRunObjects(
                id, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, run));

            textOffset += run.Text.Length;
            runIndex++;
        }

        var isHeading = DocumentOutline.TryGetLevel(paragraph.StyleId, out var headingLevel);
        var accessibleValue = PrefixMarker(accessibleMarker, paragraph.PlainText);

        return new DocumentAccessibilityNode(
            id,
            isHeading ? DocumentAccessibilityNodeKind.Heading : DocumentAccessibilityNodeKind.Paragraph,
            NameWithPreview(label, accessibleValue),
            accessibleValue,
            accessibleMarker is null ? null : $"List marker {accessibleMarker}",
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            HeadingLevel: isHeading ? headingLevel : -1,
            ListLevel: accessibleMarker is null ? -1 : Math.Max(0, paragraph.Formatting.ListLevel),
            ListMarker: accessibleMarker,
            Children: children);
    }

    private static DocumentAccessibilityNode BuildTextRun(
        TextDocument document,
        Paragraph paragraph,
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        int textStart,
        Run run)
    {
        var effective = DocumentRunFormattingResolver.Resolve(document, paragraph, run);
        return new DocumentAccessibilityNode(
            $"{paragraphId}:run:{runIndex}:text",
            DocumentAccessibilityNodeKind.TextRun,
            NameWithPreview("Text", run.Text),
            run.Text,
            DocumentRunAccessibilityFormatter.Describe(effective, run),
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            textStart,
            run.Text.Length);
    }

    private static bool HasAccessibleText(Run run) =>
        run.Text.Length > 0
        && run.Image is null
        && run.Shape is null
        && run.Chart is null
        && run.WordArt is null
        && run.SmartArt is null
        && run.DrawingGroup is null
        && run.EmbeddedObject is null;

    private static IEnumerable<DocumentAccessibilityNode> BuildRunObjects(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        Run run)
    {
        if (run.Image is { } image)
            yield return BuildImage(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, image);
        if (run.Shape is { } shape)
            yield return BuildShape(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, shape);
        if (run.Chart is { } chart)
            yield return BuildChart(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, chart);
        if (run.WordArt is { } wordArt)
            yield return BuildWordArt(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, wordArt);
        if (run.SmartArt is { } smartArt)
            yield return BuildSmartArt(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, smartArt);
        if (run.DrawingGroup is { } group)
            yield return BuildDrawingGroup(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, group);
        if (run.EmbeddedObject is { } embeddedObject)
            yield return BuildEmbeddedObject(paragraphId, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, embeddedObject);
    }

    private static DocumentAccessibilityNode BuildImage(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        InlineImage image) =>
        new(
            $"{paragraphId}:run:{runIndex}:image",
            DocumentAccessibilityNodeKind.Image,
            string.IsNullOrWhiteSpace(image.AltText) ? "Image" : image.AltText.Trim(),
            null,
            string.IsNullOrWhiteSpace(image.AltText)
                ? $"Image, {image.WidthPt:0.#} by {image.HeightPt:0.#} points"
                : image.AltText.Trim(),
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: image.IsFloating);

    private static DocumentAccessibilityNode BuildShape(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        Shape shape) =>
        new(
            $"{paragraphId}:run:{runIndex}:shape",
            DocumentAccessibilityNodeKind.Shape,
            FirstNonBlank(shape.AltText, shape.PlainText, $"{shape.Kind} shape")!,
            shape.HasText ? shape.PlainText : null,
            FirstNonBlank(shape.AltText, $"{(shape.IsFloating ? "Floating" : "Inline")} {shape.Kind} shape"),
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: shape.IsFloating);

    private static DocumentAccessibilityNode BuildChart(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        Chart chart) =>
        new(
            $"{paragraphId}:run:{runIndex}:chart",
            DocumentAccessibilityNodeKind.Chart,
            FirstNonBlank(chart.Title, $"{chart.Kind} chart")!,
            ChartSummary(chart),
            $"{chart.Kind} chart with {chart.Series.Count} series and {chart.Categories.Count} categories",
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: chart.IsFloating);

    private static DocumentAccessibilityNode BuildWordArt(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        WordArt wordArt) =>
        new(
            $"{paragraphId}:run:{runIndex}:wordart",
            DocumentAccessibilityNodeKind.WordArt,
            FirstNonBlank(wordArt.AltText, wordArt.Text, "WordArt")!,
            wordArt.Text,
            FirstNonBlank(wordArt.AltText, $"WordArt, {wordArt.Style}"),
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: wordArt.IsFloating);

    private static DocumentAccessibilityNode BuildSmartArt(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        SmartArt smartArt)
    {
        var text = string.Join("; ", FlattenSmartArtText(smartArt.Nodes));
        return new DocumentAccessibilityNode(
            $"{paragraphId}:run:{runIndex}:smartart",
            DocumentAccessibilityNodeKind.SmartArt,
            FirstNonBlank(text, $"{smartArt.Kind} SmartArt")!,
            string.IsNullOrWhiteSpace(text) ? null : text,
            $"{smartArt.Kind} SmartArt diagram with {smartArt.Nodes.Count} top-level nodes",
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: smartArt.IsFloating);
    }

    private static DocumentAccessibilityNode BuildDrawingGroup(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        DrawingGroup group)
    {
        var id = $"{paragraphId}:run:{runIndex}:group";
        var children = group.Children
            .Select((child, index) => BuildGroupedObject(
                child,
                $"{id}:child:{index}",
                blockIndex,
                rowIndex,
                columnIndex,
                paragraphIndex,
                runIndex,
                [index]))
            .Where(node => node is not null)
            .Cast<DocumentAccessibilityNode>()
            .ToArray();
        return new DocumentAccessibilityNode(
            id,
            DocumentAccessibilityNodeKind.DrawingGroup,
            $"Drawing group, {children.Length} objects",
            null,
            "Floating drawing-object group",
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: true,
            Children: children);
    }

    private static DocumentAccessibilityNode BuildEmbeddedObject(
        string paragraphId,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        EmbeddedObject embeddedObject)
    {
        var plan = EmbeddedObjectVisualPlanner.Build(embeddedObject);
        return new(
            $"{paragraphId}:run:{runIndex}:embedded-object",
            DocumentAccessibilityNodeKind.EmbeddedObject,
            plan.AccessibleName,
            null,
            plan.HelpText,
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex);
    }

    private static DocumentAccessibilityNode? BuildGroupedObject(
        object child,
        string id,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int> objectPath)
    {
        if (child is DrawingGroup nestedGroup)
        {
            var nestedChildren = nestedGroup.Children
                .Select((nestedChild, index) => BuildGroupedObject(
                    nestedChild,
                    $"{id}:child:{index}",
                    blockIndex,
                    rowIndex,
                    columnIndex,
                    paragraphIndex,
                    runIndex,
                    [.. objectPath, index]))
                .Where(node => node is not null)
                .Cast<DocumentAccessibilityNode>()
                .ToArray();
            return new DocumentAccessibilityNode(
                id,
                DocumentAccessibilityNodeKind.DrawingGroup,
                $"Drawing group, {nestedChildren.Length} objects",
                null,
                "Nested drawing-object group",
                blockIndex,
                rowIndex,
                columnIndex,
                paragraphIndex,
                runIndex,
                IsFloatingObject: true,
                ObjectPath: objectPath,
                Children: nestedChildren);
        }

        var (kind, name, value, helpText) = child switch
        {
            InlineImage image => (
                DocumentAccessibilityNodeKind.Image,
                FirstNonBlank(image.AltText, "Image")!,
                (string?)null,
                FirstNonBlank(image.AltText, $"Image, {image.WidthPt:0.#} by {image.HeightPt:0.#} points")),
            Shape shape => (
                DocumentAccessibilityNodeKind.Shape,
                FirstNonBlank(shape.AltText, shape.PlainText, $"{shape.Kind} shape")!,
                shape.HasText ? shape.PlainText : null,
                FirstNonBlank(shape.AltText, $"{shape.Kind} shape")),
            Chart chart => (
                DocumentAccessibilityNodeKind.Chart,
                FirstNonBlank(chart.Title, $"{chart.Kind} chart")!,
                ChartSummary(chart),
                (string?)$"{chart.Kind} chart with {chart.Series.Count} series and {chart.Categories.Count} categories"),
            WordArt wordArt => (
                DocumentAccessibilityNodeKind.WordArt,
                FirstNonBlank(wordArt.AltText, wordArt.Text, "WordArt")!,
                wordArt.Text,
                FirstNonBlank(wordArt.AltText, $"WordArt, {wordArt.Style}")),
            SmartArt smartArt => (
                DocumentAccessibilityNodeKind.SmartArt,
                FirstNonBlank(string.Join("; ", FlattenSmartArtText(smartArt.Nodes)), $"{smartArt.Kind} SmartArt")!,
                FirstNonBlank(string.Join("; ", FlattenSmartArtText(smartArt.Nodes))),
                (string?)$"{smartArt.Kind} SmartArt diagram with {smartArt.Nodes.Count} top-level nodes"),
            _ => default
        };
        if (kind == default)
            return null;
        return new DocumentAccessibilityNode(
            id,
            kind,
            name,
            value,
            helpText,
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            runIndex,
            IsFloatingObject: true,
            ObjectPath: objectPath);
    }

    private static string? ChartSummary(Chart chart)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(chart.Title))
            parts.Add(chart.Title.Trim());
        if (chart.Categories.Count > 0)
            parts.Add("Categories: " + string.Join(", ", chart.Categories));
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var name = FirstNonBlank(series.Name, $"Series {index + 1}")!;
            parts.Add(name + ": " + string.Join(", ", series.Values.Select(value =>
                value.ToString("G", System.Globalization.CultureInfo.InvariantCulture))));
        }
        if (parts.Count == 0)
            return null;
        var summary = string.Join(". ", parts);
        return summary.Length <= 1000 ? summary : summary[..997] + "...";
    }

    private static IEnumerable<string> FlattenSmartArtText(IEnumerable<SmartArtNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Text))
                yield return node.Text.Trim();
            foreach (var child in FlattenSmartArtText(node.Children))
                yield return child;
        }
    }

    private static int CountVerticalSpan(Table table, int restartRow, int gridColumn)
    {
        var span = 1;
        for (var rowIndex = restartRow + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var cell = CellAtGridColumn(table.Rows[rowIndex], gridColumn);
            if (cell?.VerticalMerge != VerticalMergeState.Continue)
                break;
            span++;
        }
        return span;
    }

    private static TableCell? CellAtGridColumn(TableRow row, int targetColumn) =>
        TableGridProjection.At(row, targetColumn)?.Cell;

    private static string NameWithPreview(string label, string text)
    {
        var preview = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (preview.Length > 80)
            preview = preview[..77] + "...";
        return preview.Length == 0 ? label : $"{label}: {preview}";
    }

    private static string PrefixMarker(string? marker, string text) =>
        string.IsNullOrWhiteSpace(marker)
            ? text
            : string.IsNullOrWhiteSpace(text)
                ? marker.Trim()
                : $"{marker.Trim()} {text}";

    private static string ListKindName(ListKind kind) => kind switch
    {
        ListKind.Bullet => "Bulleted",
        ListKind.Number => "Numbered",
        ListKind.MultiLevel => "Multilevel",
        _ => "Document"
    };

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
