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
        "_dvFormulaSnapshot"
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
        Count(allCommandSources, "RowColumnMutationSnapshot.Capture(ctx.Workbook, sheet)")
            .Should().Be(6, "every structural mutation family must capture through the canonical owner");

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
        source.Should().Contain("CaptureChartVerbatimFormulas(workbook)");
        source.Should().Contain("internal void RewriteReferences");
        source.Should().Contain("internal List<CellAddress> RestoreRewrittenFormulas");
        source.Should().Contain("internal void RestoreCommonState");
        source.Should().Contain("internal IReadOnlyList<CellAddress> BuildAffectedCells");
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
}
