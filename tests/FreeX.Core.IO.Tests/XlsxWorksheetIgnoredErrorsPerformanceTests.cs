using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetIgnoredErrorsPerformanceTests
{
    [Fact]
    public void Save_CoalescesVerticallyAdjacentIgnoredErrorsWithSameNativeMetadata()
    {
        var workbook = new Workbook("Ignored error run coalescing");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 2; row++)
        {
            for (uint col = 1; col <= 2; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"{row}:{col}"));
                sheet.GetCell(row, col)!.IgnoreFormulaError = true;
            }
        }

        sheet.IgnoredErrorsMetadata = new WorksheetIgnoredErrorsMetadataModel
        {
            ErrorNativeAttributes =
            {
                ["A1:B2"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["twoDigitTextYear"] = "1"
                }
            }
        };

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var ignoredError = worksheetXml.Root!
            .Element(worksheetNs + "ignoredErrors")!
            .Elements(worksheetNs + "ignoredError")
            .Should()
            .ContainSingle()
            .Subject;

        ignoredError.Attribute("sqref")!.Value.Should().Be("A1:B2");
        ignoredError.Attribute("twoDigitTextYear")!.Value.Should().Be("1");
    }

    [Fact]
    public void SaveIgnoredErrors_BuildsRectangularRunsWithoutAddressListOrLinqFlagScan()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetDiagnosticsMapper.IgnoredErrors.cs"));

        source.Should().Contain("new List<(uint Row, uint Col)>(ignoredCellCount)");
        source.Should().Contain("AddMergedIgnoredErrorRun(runs, currentRun)");
        source.Should().Contain("previous.EndRow + 1 == run.StartRow");
        source.Should().NotContain(
            "new List<CellAddress>(ignoredCellCount)",
            "ignored-error save should sort primitive coordinates instead of copying SheetId into every temporary cell");
        source.Should().NotContain(
            "SupportedIgnoredErrorFlags.Any",
            "ignored-error load/save metadata checks should avoid LINQ iterator allocation");
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
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
