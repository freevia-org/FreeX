using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum AccessibleDocumentRegion
{
    Body,
    TableCell
}

public sealed record AccessibleDocumentLocation(
    AccessibleDocumentRegion Region,
    int BlockIndex,
    int Offset,
    int Row = 0,
    int Column = 0,
    int ParagraphIndex = 0)
{
    public static AccessibleDocumentLocation Body(int blockIndex, int offset) =>
        new(AccessibleDocumentRegion.Body, blockIndex, offset);

    public static AccessibleDocumentLocation TableCell(
        int blockIndex,
        int row,
        int column,
        int paragraphIndex,
        int offset) =>
        new(AccessibleDocumentRegion.TableCell, blockIndex, offset, row, column, paragraphIndex);
}

public sealed record AccessibleTextRange(int Start, int Length)
{
    public int End => Start + Length;
}

public enum AccessibleTextUnit
{
    Character,
    Word,
    LogicalLine,
    Paragraph,
    Document
}

public sealed record AccessibleDocumentSnapshot(
    string Text,
    int CaretOffset,
    AccessibleTextRange? Selection,
    AccessibleTextRange Paragraph,
    int ParagraphNumber,
    int ParagraphCount,
    AccessibleTextRange LogicalLine,
    int LogicalLineNumber,
    int LogicalLineCount,
    AccessibleTextRange? Word,
    string Status)
{
    public IReadOnlyList<AccessibleTextRange> ParagraphRanges { get; init; } = [Paragraph];

    public string GetText(AccessibleTextRange range)
    {
        var start = Math.Clamp(range.Start, 0, Text.Length);
        return Text.Substring(start, Math.Clamp(range.Length, 0, Text.Length - start));
    }

    public AccessibleTextRange? RangeAt(AccessibleTextUnit unit, int offset) => unit switch
    {
        AccessibleTextUnit.Character when Text.Length > 0 => new AccessibleTextRange(Math.Clamp(offset, 0, Text.Length - 1), 1),
        AccessibleTextUnit.Word => AccessibleDocumentSnapshotPlanner.FindWordRange(Text, offset),
        AccessibleTextUnit.LogicalLine => RangeContaining(AccessibleDocumentSnapshotPlanner.BuildLineRanges(Text), offset),
        AccessibleTextUnit.Paragraph => RangeContaining(ParagraphRanges, offset),
        AccessibleTextUnit.Document => new AccessibleTextRange(0, Text.Length),
        _ => null
    };

    public AccessibleTextRange? AdjacentRange(AccessibleTextRange range, AccessibleTextUnit unit, int direction)
    {
        if (direction == 0)
            return RangeAt(unit, range.Start);
        if (unit == AccessibleTextUnit.Word)
            return AccessibleDocumentSnapshotPlanner.FindAdjacentWordRange(Text, range, direction);
        if (unit == AccessibleTextUnit.Document)
            return null;
        if (unit == AccessibleTextUnit.Character)
        {
            var characterTarget = direction < 0 ? range.Start - 1 : range.End;
            return characterTarget >= 0 && characterTarget < Text.Length
                ? new AccessibleTextRange(characterTarget, 1)
                : null;
        }

        var ranges = unit == AccessibleTextUnit.LogicalLine
            ? AccessibleDocumentSnapshotPlanner.BuildLineRanges(Text)
            : ParagraphRanges;
        if (ranges.Count == 0)
            return null;
        var current = AccessibleDocumentSnapshotPlanner.FindContainingRange(ranges, range.Start);
        var target = current + Math.Sign(direction);
        return target >= 0 && target < ranges.Count ? ranges[target] : null;
    }

    private static AccessibleTextRange RangeContaining(IReadOnlyList<AccessibleTextRange> ranges, int offset) =>
        ranges[AccessibleDocumentSnapshotPlanner.FindContainingRange(ranges, offset)];
}

