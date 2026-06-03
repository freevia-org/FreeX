using System.Diagnostics;
using System.IO.Compression;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageBreaksMetadataWriterPerformanceTests
{
    [Fact]
    public void Benchmark_SavePageBreaksMetadata_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 20;
        const int sheetCount = 12;
        const int breaksPerSheet = 64;

        var package = CreateSourcePackage(sheetCount);
        var workbook = CreatePageBreakWorkbook(sheetCount, breaksPerSheet);
        var worksheetPathMap = CreateWorksheetPathMap(package);

        using (var warmup = CreateWritablePackageStream(package))
            XlsxWorksheetPageBreaksMetadataWriter.Save(warmup, workbook, worksheetPathMap);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = CreateWritablePackageStream(package);
            var step = Stopwatch.StartNew();
            XlsxWorksheetPageBreaksMetadataWriter.Save(stream, workbook, worksheetPathMap);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_PAGE_BREAK_METADATA " +
            $"sheets={sheetCount} row_breaks_per_sheet={breaksPerSheet} col_breaks_per_sheet={breaksPerSheet} " +
            $"steps={iterations} package_bytes={package.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Save_UsesSingleBreakLookupForModeledBreaksAndNativeAttributes()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetPageBreaksMetadataWriter.cs"));

        source.Should().Contain("BuildBreaksById(pageBreaks)");
        source.Should().Contain("breaksById[idText] = breakElement;");
        source.Should().Contain("HasSupportedBreaks(sheet.RowPageBreaks");
        source.Should().Contain("LaterWorksheetElementNames.Contains");
        source.Should().NotContain(
            ".Where(id => IsSupportedBreakId",
            "modeled page breaks are already stored as distinct sorted sets and should not allocate LINQ filters");
        source.Should().NotContain(
            ".Distinct()",
            "modeled page breaks are already stored as distinct sorted sets and should not allocate a distinct set");
        source.Should().NotContain(
            ".ToArray()",
            "modeled page breaks should be streamed without materializing temporary arrays on save");
        source.Should().NotContain(
            ".OrderBy(id => id)",
            "sheet page breaks are already sorted by the model and should not be re-sorted on save");
        source.Should().NotContain(
            ".Any(element => string.Equals(element.Attribute(\"id\")?.Value",
            "existing worksheet breaks should be indexed once instead of scanned once per modeled break");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "the save loop should avoid allocating a LINQ iterator for metadata-bearing sheets");
    }

    private static byte[] CreateSourcePackage(int sheetCount)
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Breaks {sheetIndex}");
            sheet.Cell(1, 1).Value = sheetIndex;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Workbook CreatePageBreakWorkbook(int sheetCount, int breaksPerSheet)
    {
        var workbook = new Workbook("Page break metadata IO");
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Breaks {sheetIndex}");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(sheetIndex));
            sheet.RowPageBreaksMetadata = CreatePageBreaksMetadata("row", sheetIndex, breaksPerSheet);
            sheet.ColumnPageBreaksMetadata = CreatePageBreaksMetadata("col", sheetIndex, breaksPerSheet);

            for (uint breakIndex = 0; breakIndex < breaksPerSheet; breakIndex++)
            {
                var breakId = breakIndex + 2;
                sheet.RowPageBreaks.Add(breakId);
                sheet.ColumnPageBreaks.Add(breakId);
            }
        }

        return workbook;
    }

    private static WorksheetPageBreaksMetadataModel CreatePageBreaksMetadata(
        string prefix,
        int sheetIndex,
        int breaksPerSheet)
    {
        var metadata = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes =
            {
                ["customAttr"] = $"{prefix}-{sheetIndex}"
            }
        };

        for (uint breakIndex = 0; breakIndex < breaksPerSheet; breakIndex++)
        {
            var breakId = breakIndex + 2;
            metadata.BreakNativeAttributes[breakId] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pt"] = breakIndex % 2 == 0 ? "1" : "0",
                ["customBreakAttr"] = $"{prefix}-{sheetIndex}-{breakId}"
            };
        }

        return metadata;
    }

    private static XlsxWorkbookWorksheetPathMap CreateWorksheetPathMap(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return XlsxWorkbookWorksheetPathMap.TryCreate(archive)!;
    }

    private static MemoryStream CreateWritablePackageStream(byte[] package)
    {
        var stream = new MemoryStream(package.Length * 3);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        return stream;
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.Combine(new[] { Directory.GetCurrentDirectory() }.Concat(relativeParts).ToArray());
    }
}
