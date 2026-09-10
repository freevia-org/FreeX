using System.IO.Compression;
using FluentAssertions;
using System.Xml;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class R596_TruncatedPackagePartLoadTests
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

    private static byte[] Truncated(byte[] source, string part, double fraction)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                using var buffer = new MemoryStream();
                sourceStream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var keep = entry.FullName == part ? (int)(bytes.Length * fraction) : bytes.Length;
                targetStream.Write(bytes, 0, keep);
            }
        }

        return output.ToArray();
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

    /// <summary>
    /// Truncating a part is a DIFFERENT damage shape from deleting one: the zip stays valid and the
    /// entry holds malformed XML, which is what an interrupted download or a zip-repair tool
    /// actually produces. Every reader in the package layer then threw a raw XmlException, and the
    /// shell shows exception.Message verbatim -- so the user read "Unexpected end of file has
    /// occurred. The following elements are not closed" for a file this adapter already owns an
    /// accurate sentence about.
    /// <para>
    /// Two outcomes are legitimate and both are accepted here: refusing with FreeX's own typed error,
    /// or loading with less content AND a warning saying so (r593). What is not legitimate is a raw
    /// framework exception, or a silent loss.
    /// </para>
    /// </summary>
    [Fact]
    public void TruncatingAnyPartRefusesTypedOrReportsWhatItLost()
    {
        var source = RichWorkbookBytes();
        string baseline;
        using (var ms = new MemoryStream(source))
            baseline = DescribeContent(new XlsxFileAdapter().Load(ms));

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(8, "the fence is worthless if it stopped finding parts");

        var leaked = new List<string>();
        var silent = new List<string>();

        foreach (var part in parts)
        foreach (var fraction in new[] { 0.5, 0.9 })
        {
            var mutated = Truncated(source, part, fraction);
            try
            {
                using var ms = new MemoryStream(mutated);
                var loaded = new XlsxFileAdapter().LoadWithWarnings(ms);
                if (DescribeContent(loaded.Workbook) != baseline && loaded.Warnings.Count == 0)
                    silent.Add($"{part} at {(int)(fraction * 100)}%");
            }
            catch (WorkbookInvalidException)
            {
            }
            catch (XlsxThemePartCorruptException)
            {
                // FreeX's own typed error, naming the part and saying it could not be read.
            }
            catch (Exception ex)
            {
                leaked.Add($"{part} at {(int)(fraction * 100)}% -> {ex.GetType().Name}");
            }
        }

        leaked.Should().BeEmpty(
            "a truncated package must surface FreeX's own error, not a raw framework exception whose " +
            "Message the shell then shows to the user verbatim");

        silent.Should().BeEmpty(
            "content may not disappear from a loaded workbook without the load saying so");
    }
}
