using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r592: removing any one package part must fail the load with FreeX's OWN typed error, never a raw
/// framework exception -- and must not silently drop content without saying so.
///
/// <para>Found by deleting each part of a saved workbook in turn and reloading. Two escaped r382's
/// missing-part mapping: a missing <c>xl/_rels/workbook.xml.rels</c> reached
/// <c>DocumentFormat.OpenXml</c>'s <c>OpenXmlPartContainer.GetPartById</c> and threw
/// <c>ArgumentOutOfRangeException</c>, and a missing worksheet <c>.rels</c> reached a bare
/// <c>Dictionary</c> indexer and threw <c>KeyNotFoundException</c>. Neither is a
/// NullReferenceException and neither carries r382's message, so both reached the shell -- which
/// shows <c>exception.Message</c> verbatim, so the user read "Index was out of range" for a damaged
/// file.</para>
///
/// <para>Two parts are a KNOWN, pinned residue rather than a passing case: removing a chart part, or
/// the drawing rels that resolve it, loads successfully with the chart silently gone.
/// <c>XlsxWorksheetDrawingPartReader.ReadChartParts</c> has three bare <c>continue</c>s for a chart
/// the drawing explicitly declares but the package cannot resolve, and no warnings channel reaches
/// that far -- <c>ReadHiddenSheetLayout</c> would have to grow one and thread it down.</para>
///
/// <para>Worth being precise about severity, because it is easy to overstate: the chart is ALREADY
/// gone from the file. FreeX is not destroying data, it is failing to REPORT that the file arrived
/// damaged, which is a reporting gap rather than corruption -- Excel says "we repaired this". The
/// two paths are pinned exactly, so the silence cannot spread to other content.</para>
/// </summary>
public sealed class R592_MissingPackagePartLoadTests
{
    private static byte[] RichWorkbookBytes()
    {
        var workbook = new Workbook("PartLoss");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        sheet.Comments[new CellAddress(sheet.Id, 3, 3)] = "a note";
        sheet.CommentAuthors[new CellAddress(sheet.Id, 3, 3)] = "Author";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 4, 4)] = "https://example.invalid/";

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
        };
        for (var i = 1; i <= 4; i++)
            table.Columns.Add(new StructuredTableColumnModel(i, $"H{i}"));
        sheet.StructuredTables.Add(table);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
            ShowLegend = true,
        });

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("second sheet content"));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        return ms.ToArray();
    }

    private static byte[] WithoutPart(byte[] source, string part)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                if (entry.FullName == part)
                    continue;

                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        return output.ToArray();
    }

    private static string DescribeContent(Workbook workbook)
    {
        var cells = 0;
        var tables = 0;
        var charts = 0;
        foreach (var sheet in workbook.Sheets)
        {
            cells += sheet.EnumerateCells().Count();
            tables += sheet.StructuredTables.Count;
            charts += sheet.Charts.Count;
        }

        return $"sheets={workbook.Sheets.Count} cells={cells} tables={tables} charts={charts}";
    }

    [Fact]
    public void RemovingAnyPartFailsWithFreeXsOwnErrorAndLosesNothingSilently()
    {
        var source = RichWorkbookBytes();

        string baseline;
        using (var ms = new MemoryStream(source))
            baseline = DescribeContent(new XlsxFileAdapter().Load(ms));

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(10,
            "the probe must actually be removing parts from a rich package");

        var leaked = new List<string>();
        var silentlyLost = new List<string>();

        foreach (var part in parts)
        {
            var mutated = WithoutPart(source, part);

            try
            {
                using var ms = new MemoryStream(mutated);
                var loaded = new XlsxFileAdapter().LoadWithWarnings(ms);
                var result = DescribeContent(loaded.Workbook);
                if (result == baseline)
                    continue;

                // r593: content missing from a damaged package is acceptable -- it is already gone
                // from the FILE -- but only if the load SAYS so. Silence is the defect.
                if (loaded.Warnings.Count == 0)
                    silentlyLost.Add($"{part} -> {result} (baseline {baseline}) with NO warning");
            }
            catch (WorkbookInvalidException)
            {
                // FreeX's own typed error: the file is refused, and the shell has a sentence for it.
            }
            catch (Exception ex)
            {
                leaked.Add($"{part} -> {ex.GetType().Name}");
            }
        }

        leaked.Should().BeEmpty(
            "a damaged package must surface FreeX's own WorkbookInvalidException, not a raw framework " +
            "exception whose Message the shell then shows to the user verbatim");

        silentlyLost.Should().BeEmpty(
            "content may not disappear from a loaded workbook without the load either refusing or " +
            "reporting it");
    }

    [Fact]
    public void AnUndamagedWorkbookLoadsWithNoWarningsAtAll()
    {
        // r593 non-vacuity, and the thing that keeps a warnings channel worth reading: the chart
        // warnings must fire ONLY on damage. A drawing that holds just shapes or text boxes has no
        // relationship part at all and is entirely ordinary, so warning on every missing rels part
        // would put a line in front of the user on healthy files -- which is how a warnings channel
        // stops being read at all.
        using var ms = new MemoryStream(RichWorkbookBytes());

        var loaded = new XlsxFileAdapter().LoadWithWarnings(ms);

        loaded.Warnings.Should().BeEmpty("a healthy package must load silently");
        loaded.Workbook.Sheets.Should().HaveCount(2);
    }
}
