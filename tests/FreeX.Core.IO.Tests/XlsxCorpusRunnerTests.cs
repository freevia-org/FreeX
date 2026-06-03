using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedCorpusRows_RoundTripThroughXlsxAdapter()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .ToArray();

        rows.Should().NotBeEmpty("generated corpus rows are deterministic and do not rely on redistributed Excel files");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var workbook = XlsxCorpusFixtureFactory.Create(row.Id);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);

            saved.Position = 0;
            var loaded = adapter.Load(saved);

            loaded.SheetCount.Should().Be(workbook.SheetCount, row.Id);
            loaded.Sheets.Select(sheet => sheet.Name).Should().Equal(workbook.Sheets.Select(sheet => sheet.Name), row.Id);
            loaded.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);
            CaptureSummary(loaded).Should().BeEquivalentTo(
                CaptureSummary(workbook),
                options => options.WithStrictOrdering(),
                row.Id);
            AssertExpectedFeatureTags(row, loaded);
        }
    }


    [Fact]
    public void WorkbookSummary_IncludesPopulatedCellStyles()
    {
        var workbook = new Workbook("StyledCells");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("styled"),
            StyleId = workbook.RegisterStyle(new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(1, 2, 3),
                NumberFormat = "0.00"
            })
        });
        var baseline = new Workbook("StyledCells");
        var baselineSheet = baseline.AddSheet("Sheet1");
        baselineSheet.SetCell(new CellAddress(baselineSheet.Id, 1, 1), new TextValue("styled"));

        CaptureSummary(workbook).Should().NotBe(CaptureSummary(baseline));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeNamedVisualObjects()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(tag => tag is "images" or "text-boxes" or "shapes"))
            .ToArray();

        rows.Should().NotBeEmpty("visual object identity should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbooks = rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id)).ToArray();
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.Pictures)
            .Should().Contain(picture => !string.IsNullOrWhiteSpace(picture.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.TextBoxes)
            .Should().Contain(textBox => !string.IsNullOrWhiteSpace(textBox.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DrawingShapes)
            .Should().Contain(shape => !string.IsNullOrWhiteSpace(shape.Name));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeWorksheetBackgroundImageCoverage()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("background-image"))
            .ToArray();

        rows.Should().ContainSingle("worksheet background images should have explicit deterministic corpus coverage");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbook = XlsxCorpusFixtureFactory.Create(rows[0].Id);
        workbook.Sheets
            .Where(sheet =>
                sheet.BackgroundImage is not null &&
                sheet.BackgroundImage.ContentType == "image/png" &&
                sheet.BackgroundImage.ImageBytes.Length > 0)
            .Should()
            .ContainSingle();
    }

}