/// <summary>
/// Builds the renderer-neutral text/range view used by document automation peers. The flattened
/// text deliberately matches <see cref="TextDocument.PlainText"/>: body blocks and table rows are
/// newline-separated, table cells are tab-separated, and paragraphs inside a cell are newline-separated.
/// </summary>
public static class AccessibleDocumentSnapshotPlanner
{
    public static AccessibleDocumentSnapshot Build(
        TextDocument document,
        AccessibleDocumentLocation caret,
        AccessibleDocumentLocation? selectionAnchor = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var flattened = Flatten(document);
        var caretOffset = ResolveOffset(flattened, caret);
        AccessibleTextRange? selection = null;
        if (selectionAnchor is not null)
        {
            var anchorOffset = ResolveOffset(flattened, selectionAnchor);
            if (anchorOffset != caretOffset)
                selection = new AccessibleTextRange(Math.Min(anchorOffset, caretOffset), Math.Abs(anchorOffset - caretOffset));
        }

        var paragraphIndex = FindContainingRange(flattened.Paragraphs, caretOffset);
        var paragraph = flattened.Paragraphs[paragraphIndex];
        var lines = BuildLineRanges(flattened.Text);
        var lineIndex = FindContainingRange(lines, caretOffset);
        var line = lines[lineIndex];
        var word = FindWordRange(flattened.Text, caretOffset);
        var status = BuildStatus(flattened.Text, caretOffset, selection, paragraphIndex, flattened.Paragraphs.Count, lineIndex, lines.Count, word);

        return new AccessibleDocumentSnapshot(
            flattened.Text,
            caretOffset,
            selection,
            paragraph,
            paragraphIndex + 1,
            flattened.Paragraphs.Count,
            line,
            lineIndex + 1,
            lines.Count,
            word,
            status)
        {
            ParagraphRanges = flattened.Paragraphs
        };
    }

