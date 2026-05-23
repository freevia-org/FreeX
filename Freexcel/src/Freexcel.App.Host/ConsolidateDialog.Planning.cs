using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class ConsolidateDialog
{
    public static IReadOnlyList<string> SplitSourceRangeText(string sourceRangesText) =>
        sourceRangesText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    public static string JoinSourceRanges(IEnumerable<string> sourceRanges) =>
        string.Join("; ", sourceRanges.Select(item => item.Trim()).Where(item => item.Length > 0));

    public static bool TryAddReference(
        SheetId sheetId,
        IEnumerable<string> existingReferences,
        string referenceText,
        out IReadOnlyList<string> updatedReferences,
        out string? error)
    {
        var references = existingReferences.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
        updatedReferences = references;
        error = null;

        var reference = referenceText.Trim();
        if (!ConsolidateInputParser.TryParseSourceRanges(reference, sheetId, out var ranges, out var invalidPart) ||
            ranges.Count != 1)
        {
            error = string.IsNullOrWhiteSpace(invalidPart)
                ? "Enter a valid source range."
                : $"Enter a valid source range: {invalidPart}.";
            return false;
        }

        references.Add(reference);
        updatedReferences = references;
        return true;
    }

    public static ConsolidateDialogResult CreateResult(
        IEnumerable<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateFunction function,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count == 0)
            throw new ArgumentException("At least one source range is required.", nameof(sourceRanges));

        return new ConsolidateDialogResult(
            ranges,
            destinationCell,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
    }

    public static bool HaveSameSize(IEnumerable<GridRange> sourceRanges)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count < 2)
            return true;

        var rowCount = ranges[0].RowCount;
        var colCount = ranges[0].ColCount;
        return ranges.All(range => range.RowCount == rowCount && range.ColCount == colCount);
    }

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out string? error) =>
        TryParse(
            sheetId,
            sourceRangesText,
            destinationCellText,
            ConsolidateFunction.Sum,
            useTopRowLabels: false,
            useLeftColumnLabels: false,
            createLinksToSourceData: false,
            out result,
            out error);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        if (!ConsolidateInputParser.TryParseSourceRanges(sourceRangesText, sheetId, out var ranges, out var invalidPart))
        {
            error = string.IsNullOrWhiteSpace(invalidPart)
                ? "Enter at least one valid source range."
                : $"Enter a valid source range: {invalidPart}.";
            return false;
        }

        if (!HaveSameSize(ranges))
        {
            error = "Source ranges must be the same size.";
            return false;
        }

        if (!ConsolidateInputParser.TryParseDestination(destinationCellText, sheetId, out var destination))
        {
            error = "Enter a valid destination cell.";
            return false;
        }

        result = CreateResult(
            ranges,
            destination,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
        return true;
    }

    public static ConsolidateRangeSelectionRequest CreateRangeSelectionRequest(
        ConsolidateRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static string FunctionLabel(ConsolidateFunction function) =>
        function switch
        {
            ConsolidateFunction.CountNumbers => "Count Numbers",
            ConsolidateFunction.StdDev => "StdDev",
            ConsolidateFunction.StdDevp => "StdDevp",
            _ => function.ToString()
        };
}
