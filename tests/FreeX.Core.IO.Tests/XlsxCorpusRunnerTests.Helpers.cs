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
    private static IReadOnlyList<ManifestRow> ReadManifestRows()
    {
        var manifestPath = Path.Combine(FindWorkspaceRoot(), "test-corpus", "manifest.csv");
        return File.ReadAllLines(manifestPath)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestRow)
            .ToArray();
    }

    private static ManifestRow ParseManifestRow(string line)
    {
        var columns = line.Split(',');
        columns.Should().HaveCount(10);
        return new ManifestRow(columns[0], columns[1], columns[2], columns[6], columns[7], columns[8], columns[9]);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-corpus", "manifest.csv")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FreeX workspace root.");
    }

    private sealed record ManifestRow(
        string Id,
        string Path,
        string SourceType,
        string FeatureTags,
        string ExpectedWarnings,
        string ExpectedStatus,
        string Notes);

    private static readonly IReadOnlyDictionary<XlsxUnsupportedFeatureKind, string> ExpectedWarningText =
        new Dictionary<XlsxUnsupportedFeatureKind, string>
        {
            [XlsxUnsupportedFeatureKind.Macros] = "excluded VBA macro disclosed",
            [XlsxUnsupportedFeatureKind.Charts] = "unsupported chart package disclosed",
            [XlsxUnsupportedFeatureKind.EmbeddedObjects] = "unsupported embedded object disclosed",
            [XlsxUnsupportedFeatureKind.PowerQuery] = "excluded Power Query disclosed",
            [XlsxUnsupportedFeatureKind.DataModel] = "excluded Data Model disclosed",
            [XlsxUnsupportedFeatureKind.LinkedDataTypes] = "excluded linked data type disclosed",
            [XlsxUnsupportedFeatureKind.ThreadedComments] = "unsupported threaded comment disclosed",
            [XlsxUnsupportedFeatureKind.TrackChanges] = "unsupported track changes disclosed",
            [XlsxUnsupportedFeatureKind.FormControls] = "unsupported form control disclosed",
            [XlsxUnsupportedFeatureKind.DigitalSignatures] = "unsupported digital signature disclosed",
            [XlsxUnsupportedFeatureKind.CustomRibbonUi] = "unsupported custom ribbon UI disclosed",
            [XlsxUnsupportedFeatureKind.OfficeAddIns] = "unsupported Office add-in disclosed",
            [XlsxUnsupportedFeatureKind.LiveWebQueries] = "unsupported live web query disclosed",
            [XlsxUnsupportedFeatureKind.SensitivityLabels] = "unsupported sensitivity label disclosed",
            [XlsxUnsupportedFeatureKind.SmartArtDiagrams] = "unsupported SmartArt diagram disclosed",
            [XlsxUnsupportedFeatureKind.UnsupportedSheetTypes] = "unsupported sheet type disclosed"
        };

    private static XlsxUnsupportedFeatureKind[] ExpectedFeatureKindsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new List<XlsxUnsupportedFeatureKind>();

        if (tags.Contains("macros"))
            expected.Add(XlsxUnsupportedFeatureKind.Macros);

        if (tags.Contains("unsupported-chart-family"))
            expected.Add(XlsxUnsupportedFeatureKind.Charts);

        if (tags.Contains("power-query") || tags.Contains("connections"))
            expected.Add(XlsxUnsupportedFeatureKind.PowerQuery);

        if (tags.Contains("data-model") || tags.Contains("power-pivot"))
            expected.Add(XlsxUnsupportedFeatureKind.DataModel);

        if (tags.Contains("linked-data-types") || tags.Contains("rich-data"))
            expected.Add(XlsxUnsupportedFeatureKind.LinkedDataTypes);

        if (tags.Contains("threaded-comments"))
            expected.Add(XlsxUnsupportedFeatureKind.ThreadedComments);

        if (tags.Contains("track-changes") || tags.Contains("revision-history"))
            expected.Add(XlsxUnsupportedFeatureKind.TrackChanges);

        if (tags.Contains("form-controls") || tags.Contains("activex"))
            expected.Add(XlsxUnsupportedFeatureKind.FormControls);

        if (tags.Contains("digital-signatures"))
            expected.Add(XlsxUnsupportedFeatureKind.DigitalSignatures);

        if (tags.Contains("custom-ribbon-ui"))
            expected.Add(XlsxUnsupportedFeatureKind.CustomRibbonUi);

        if (tags.Contains("office-addins") || tags.Contains("webextensions"))
            expected.Add(XlsxUnsupportedFeatureKind.OfficeAddIns);

        if (tags.Contains("live-web-queries") || tags.Contains("web-publish"))
            expected.Add(XlsxUnsupportedFeatureKind.LiveWebQueries);

        if (tags.Contains("sensitivity-labels") || tags.Contains("irm"))
            expected.Add(XlsxUnsupportedFeatureKind.SensitivityLabels);

        if (tags.Contains("smartart") || tags.Contains("diagrams"))
            expected.Add(XlsxUnsupportedFeatureKind.SmartArtDiagrams);

        if (tags.Contains("chart-sheets") || tags.Contains("dialog-sheets") || tags.Contains("macro-sheets") || tags.Contains("unsupported-sheet-types"))
            expected.Add(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);

        if (tags.Contains("embedded-objects"))
            expected.Add(XlsxUnsupportedFeatureKind.EmbeddedObjects);

        return expected.Distinct().ToArray();
    }

    private static void AssertExpectedFeatureTags(ManifestRow row, Workbook workbook)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = CaptureSummary(workbook);

        if (tags.Contains("hyperlinks"))
            summary.Sheets.Sum(sheet => sheet.HyperlinkCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("comments") || tags.Contains("notes"))
            summary.Sheets.Sum(sheet => sheet.CommentCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("merged-cells"))
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);

        if (row.SourceType == "public" && tags.Contains("sheet-names") && tags.Contains("boundary"))
            summary.Sheets.Should().Contain(sheet => sheet.Name.Length == 31, row.Id);

        if (row.SourceType == "public" && tags.Contains("inline-strings"))
            summary.Sheets
                .SelectMany(sheet => sheet.Cells)
                .Should()
                .Contain(cell => cell.Value.Kind == "Text" && !string.IsNullOrEmpty(cell.Value.Value), row.Id);

        if (row.SourceType == "public" && tags.Contains("shared-strings"))
            summary.Sheets
                .SelectMany(sheet => sheet.Cells)
                .Should()
                .Contain(cell => cell.Value.Kind == "Text" && !string.IsNullOrEmpty(cell.Value.Value), row.Id);

        if (row.SourceType == "public" && tags.Contains("cell-types"))
            summary.Sheets
                .SelectMany(sheet => sheet.Cells)
                .Select(cell => cell.Value.Kind)
                .Distinct(StringComparer.Ordinal)
                .Should()
                .HaveCountGreaterThanOrEqualTo(3, row.Id);

        if (tags.Contains("formulas"))
            summary.Sheets.Sum(sheet => sheet.FormulaCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cross-sheet"))
        {
            summary.SheetCount.Should().BeGreaterThan(1, row.Id);
            workbook.Sheets
                .SelectMany(sheet => sheet.EnumerateCells())
                .Count(item => item.Cell.FormulaText?.Contains('!') == true)
                .Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("named-ranges"))
            summary.NamedRangeCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-validation"))
            summary.Sheets.Sum(sheet => sheet.DataValidationCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("conditional-formatting"))
            summary.Sheets.Sum(sheet => sheet.ConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("color-scales"))
            summary.Sheets.Sum(sheet => sheet.ColorScaleConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-bars"))
            summary.Sheets.Sum(sheet => sheet.DataBarConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("icon-sets"))
            summary.Sheets.Sum(sheet => sheet.IconSetConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("charts") && !tags.Contains("unsupported-chart-family"))
            summary.Sheets.Sum(sheet => sheet.ChartCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("surface-charts"))
        {
            var chartTypes = workbook.Sheets
                .SelectMany(sheet => sheet.Charts)
                .Select(chart => chart.Type)
                .ToArray();
            chartTypes.Should().Contain([ChartType.Surface, ChartType.ThreeDSurface], row.Id);
        }

        if (row.SourceType == "generated" && (tags.Contains("styles") || tags.Contains("formatting")))
            (workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count(item => item.Cell.StyleId != StyleId.Default)) +
             summary.Sheets.Sum(sheet => sheet.StyleOnlyCellCount)).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cell-types"))
            summary.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("text-boxes"))
            summary.Sheets.Sum(sheet => sheet.TextBoxCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("shapes"))
            summary.Sheets.Sum(sheet => sheet.DrawingShapeCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("images"))
            summary.Sheets.Sum(sheet => sheet.PictureCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("background-image"))
            summary.Sheets.Count(sheet => sheet.HasBackgroundImage).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("sparklines"))
            summary.Sheets.Sum(sheet => sheet.SparklineCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivottables"))
            summary.Sheets.Sum(sheet => sheet.PivotTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-caches"))
            summary.PivotCacheCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-styles"))
        {
            summary.PivotTableStyleCount.Should().BeGreaterThan(0, row.Id);
            summary.PivotTableStyleElementCount.Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("structured-tables") || tags.Contains("listobjects") || tags.Contains("tables"))
            summary.Sheets.Sum(sheet => sheet.StructuredTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("protection"))
        {
            summary.IsStructureProtected.Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.IsProtected).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.AllowEditRangeCount).Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("page-setup"))
        {
            summary.Sheets.Any(sheet => sheet.HasPrintArea || sheet.HasPrintTitleRows || sheet.HasPrintTitleColumns).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageOrientation == WorksheetPageOrientation.Landscape).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PaperSize == WorksheetPaperSize.Letter).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageMargins == WorksheetPageMargins.Narrow).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.ScaleToFit.FitToPagesWide == 1 && sheet.ScaleToFit.FitToPagesTall == 1).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PrintGridlines && sheet.PrintHeadings).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageHeader).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageFooter).Should().BeTrue(row.Id);
        }

        if (tags.Contains("freeze-panes"))
            summary.Sheets.Any(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0).Should().BeTrue(row.Id);

        if (tags.Contains("hidden-rows"))
            summary.Sheets.Sum(sheet => sheet.HiddenRowCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("hidden-columns"))
            summary.Sheets.Sum(sheet => sheet.HiddenColumnCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("custom-dimensions"))
            summary.Sheets.Sum(sheet => sheet.ColumnWidths.Count + sheet.RowHeights.Count).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("outline-groups"))
            summary.Sheets.Sum(sheet => sheet.RowOutlineLevelCount + sheet.ColumnOutlineLevelCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("row-column-groups"))
        {
            summary.Sheets.Sum(sheet => sheet.RowOutlineLevelCount).Should().BeGreaterThan(0, row.Id);
            summary.Sheets.Sum(sheet => sheet.ColumnOutlineLevelCount).Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("structure"))
        {
            summary.Sheets.Should().Contain(
                sheet => sheet.MergedRegionCount > 0 ||
                    sheet.FrozenRows > 0 ||
                    sheet.FrozenCols > 0 ||
                    sheet.HiddenRowCount > 0 ||
                    sheet.HiddenColumnCount > 0 ||
                    sheet.ColumnWidths.Count > 0 ||
                    sheet.RowHeights.Count > 0 ||
                    sheet.RowOutlineLevelCount > 0 ||
                    sheet.ColumnOutlineLevelCount > 0,
                row.Id);
        }
    }

}
