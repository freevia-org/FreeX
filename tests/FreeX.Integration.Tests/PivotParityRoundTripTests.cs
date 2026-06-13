using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// End-to-end verification for the pivot Excel-parity hardening work: author a
/// multi-feature pivot (3 nested row fields with multi-level subtotals, grand
/// totals, and a "% of Parent Row Total" data field), refresh it to materialize
/// cells, then round-trip through the XLSX adapter and confirm the pivot
/// definition and every materialized cell survive unchanged. Also drops the saved
/// workbook to TEMP so it can be opened in a real Excel instance for visual checks.
/// </summary>
public class PivotParityRoundTripTests
{
    [Fact]
    public void MultiLevelPivot_RefreshSaveReload_PreservesDefinitionAndMaterializedCells()
    {
        var workbook = BuildPivotWorkbook(out var sheet, out var pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var materialized = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot);
        var before = CaptureRange(sheet, materialized);

        // A known Excel-correct anchor: the Sum-of-Amount grand total over all 8 rows.
        before.Values.Should().Contain(v => v == "Grand Total", "grand total row must be materialized");
        FindRowValue(before, materialized, "Grand Total").Should().Be("220");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);

        // Drop the artifact for real-Excel visual verification (best-effort; ignored on CI).
        TryWriteArtifact(ms);

        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedSheet = reloaded.Sheets[0];

        reloaded.PivotCaches.Should().ContainSingle();
        reloadedSheet.PivotTables.Should().ContainSingle();

        var after = CaptureRange(reloadedSheet, materialized);
        after.Should().BeEquivalentTo(before,
            "every materialized pivot cell must survive an XLSX save/reload round-trip");
    }

    private static Workbook BuildPivotWorkbook(out Sheet sheet, out PivotTableModel pivot)
    {
        var workbook = new Workbook("PivotParity");
        sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 1, 2, "Quarter");
        SetText(sheet, 1, 3, "Channel");
        SetText(sheet, 1, 4, "Amount");

        // 8 rows: East/West x Q1/Q2 x Retail/Wholesale, amounts 10..45.
        var rows = new[]
        {
            ("East", "Q1", "Retail", 10), ("East", "Q1", "Wholesale", 15),
            ("East", "Q2", "Retail", 20), ("East", "Q2", "Wholesale", 25),
            ("West", "Q1", "Retail", 30), ("West", "Q1", "Wholesale", 35),
            ("West", "Q2", "Retail", 40), ("West", "Q2", "Wholesale", 45),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)i + 2;
            SetText(sheet, row, 1, rows[i].Item1);
            SetText(sheet, row, 2, rows[i].Item2);
            SetText(sheet, row, 3, rows[i].Item3);
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(rows[i].Item4));
        }

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 9, 4));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 8,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Quarter"));
        cache.Fields.Add(new PivotCacheFieldModel("Channel"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 1, 6),
                new CellAddress(sheet.Id, 30, 11)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(
            3, "% Parent Row", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentRowTotal));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static Dictionary<(uint Row, uint Col), string> CaptureRange(Sheet sheet, GridRange range)
    {
        var map = new Dictionary<(uint, uint), string>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var value = sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value;
            if (value is null or BlankValue)
                continue;
            map[(row, col)] = Format(value);
        }

        return map;
    }

    private static string Format(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            _ => value.ToString() ?? ""
        };

    private static string FindRowValue(
        Dictionary<(uint Row, uint Col), string> cells, GridRange range, string label)
    {
        foreach (var ((row, col), text) in cells)
        {
            if (text != label)
                continue;
            // Sum of Amount is the first data column, immediately right of the row-label columns.
            for (var c = col + 1; c <= range.End.Col; c++)
                if (cells.TryGetValue((row, c), out var value))
                    return value;
        }

        return "";
    }

    private static void TryWriteArtifact(MemoryStream package)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "freex-pivot-parity-verify.xlsx");
            File.WriteAllBytes(path, package.ToArray());
        }
        catch
        {
            // Artifact drop is best-effort; never fail the test on it.
        }
    }
}
