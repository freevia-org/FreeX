using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class RowColumnMutationSnapshotOwnershipTests
{
    private static readonly string[] RetiredCommandOwnedFields =
    [
        "_commentSnapshot",
        "_commentAuthorsSnapshot",
        "_shownCommentsSnapshot",
        "_threadedCommentSnapshot",
        "_hyperlinkSnapshot",
        "_hyperlinkMetadataSnapshot",
        "_otherSheetHyperlinkBookmarkSnapshot",
        "_rangeHyperlinkSnapshot",
        "_richTextRunsSnapshot",
        "_phoneticGuideSnapshot",
        "_mergeSnapshot",
        "_dataValidationSnapshot",
        "_conditionalFormatSnapshot",
        "_dvRuleSnapshot",
        "_cfRuleSnapshot",
        "_namedRangeSnapshot",
        "_scopedNamedRangeSnapshot",
        "_chartVerbatimSnapshot",
        "_formulaSnapshot",
        "_namedFormulaSnapshot",
        "_scopedNamedFormulaSnapshot",
        "_cfFormulaSnapshot",
        "_cfThresholdSnapshot",
        "_dvFormulaSnapshot",
        "_chartSnapshot",
        "_chartSeriesColumnMappingsSnapshot",
        "_chartSeriesFormattingSnapshot",
        "_chartPositionSnapshot"
    ];

    [Fact]
    public void StructuralCommands_UseOneCanonicalCommonSnapshotOwner()
    {
        var rowSources = string.Join(
            Environment.NewLine,
            ModelSourceTestSupport.ReadCommandsSource("InsertDeleteRowsCommand.cs"),
            ModelSourceTestSupport.ReadCommandsSource("DeleteRowsCommand.cs"));
        var columnSource = ModelSourceTestSupport.ReadCommandsSource("InsertDeleteColumnsCommand.cs");
        var cellSource = ModelSourceTestSupport.ReadCommandsSource("InsertDeleteCellsCommand.cs");
        var allCommandSources = string.Join(Environment.NewLine, rowSources, columnSource, cellSource);

        Count(allCommandSources, "private RowColumnMutationSnapshot? _mutationSnapshot;")
            .Should().Be(6, "insert/delete rows, columns, and cells each retain the canonical snapshot");
        Count(allCommandSources, "RowColumnMutationSnapshot.Capture(")
            .Should().Be(6, "every structural mutation family must capture through the canonical owner");
        Count(allCommandSources, "RestoreChartStructuralState(ctx.Workbook)")
            .Should().Be(6, "every structural mutation family must restore chart state through the canonical owner");

        var privateFieldLines = allCommandSources
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("private ", StringComparison.Ordinal))
            .ToList();
        foreach (var retiredField in RetiredCommandOwnedFields)
            privateFieldLines.Should().NotContain(line => line.Contains(retiredField, StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalSnapshot_OwnsCommonCaptureRewriteRestoreAndAffectedCellAssembly()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("RowColumnMutationSnapshot.cs");

        source.Should().Contain("internal sealed class RowColumnMutationSnapshot");
        source.Should().Contain("CaptureDictionary(sheet.Comments)");
        source.Should().Contain("CaptureRuleRanges(sheet)");
        source.Should().Contain("CaptureNamedRanges(workbook)");
        source.Should().Contain("CaptureChartStructuralState(workbook, sheet, chartFeatures)");
        source.Should().Contain("internal void RewriteReferences");
        source.Should().Contain("internal List<CellAddress> RestoreRewrittenFormulas");
        source.Should().Contain("internal void RestoreCommonState");
        source.Should().Contain("internal void RestoreChartStructuralState");
        source.Should().Contain("internal IReadOnlyList<CellAddress> BuildAffectedCells");
    }

    [Fact]
    public void CanonicalChartSnapshot_CapturesAndRestoresStructuralStateInSingleTraversals()
    {
        var helperSource = ModelSourceTestSupport.ReadCommandsSource("RowColumnShiftHelpers.PrintAndCharts.cs");
        var capture = Slice(
            helperSource,
            "internal static ChartStructuralWorkbookSnapshot CaptureChartStructuralState(",
            "internal static void RestoreChartStructuralVerbatimFormulas(");
        var restore = Slice(
            helperSource,
            "internal static void RestoreChartStructuralState(",
            "internal static void RewriteChartVerbatimFormulas(");

        Count(capture, "foreach (var sheet in workbook.Sheets)").Should().Be(1);
        Count(capture, "foreach (var chart in sheet.Charts)").Should().Be(1);
        capture.Should().Contain("DataRange = chart.DataRange");
        capture.Should().Contain("new List<ChartSeriesColumnMapping>(chart.SeriesColumnMappings)");
        capture.Should().Contain("CaptureChartSeriesFormatting(chart)");
        capture.Should().Contain("CaptureChartVerbatimFormulas(chart)");

        Count(restore, "foreach (var entry in snapshot.Sheets)").Should().Be(1);
        restore.Should().Contain("chart.DataRange = chartSnapshot.DataRange");
        restore.Should().Contain("chart.SeriesColumnMappings = mappings");
        restore.Should().Contain("RestoreChartSeriesFormatting(chart, chartSnapshot.SeriesFormatting)");

        var structuralCommands = string.Join(
            Environment.NewLine,
            ModelSourceTestSupport.ReadCommandsSource("InsertDeleteRowsCommand.cs"),
            ModelSourceTestSupport.ReadCommandsSource("DeleteRowsCommand.cs"),
            ModelSourceTestSupport.ReadCommandsSource("InsertDeleteColumnsCommand.cs"),
            ModelSourceTestSupport.ReadCommandsSource("InsertDeleteCellsCommand.cs"));
        structuralCommands.Should().NotContain("CaptureChartDataRanges(ctx.Workbook)");
        structuralCommands.Should().NotContain("CaptureChartSeriesColumnMappings(ctx.Workbook)");
        structuralCommands.Should().NotContain("CaptureChartSeriesFormatting(ctx.Workbook)");
        structuralCommands.Should().NotContain("RestoreChartDataRanges(ctx.Workbook");
        structuralCommands.Should().NotContain("RestoreChartSeriesColumnMappings(ctx.Workbook");
        structuralCommands.Should().NotContain("RestoreChartSeriesFormatting(ctx.Workbook");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
