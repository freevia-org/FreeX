using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DocumentAccessibilityNodeKind
{
    Paragraph,
    Heading,
    Table,
    TableRow,
    TableCell,
    Hyperlink,
    Image
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
    int ColumnSpan = 1,
    int RowSpan = 1,
    bool IsHeader = false,
    IReadOnlyList<DocumentAccessibilityNode>? Children = null)
{
    public IReadOnlyList<DocumentAccessibilityNode> SemanticChildren { get; init; } = Children ?? [];
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

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            switch (document.Blocks[blockIndex])
            {
                case Paragraph paragraph:
                    children.Add(BuildParagraph(paragraph, blockIndex, -1, -1, -1,
                        $"block:{blockIndex}:paragraph", $"Paragraph {blockIndex + 1}"));
                    break;

                case Table table:
                    tableNumber++;
                    children.Add(BuildTable(table, blockIndex, tableNumber, $"block:{blockIndex}:table"));
                    break;
            }
        }

        return new DocumentAccessibilityTree(children);
    }

    private static DocumentAccessibilityNode BuildTable(Table table, int blockIndex, int tableNumber, string id)
    {
        var columnCount = table.Rows.Count == 0
            ? 0
            : table.Rows.Max(row => row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)));
        var rows = new List<DocumentAccessibilityNode>(table.Rows.Count);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var cells = new List<DocumentAccessibilityNode>(row.Cells.Count);
            var gridColumn = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = Math.Max(1, cell.GridSpan);
                if (cell.VerticalMerge == VerticalMergeState.Continue)
                {
                    gridColumn += span;
                    continue;
                }

                var cellId = $"{id}:row:{rowIndex}:column:{gridColumn}";
                var content = new List<DocumentAccessibilityNode>(cell.NestedTables.Count + cell.Paragraphs.Count);
                for (var nestedIndex = 0; nestedIndex < cell.NestedTables.Count; nestedIndex++)
                {
                    content.Add(BuildTable(
                        cell.NestedTables[nestedIndex],
                        blockIndex,
                        nestedIndex + 1,
                        $"{cellId}:nested:{nestedIndex}"));
                }
                for (var paragraphIndex = 0; paragraphIndex < cell.Paragraphs.Count; paragraphIndex++)
                {
                    content.Add(BuildParagraph(
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
                gridColumn += span;
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
        Paragraph paragraph,
        int blockIndex,
        int rowIndex,
        int columnIndex,
        int paragraphIndex,
        string id,
        string label)
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
                    if (candidate.Image is { IsFloating: false } linkedImage)
                        linkChildren.Add(BuildImage(id, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, linkedImage));
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

            if (run.Image is { IsFloating: false } image)
                children.Add(BuildImage(id, blockIndex, rowIndex, columnIndex, paragraphIndex, runIndex, image));

            textOffset += run.Text.Length;
            runIndex++;
        }

        var isHeading = DocumentOutline.TryGetLevel(paragraph.StyleId, out var headingLevel);

        return new DocumentAccessibilityNode(
            id,
            isHeading ? DocumentAccessibilityNodeKind.Heading : DocumentAccessibilityNodeKind.Paragraph,
            NameWithPreview(label, paragraph.PlainText),
            paragraph.PlainText,
            null,
            blockIndex,
            rowIndex,
            columnIndex,
            paragraphIndex,
            HeadingLevel: isHeading ? headingLevel : -1,
            Children: children);
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
            runIndex);

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

    private static TableCell? CellAtGridColumn(TableRow row, int targetColumn)
    {
        var gridColumn = 0;
        foreach (var cell in row.Cells)
        {
            var span = Math.Max(1, cell.GridSpan);
            if (targetColumn >= gridColumn && targetColumn < gridColumn + span)
                return cell;
            gridColumn += span;
        }
        return null;
    }

    private static string NameWithPreview(string label, string text)
    {
        var preview = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (preview.Length > 80)
            preview = preview[..77] + "...";
        return preview.Length == 0 ? label : $"{label}: {preview}";
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
