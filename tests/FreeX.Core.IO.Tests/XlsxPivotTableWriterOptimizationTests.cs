using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPivotTableWriterOptimizationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlyDictionary<string, int> EmptyCalculatedFieldIndexes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly MethodInfo ToPivotFieldsXmlMethod = typeof(XlsxPivotTableWriter).GetMethod(
        "ToPivotFieldsXml",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void PivotFields_OverlappingAxesUseLastMetadataButRowAxisAndLastSort()
    {
        var pivot = new PivotTableModel();
        pivot.RowFields.Add(new PivotFieldModel(
            0,
            ShowSubtotals: true,
            SubtotalPlacement: PivotSubtotalPlacement.Top,
            ReportLayout: PivotReportLayout.Outline));
        pivot.ColumnFields.Add(new PivotFieldModel(
            0,
            ShowSubtotals: true,
            SubtotalPlacement: PivotSubtotalPlacement.Top,
            ReportLayout: PivotReportLayout.Tabular));
        pivot.PageFields.Add(new PivotFieldModel(
            0,
            ShowSubtotals: false,
            SubtotalPlacement: PivotSubtotalPlacement.Bottom,
            ReportLayout: PivotReportLayout.Outline));
        pivot.PageFields.Add(new PivotFieldModel(
            0,
            ShowSubtotals: false,
            SubtotalPlacement: PivotSubtotalPlacement.Bottom,
            ReportLayout: PivotReportLayout.Compact));
        pivot.PageFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(-4, ReportLayout: PivotReportLayout.Tabular));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 0));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 1));

        var pivotFields = WritePivotFields(pivot).Elements(WorkbookNs + "pivotField").ToList();

        pivotFields.Should().HaveCount(2, "negative source field indexes are not emitted");
        var first = pivotFields[0];
        first.Attribute("axis")!.Value.Should().Be("axisRow", "row axis wins over overlapping column/page entries");
        first.Attribute("compact")!.Value.Should().Be("1", "the final page-field metadata entry wins");
        first.Attribute("outline")!.Value.Should().Be("1");
        first.Attribute("defaultSubtotal")!.Value.Should().Be("0");
        first.Attribute("subtotalTop")!.Value.Should().Be("0");
        first.Attribute("sortType")!.Value.Should().Be("descending", "the last sort for the field wins");

        pivotFields[1].Attribute("axis")!.Value.Should().Be("axisPage");
        pivotFields[1].Attribute("sortType").Should().BeNull("sorts only serialize on row/column axis fields");
    }

    [Fact]
    public void PivotFields_WideRowFieldsHaveBoundedWarmAllocation()
    {
        const int fieldCount = 3_000;
        const long allocationLimit = 5_000_000;
        var pivot = new PivotTableModel();
        for (var index = 0; index < fieldCount; index++)
            pivot.RowFields.Add(new PivotFieldModel(index));

        _ = WritePivotFields(pivot);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var pivotFields = WritePivotFields(pivot);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        pivotFields.Elements(WorkbookNs + "pivotField").Should().HaveCount(fieldCount);
        allocatedBytes.Should().BeLessThan(
            allocationLimit,
            "wide pivot-field emission should index metadata instead of rescanning every field for every XML element");
    }

    [Fact]
    public void PivotFields_IndexesMetadataAxesAndSortsBeforeEmission()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("XlsxPivotTableWriter.cs");

        source.Should().Contain("var metadataByIndex = new Dictionary<int, PivotFieldModel>();");
        source.Should().Contain("var axisByIndex = new Dictionary<int, string>();");
        source.Should().Contain("var sortByFieldIndex = new Dictionary<int, PivotSortModel>();");
        source.Should().Contain("metadataByIndex.TryGetValue(index, out var metadataField);");
        source.Should().NotContain("FindPivotField(");
        source.Should().NotContain("pivot.RowFields.Any(field => field.SourceFieldIndex == index)");
        source.Should().NotContain("pivot.Sorts.LastOrDefault(s => s.FieldIndex == index)");
    }

    private static XElement WritePivotFields(PivotTableModel pivot) =>
        (XElement)ToPivotFieldsXmlMethod.Invoke(null, [pivot, null, EmptyCalculatedFieldIndexes, WorkbookNs])!;
}