    private static FlattenedDocument Flatten(TextDocument document)
    {
        var text = new StringBuilder();
        var paragraphs = new List<AccessibleTextRange>();
        var locations = new Dictionary<LocationKey, AccessibleTextRange>();

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (blockIndex > 0)
                text.Append('\n');

            switch (document.Blocks[blockIndex])
            {
                case Paragraph paragraph:
                    AppendParagraph(text, paragraphs, locations, new LocationKey(AccessibleDocumentRegion.Body, blockIndex, 0, 0, 0), paragraph.PlainText);
                    break;

                case Table table:
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        if (rowIndex > 0)
                            text.Append('\n');
                        var row = table.Rows[rowIndex];
                        var gridColumn = 0;
                        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        {
                            if (cellIndex > 0)
                                text.Append('\t');
                            var cell = row.Cells[cellIndex];
                            for (var paragraphIndex = 0; paragraphIndex < cell.Paragraphs.Count; paragraphIndex++)
                            {
                                if (paragraphIndex > 0)
                                    text.Append('\n');
                                AppendParagraph(
                                    text,
                                    paragraphs,
                                    locations,
                                    new LocationKey(AccessibleDocumentRegion.TableCell, blockIndex, rowIndex, gridColumn, paragraphIndex),
                                    cell.Paragraphs[paragraphIndex].PlainText);
                            }
                            gridColumn += Math.Max(1, cell.GridSpan);
                        }
                    }
                    break;
            }
        }

        if (paragraphs.Count == 0)
            paragraphs.Add(new AccessibleTextRange(0, 0));

        return new FlattenedDocument(text.ToString(), paragraphs, locations);
    }

    private static void AppendParagraph(
        StringBuilder text,
        List<AccessibleTextRange> paragraphs,
        Dictionary<LocationKey, AccessibleTextRange> locations,
        LocationKey key,
        string value)
    {
        var range = new AccessibleTextRange(text.Length, value.Length);
        paragraphs.Add(range);
        locations[key] = range;
        text.Append(value);
    }

    private static int ResolveOffset(FlattenedDocument document, AccessibleDocumentLocation location)
    {
        var key = new LocationKey(
            location.Region,
            location.BlockIndex,
            location.Row,
            location.Column,
            location.ParagraphIndex);
        if (document.Locations.TryGetValue(key, out var range))
            return range.Start + Math.Clamp(location.Offset, 0, range.Length);

        var sameBlock = document.Locations
            .Where(pair => pair.Key.BlockIndex == location.BlockIndex)
            .Select(pair => pair.Value)
            .OrderBy(range => range.Start)
            .FirstOrDefault();
        return sameBlock is null ? Math.Clamp(location.Offset, 0, document.Text.Length) : sameBlock.Start;
    }

    internal static List<AccessibleTextRange> BuildLineRanges(string text)
    {
        var result = new List<AccessibleTextRange>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            result.Add(new AccessibleTextRange(start, i - start));
            start = i + 1;
        }
        result.Add(new AccessibleTextRange(start, text.Length - start));
        return result;
    }

    internal static int FindContainingRange(IReadOnlyList<AccessibleTextRange> ranges, int offset)
    {
        offset = Math.Max(0, offset);
        for (var i = 0; i < ranges.Count; i++)
        {
            if (offset <= ranges[i].End || i == ranges.Count - 1)
                return i;
        }
        return ranges.Count - 1;
    }

    internal static AccessibleTextRange? FindWordRange(string text, int caretOffset)
    {
        if (text.Length == 0)
            return null;
        var index = Math.Clamp(caretOffset, 0, text.Length - 1);
        if (!IsWordCharacter(text[index]) && index > 0 && IsWordCharacter(text[index - 1]))
            index--;
        if (!IsWordCharacter(text[index]))
            return null;

        var start = index;
        while (start > 0 && IsWordCharacter(text[start - 1]))
            start--;
        var end = index + 1;
        while (end < text.Length && IsWordCharacter(text[end]))
            end++;
        return new AccessibleTextRange(start, end - start);
    }

    internal static AccessibleTextRange? FindAdjacentWordRange(string text, AccessibleTextRange range, int direction)
    {
        var index = direction < 0 ? range.Start - 1 : range.End;
        while (index >= 0 && index < text.Length && !IsWordCharacter(text[index]))
            index += Math.Sign(direction);
        return index >= 0 && index < text.Length ? FindWordRange(text, index) : null;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '\'' or '\u2019';

    private static string BuildStatus(
        string text,
        int caretOffset,
        AccessibleTextRange? selection,
        int paragraphIndex,
        int paragraphCount,
        int lineIndex,
        int lineCount,
        AccessibleTextRange? word)
    {
        var status = new StringBuilder()
            .Append("Caret ").Append(caretOffset).Append(" of ").Append(text.Length)
            .Append("; paragraph ").Append(paragraphIndex + 1).Append(" of ").Append(paragraphCount)
            .Append("; logical line ").Append(lineIndex + 1).Append(" of ").Append(lineCount);
        if (word is not null)
            status.Append("; word: ").Append(text.AsSpan(word.Start, word.Length));
        if (selection is not null)
        {
            const int previewLimit = 120;
            var previewLength = Math.Min(selection.Length, previewLimit);
            status.Append("; selected ").Append(selection.Length).Append(" characters: ")
                .Append(text.AsSpan(selection.Start, previewLength));
            if (previewLength < selection.Length)
                status.Append('…');
        }
        return status.ToString();
    }

    private sealed record FlattenedDocument(
        string Text,
        IReadOnlyList<AccessibleTextRange> Paragraphs,
        IReadOnlyDictionary<LocationKey, AccessibleTextRange> Locations);

    private readonly record struct LocationKey(
        AccessibleDocumentRegion Region,
        int BlockIndex,
        int Row,
        int Column,
        int ParagraphIndex);
}
