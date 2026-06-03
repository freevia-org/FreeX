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

        if (tags.Contains("structure"))
        {
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);
            summary.Sheets.Any(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.HiddenRowCount + sheet.HiddenColumnCount).Should().BeGreaterThan(0, row.Id);
        }
    }

    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CaptureNamedRangeSummary(workbook, pair.Key, pair.Value))
                .ToArray(),
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            ToLegacyPasswordHash(workbook.StructureProtectionPassword),
            workbook.PivotCaches.Select(CapturePivotCacheSummary).ToArray(),
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.PivotTableStyles
                .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CapturePivotTableStyleSummary)
                .ToArray(),
            workbook.PivotTableStyles.Count,
            workbook.PivotTableStyles.Sum(style => style.Elements.Count),
            CapturePivotNumberFormatCatalogSummary(workbook),
            workbook.CustomViews
                .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CaptureCustomViewSummary)
                .ToArray(),
            workbook.CustomViews.Count,
            CaptureWorkbookMetadataSummary(workbook),
            CaptureWorkbookCalculationSummary(workbook),
            CaptureWorkbookThemeSummary(workbook.Theme),
            workbook.Sheets.Select(sheet => CaptureSheetSummary(workbook, sheet)).ToArray());

    private static WorkbookMetadataSummary CaptureWorkbookMetadataSummary(Workbook workbook) =>
        new(
            workbook.Slicers
                .OrderBy(slicer => slicer.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(slicer => new SlicerSummary(
                    slicer.Name,
                    slicer.Caption ?? "",
                    slicer.CacheName,
                    slicer.SourcePivotTableName ?? "",
                    slicer.SourceFieldName ?? "",
                    slicer.StyleName ?? "",
                    slicer.SelectedItems.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    slicer.PackagePart))
                .ToArray(),
            workbook.Timelines
                .OrderBy(timeline => timeline.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(timeline => new TimelineSummary(
                    timeline.Name,
                    timeline.Caption ?? "",
                    timeline.CacheName,
                    timeline.SourcePivotTableName ?? "",
                    timeline.SourceFieldName ?? "",
                    timeline.StyleName ?? "",
                    timeline.StartDate ?? "",
                    timeline.EndDate ?? "",
                    timeline.SelectedStartDate ?? "",
                    timeline.SelectedEndDate ?? "",
                    timeline.PackagePart))
                .ToArray(),
            workbook.ExternalLinks
                .OrderBy(link => link.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(link => new ExternalLinkSummary(
                    link.PackagePart,
                    link.TargetUri ?? "",
                    link.TargetMode ?? ""))
                .ToArray(),
            workbook.WatchedCells
                .Select(address => new WatchedCellSummary(
                    workbook.GetSheet(address.Sheet)?.Name ?? "",
                    address.Row,
                    address.Col))
                .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ToArray(),
            workbook.Scenarios
                .OrderBy(scenario => scenario.Name, StringComparer.OrdinalIgnoreCase)
                .Select(scenario => new ScenarioSummary(
                    scenario.Name,
                    scenario.ChangingCells
                        .Select(change => new ScenarioCellSummary(
                            workbook.GetSheet(change.Address.Sheet)?.Name ?? "",
                            change.Address.Row,
                            change.Address.Col,
                            CaptureScalarValueSummary(change.Value)))
                        .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(cell => cell.Row)
                        .ThenBy(cell => cell.Column)
                        .ToArray()))
                .ToArray());

    private static WorkbookCalculationSummary CaptureWorkbookCalculationSummary(Workbook workbook) =>
        new(
            workbook.CalculationMode,
            workbook.FullCalculationOnLoad,
            workbook.ForceFullCalculation,
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange);

    private static IReadOnlyList<NumberFormatCatalogSummary> CapturePivotNumberFormatCatalogSummary(Workbook workbook)
    {
        var referencedIds = workbook.PivotCaches
            .SelectMany(cache => cache.Fields)
            .Select(field => field.NumberFormatId)
            .Concat(workbook.Sheets
                .SelectMany(sheet => sheet.PivotTables)
                .SelectMany(pivot => pivot.DataFields)
                .Select(field => field.NumberFormatId))
            .Where(id => id is >= 164)
            .Select(id => id!.Value)
            .ToHashSet();

        return workbook.NumberFormatCatalog
            .Where(pair => referencedIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => new NumberFormatCatalogSummary(pair.Key, pair.Value))
            .ToArray();
    }

    private static WorkbookThemeSummary CaptureWorkbookThemeSummary(WorkbookTheme theme) =>
        new(
            theme.Name,
            theme.MajorFontName,
            theme.MinorFontName,
            theme.EffectsName,
            Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new ThemeColorSummary(slot, ToColorSummary(theme.GetColor(slot))))
                .ToArray());

    private static string ToColorSummary(CellColor color) =>
        FormattableString.Invariant($"{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string ToLegacyPasswordHash(string? passwordOrHash)
    {
        if (string.IsNullOrWhiteSpace(passwordOrHash))
            return "";
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        var hash = 0;
        for (var i = 0; i < passwordOrHash.Length; i++)
        {
            var value = passwordOrHash[i] << (i + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= passwordOrHash.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static bool IsLegacyPasswordHash(string value) =>
        value.Length is > 0 and <= 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');

    private static SheetSummary CaptureSheetSummary(Workbook workbook, Sheet sheet) =>
        new(
            sheet.Name,
            sheet.EnumerateCells()
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => CaptureCellSummary(workbook, item.Address, item.Cell))
                .ToArray(),
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Select(CaptureDataValidationSummary).ToArray(),
            sheet.DataValidations.Count,
            sheet.ConditionalFormats
                .OrderBy(format => format.AppliesTo.Start.Row)
                .ThenBy(format => format.AppliesTo.Start.Col)
                .ThenBy(format => format.AppliesTo.End.Row)
                .ThenBy(format => format.AppliesTo.End.Col)
                .ThenBy(format => format.Priority)
                .ThenBy(format => format.RuleType)
                .Select(CaptureConditionalFormatSummary)
                .ToArray(),
            sheet.ConditionalFormats.Count,
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.ColorScale),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.DataBar),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.IconSet),
            sheet.Comments
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => new CommentSummary(pair.Key.Row, pair.Key.Col, pair.Value))
                .ToArray(),
            sheet.Comments.Count,
            sheet.Hyperlinks
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => CaptureHyperlinkSummary(sheet, pair))
                .ToArray(),
            sheet.Hyperlinks.Count,
            sheet.Charts.Select(CaptureChartSummary).ToArray(),
            sheet.Charts.Count,
            sheet.PivotTables.Select(CapturePivotTableSummary).ToArray(),
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Select(CaptureStructuredTableSummary).ToArray(),
            sheet.StructuredTables.Count,
            sheet.StructuredTables.Sum(table => table.Columns.Count),
            sheet.Sparklines.Select(sparkline => new SparklineSummary(sparkline.Kind, ToRangeSummary(sparkline.DataRange), sparkline.Location.Row, sparkline.Location.Col)).ToArray(),
            sheet.Sparklines.Count,
            sheet.TextBoxes.Select(CaptureTextBoxSummary).ToArray(),
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Select(CaptureDrawingShapeSummary).ToArray(),
            sheet.DrawingShapes.Count,
            sheet.Pictures.Select(CapturePictureSummary).ToArray(),
            sheet.Pictures.Count,
            CaptureBackgroundImageSummary(sheet.BackgroundImage),
            sheet.BackgroundImage is not null,
            sheet.IsProtected,
            ToLegacyPasswordHash(sheet.ProtectionPassword),
            sheet.AllowEditRanges
                .OrderBy(range => range.Start.Row)
                .ThenBy(range => range.Start.Col)
                .ThenBy(range => range.End.Row)
                .ThenBy(range => range.End.Col)
                .Select(ToRangeSummary)
                .ToArray(),
            sheet.AllowEditRanges.Count,
            sheet.PrintArea.HasValue ? ToRangeSummary(sheet.PrintArea.Value) : null,
            sheet.PrintArea is not null,
            sheet.PrintTitleRows.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleRows.Value) : null,
            sheet.PrintTitleRows is not null,
            sheet.PrintTitleColumns.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleColumns.Value) : null,
            sheet.PrintTitleColumns is not null,
            sheet.PageOrientation,
            sheet.PaperSize,
            sheet.PageMargins,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.ScaleToFit,
            sheet.PrintGridlines,
            sheet.PrintHeadings,
            CaptureHeaderFooterSummary(sheet.PageHeader),
            !sheet.PageHeader.Equals(new WorksheetHeaderFooter("", "", "")),
            CaptureHeaderFooterSummary(sheet.PageFooter),
            !sheet.PageFooter.Equals(new WorksheetHeaderFooter("", "", "")),
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter,
            sheet.DifferentOddEvenHeaderFooter,
            sheet.HeaderFooterScaleWithDocument,
            sheet.HeaderFooterAlignWithMargins,
            CaptureHeaderFooterPictureSetSummary(sheet.PageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.PageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageFooterPictures),
            sheet.CenterHorizontallyOnPage,
            sheet.CenterVerticallyOnPage,
            sheet.PageOrder,
            sheet.FirstPageNumber,
            sheet.PrintBlackAndWhite,
            sheet.PrintDraftQuality,
            sheet.PrintQualityDpi,
            sheet.PrintErrorValue,
            sheet.PrintComments,
            sheet.DefaultColumnWidth,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultColumnWidth) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowHeights
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultRowHeight) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowPageBreaks.OrderBy(row => row).ToArray(),
            sheet.RowPageBreaks.Count,
            sheet.ColumnPageBreaks.OrderBy(column => column).ToArray(),
            sheet.ColumnPageBreaks.Count,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ViewMode,
            sheet.ViewTopRow,
            sheet.ViewLeftCol,
            sheet.ActiveRow,
            sheet.ActiveCol,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            sheet.FullCalculationOnLoad,
            CapturePhoneticSummary(sheet.PhoneticProperties),
            sheet.IsHidden,
            sheet.IsVeryHidden,
            sheet.CodeName ?? "",
            sheet.TabColor is null ? "" : ToColorSummary(sheet.TabColor.Value),
            sheet.CustomProperties
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property => new WorksheetCustomPropertySummary(property.Name, property.Id))
                .ToArray(),
            CaptureEffectiveHiddenRows(sheet),
            CaptureEffectiveHiddenRows(sheet).Length,
            [],
            0,
            sheet.HiddenCols.OrderBy(column => column).ToArray(),
            sheet.HiddenCols.Count,
            sheet.RowOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.RowOutlineLevels.Count,
            sheet.ColOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.ColOutlineLevels.Count,
            sheet.GroupHiddenRows.OrderBy(row => row).ToArray(),
            sheet.GroupHiddenRows.Count,
            sheet.GroupHiddenCols.OrderBy(column => column).ToArray(),
            sheet.GroupHiddenCols.Count,
            sheet.GetStyleOnlyEntries()
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => new StyleOnlyCellSummary(
                    entry.Key.Row,
                    entry.Key.Col,
                    CaptureStyleSummary(workbook.GetStyle(entry.StyleId))))
                .ToArray(),
            sheet.GetStyleOnlyEntries().Count());

    private static uint[] CaptureEffectiveHiddenRows(Sheet sheet) =>
        sheet.HiddenRows
            .Concat(sheet.FilterHiddenRows)
            .Distinct()
            .OrderBy(row => row)
            .ToArray();

    private static PhoneticSummary? CapturePhoneticSummary(WorksheetPhoneticProperties? properties) =>
        properties is null
            ? null
            : new PhoneticSummary(
                properties.FontId ?? "",
                properties.Type ?? "",
                properties.Alignment ?? "");

    private static BackgroundImageSummary? CaptureBackgroundImageSummary(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new BackgroundImageSummary(
                background.ContentType,
                background.FileName ?? "",
                background.ImageBytes.Length);

    private static HyperlinkSummary CaptureHyperlinkSummary(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
        metadata ??= new HyperlinkMetadata();
        return new HyperlinkSummary(
            pair.Key.Row,
            pair.Key.Col,
            pair.Value,
            metadata.LinkType,
            metadata.ScreenTip,
            metadata.Bookmark);
    }

    private static NamedRangeSummary CaptureNamedRangeSummary(Workbook workbook, string name, GridRange range)
    {
        var metadata = workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
            ? savedMetadata
            : NamedRangeMetadata.WorkbookScope;

        return new NamedRangeSummary(
            name,
            metadata.Scope,
            metadata.Comment,
            ToRangeSummary(range));
    }

    private static IReadOnlyList<FormulaCellSummary> CaptureFormulaCellSummaries(Workbook workbook) =>
        workbook.Sheets
            .SelectMany(sheet => sheet.EnumerateCells()
                .Where(item => item.Cell.HasFormula)
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => new FormulaCellSummary(
                    sheet.Name,
                    item.Address.Row,
                    item.Address.Col,
                    item.Cell.FormulaText ?? "",
                    CaptureScalarValueSummary(item.Cell.Value))))
            .ToArray();

    private static CellSummary CaptureCellSummary(Workbook workbook, CellAddress address, Cell cell) =>
        new(
            address.Row,
            address.Col,
            cell.HasFormula ? new ScalarValueSummary("FormulaCachedValue", "") : CaptureScalarValueSummary(cell.Value),
            cell.FormulaText ?? "",
            cell.IgnoreFormulaError,
            CaptureStyleSummary(workbook.GetStyle(cell.StyleId)));

    private static ScalarValueSummary CaptureScalarValueSummary(ScalarValue value) =>
        value switch
        {
            BlankValue => new ScalarValueSummary("Blank", ""),
            NumberValue number => new ScalarValueSummary("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolValue boolean => new ScalarValueSummary("Boolean", boolean.Value ? "TRUE" : "FALSE"),
            TextValue text => new ScalarValueSummary("Text", text.Value),
            DateTimeValue dateTime => new ScalarValueSummary("DateTime", dateTime.Value.ToString("R", CultureInfo.InvariantCulture)),
            ErrorValue error => new ScalarValueSummary("Error", error.Code),
            _ => new ScalarValueSummary(value.GetType().Name, value.ToString() ?? "")
        };

    private static CustomViewSummary CaptureCustomViewSummary(WorkbookCustomView view) =>
        new(
            view.Name,
            view.IncludePrintSettings,
            view.IncludeHiddenRowsColumnsAndFilterSettings,
            view.Sheets
                .OrderBy(sheet => sheet.SheetName, StringComparer.OrdinalIgnoreCase)
                .Select(sheet => new CustomViewSheetSummary(
                    sheet.SheetName,
                    sheet.ViewMode,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    sheet.ZoomPercent,
                    sheet.ShowFormulas))
                .ToArray());

    private static ChartSummary CaptureChartSummary(ChartModel chart) =>
        new(
            chart.Type,
            chart.Title ?? "",
            chart.XAxisTitle ?? "",
            chart.YAxisTitle ?? "",
            CaptureChartVisualSummary(chart),
            CaptureChartAxisSummary(chart, isXAxis: true),
            CaptureChartAxisSummary(chart, isXAxis: false),
            chart.ShowLegend,
            chart.IsPivotChart,
            chart.PivotSourceFormatId,
            chart.Uses1904DateSystem,
            chart.Language ?? "",
            chart.ChartStyleId,
            chart.RoundedCorners,
            chart.BlankDisplayMode,
            chart.ShowDataLabelsOverMaximum,
            chart.AutoTitleDeleted,
            chart.ShowDataInHiddenRowsAndColumns,
            CaptureChartProtectionSummary(chart.Protection),
            CaptureChartPrintSettingsSummary(chart.PrintSettings),
            CaptureChartColorMapSummary(chart.ColorMapOverride),
            CaptureChartExternalDataSummary(chart.ExternalData),
            CaptureChartManualLayoutSummary(chart.PlotAreaLayout),
            CaptureChartManualLayoutSummary(chart.LegendLayout),
            chart.LegendPosition,
            chart.LegendOverlay,
            chart.ShowDataLabels,
            chart.ShowDataLabelValue,
            chart.ShowDataLabelLegendKey,
            chart.ShowDataLabelBubbleSize,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.DataLabelPosition,
            chart.DataLabelSeparator,
            chart.DataLabelNumberFormat,
            chart.ShowDataLabelCallouts,
            chart.DataLabelFillColor is null ? "" : ToColorSummary(chart.DataLabelFillColor.Value),
            chart.DataLabelFillThemeColor,
            chart.DataLabelBorderColor is null ? "" : ToColorSummary(chart.DataLabelBorderColor.Value),
            chart.DataLabelBorderThemeColor,
            chart.DataLabelTextColor is null ? "" : ToColorSummary(chart.DataLabelTextColor.Value),
            chart.DataLabelTextThemeColor,
            chart.DataLabelBorderThickness,
            chart.DataLabelFontSize,
            chart.DataLabelAngle,
            chart.BarGapWidth,
            chart.BarOverlap,
            chart.VaryColorsByPoint,
            chart.BubbleScale,
            chart.ShowNegativeBubbles,
            chart.BubbleSizeRepresents,
            CaptureChartTrendlineSummary(chart),
            CaptureChartErrorBarSummary(chart),
            CaptureChartGuideLineSummary(
                chart.ShowDropLines,
                chart.DropLineColor,
                chart.DropLineThemeColor,
                chart.DropLineThickness,
                chart.DropLineDashStyle),
            chart.StockSubtype,
            CaptureChartGuideLineSummary(
                chart.ShowHighLowLines,
                chart.HighLowLineColor,
                chart.HighLowLineThemeColor,
                chart.HighLowLineThickness,
                chart.HighLowLineDashStyle),
            CaptureChartGuideLineSummary(
                chart.ShowSeriesLines,
                chart.SeriesLineColor,
                chart.SeriesLineThemeColor,
                chart.SeriesLineThickness,
                chart.SeriesLineDashStyle),
            CaptureChartUpDownBarsSummary(chart),
            CaptureChartDataTableSummary(chart.DataTable),
            CaptureChart3DViewSummary(chart.ThreeDView),
            CaptureChartSurfaceFormatSummary(chart.FloorFormat),
            CaptureChartSurfaceFormatSummary(chart.SideWallFormat),
            CaptureChartSurfaceFormatSummary(chart.BackWallFormat),
            new ChartRangeSummary(
                chart.DataRange.Start.Row,
                chart.DataRange.Start.Col,
                chart.DataRange.End.Row,
                chart.DataRange.End.Col));

    private static ChartDataTableSummary? CaptureChartDataTableSummary(ChartDataTableModel? dataTable) =>
        dataTable is null
            ? null
            : new ChartDataTableSummary(
                dataTable.ShowHorizontalBorder,
                dataTable.ShowVerticalBorder,
                dataTable.ShowOutline,
                dataTable.ShowLegendKeys);

    private static ChartProtectionSummary? CaptureChartProtectionSummary(ChartProtectionModel? protection) =>
        protection is null
            ? null
            : new ChartProtectionSummary(
                protection.ChartObject,
                protection.Data,
                protection.Formatting,
                protection.Selection,
                protection.UserInterface);

    private static ChartPrintSettingsSummary? CaptureChartPrintSettingsSummary(ChartPrintSettingsModel? printSettings) =>
        printSettings is null
            ? null
            : new ChartPrintSettingsSummary(
                CaptureChartPageMarginsSummary(printSettings.PageMargins),
                CaptureChartPageSetupSummary(printSettings.PageSetup));

    private static ChartPageMarginsSummary? CaptureChartPageMarginsSummary(ChartPageMarginsModel? pageMargins) =>
        pageMargins is null
            ? null
            : new ChartPageMarginsSummary(
                pageMargins.Left,
                pageMargins.Right,
                pageMargins.Top,
                pageMargins.Bottom,
                pageMargins.Header,
                pageMargins.Footer);

    private static ChartPageSetupSummary? CaptureChartPageSetupSummary(ChartPageSetupModel? pageSetup) =>
        pageSetup is null
            ? null
            : new ChartPageSetupSummary(
                pageSetup.PaperSize ?? "",
                pageSetup.Orientation ?? "",
                pageSetup.Copies,
                pageSetup.BlackAndWhite,
                pageSetup.Draft);

    private static ChartTrendlineSummary CaptureChartTrendlineSummary(ChartModel chart) =>
        new(
            chart.ShowLinearTrendline,
            chart.TrendlineType,
            chart.TrendlinePeriod,
            chart.TrendlineOrder,
            chart.ShowTrendlineEquation,
            chart.ShowTrendlineRSquared,
            chart.TrendlineColor is null ? "" : ToColorSummary(chart.TrendlineColor.Value),
            chart.TrendlineThemeColor,
            chart.TrendlineThickness,
            chart.TrendlineDashStyle);

    private static ChartErrorBarSummary CaptureChartErrorBarSummary(ChartModel chart) =>
        new(
            chart.ShowErrorBars,
            chart.ErrorBarKind,
            chart.ErrorBarDirection,
            chart.ErrorBarValue,
            chart.ErrorBarEndCaps,
            chart.ErrorBarColor is null ? "" : ToColorSummary(chart.ErrorBarColor.Value),
            chart.ErrorBarThemeColor,
            chart.ErrorBarThickness,
            chart.ErrorBarDashStyle);
    private static ChartGuideLineSummary CaptureChartGuideLineSummary(
        bool show,
        CellColor? color,
        WorkbookThemeColorReference? themeColor,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(
            show,
            color is null ? "" : ToColorSummary(color.Value),
            themeColor,
            thickness,
            dashStyle);

    private static ChartUpDownBarsSummary CaptureChartUpDownBarsSummary(ChartModel chart) =>
        new(
            chart.ShowUpDownBars,
            chart.UpDownBarGapWidth,
            CaptureChartBarShapeSummary(
                chart.UpBarFillColor,
                chart.UpBarFillThemeColor,
                chart.UpBarBorderColor,
                chart.UpBarBorderThemeColor,
                chart.UpBarBorderThickness),
            CaptureChartBarShapeSummary(
                chart.DownBarFillColor,
                chart.DownBarFillThemeColor,
                chart.DownBarBorderColor,
                chart.DownBarBorderThemeColor,
                chart.DownBarBorderThickness));

    private static ChartBarShapeSummary CaptureChartBarShapeSummary(
        CellColor? fillColor,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? borderColor,
        WorkbookThemeColorReference? borderThemeColor,
        double? borderThickness) =>
        new(
            fillColor is null ? "" : ToColorSummary(fillColor.Value),
            fillThemeColor,
            borderColor is null ? "" : ToColorSummary(borderColor.Value),
            borderThemeColor,
            borderThickness);

    private static ChartVisualSummary CaptureChartVisualSummary(ChartModel chart) =>
        new(
            chart.ChartTitleTextColor is null ? "" : ToColorSummary(chart.ChartTitleTextColor.Value),
            chart.ChartTitleTextThemeColor,
            chart.ChartTitleFontSize,
            chart.AxisTitleTextColor is null ? "" : ToColorSummary(chart.AxisTitleTextColor.Value),
            chart.AxisTitleTextThemeColor,
            chart.AxisTitleFontSize,
            chart.ChartAreaFillColor is null ? "" : ToColorSummary(chart.ChartAreaFillColor.Value),
            chart.ChartAreaFillThemeColor,
            chart.PlotAreaFillColor is null ? "" : ToColorSummary(chart.PlotAreaFillColor.Value),
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaBorderColor is null ? "" : ToColorSummary(chart.PlotAreaBorderColor.Value),
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderThickness,
            chart.LegendTextColor is null ? "" : ToColorSummary(chart.LegendTextColor.Value),
            chart.LegendTextThemeColor,
            chart.LegendFillColor is null ? "" : ToColorSummary(chart.LegendFillColor.Value),
            chart.LegendFillThemeColor,
            chart.LegendBorderColor is null ? "" : ToColorSummary(chart.LegendBorderColor.Value),
            chart.LegendBorderThemeColor,
            chart.LegendBorderThickness,
            chart.LegendFontSize);

    private static ChartAxisSummary CaptureChartAxisSummary(ChartModel chart, bool isXAxis) =>
        isXAxis
            ? new ChartAxisSummary(
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisIsDateAxis,
                chart.XAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMajorGridlineColor.Value),
                chart.XAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMinorGridlineColor.Value),
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.ShowXAxisLabels,
                chart.XAxisLabelTextColor is null ? "" : ToColorSummary(chart.XAxisLabelTextColor.Value),
                chart.XAxisLabelTextThemeColor,
                chart.XAxisLabelFontSize,
                chart.XAxisLabelAngle,
                chart.XAxisLabelSkip,
                chart.XAxisTickMarkSkip,
                chart.XAxisLabelOffset,
                chart.XAxisLineColor is null ? "" : ToColorSummary(chart.XAxisLineColor.Value),
                chart.XAxisLineThickness)
            : new ChartAxisSummary(
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                false,
                chart.YAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMajorGridlineColor.Value),
                chart.YAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMinorGridlineColor.Value),
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.ShowYAxisLabels,
                chart.YAxisLabelTextColor is null ? "" : ToColorSummary(chart.YAxisLabelTextColor.Value),
                chart.YAxisLabelTextThemeColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                0,
                0,
                0,
                chart.YAxisLineColor is null ? "" : ToColorSummary(chart.YAxisLineColor.Value),
                chart.YAxisLineThickness);

    private static ChartColorMapSummary? CaptureChartColorMapSummary(ChartColorMapOverrideModel? colorMap) =>
        colorMap is null
            ? null
            : new ChartColorMapSummary(
                colorMap.UseMasterColorMapping,
                colorMap.OverrideMappings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ChartColorMapEntrySummary(pair.Key, pair.Value))
                    .ToArray());

    private static ChartExternalDataSummary? CaptureChartExternalDataSummary(ChartExternalDataModel? externalData) =>
        externalData is null
            ? null
            : new ChartExternalDataSummary(
                externalData.RelationshipId ?? "",
                externalData.RelationshipType ?? "",
                externalData.Target ?? "",
                externalData.TargetMode ?? "",
                externalData.AutoUpdate);

    private static ChartManualLayoutSummary? CaptureChartManualLayoutSummary(ChartManualLayoutModel? layout) =>
        layout is null
            ? null
            : new ChartManualLayoutSummary(
                layout.LayoutTarget ?? "",
                layout.XMode ?? "",
                layout.YMode ?? "",
                layout.WidthMode ?? "",
                layout.HeightMode ?? "",
                layout.X,
                layout.Y,
                layout.Width,
                layout.Height);

    private static Chart3DViewSummary? CaptureChart3DViewSummary(Chart3DViewModel? view) =>
        view is null
            ? null
            : new Chart3DViewSummary(
                view.RotationX,
                view.HeightPercent,
                view.RotationY,
                view.DepthPercent,
                view.RightAngleAxes,
                view.Perspective);

    private static ChartSurfaceFormatSummary? CaptureChartSurfaceFormatSummary(ChartSurfaceFormatModel? format) =>
        format is null
            ? null
            : new ChartSurfaceFormatSummary(
                format.FillColor is null ? "" : ToColorSummary(format.FillColor.Value),
                format.FillThemeColor,
                format.BorderColor is null ? "" : ToColorSummary(format.BorderColor.Value),
                format.BorderThemeColor,
                format.BorderThickness);

    private static PivotCacheSummary CapturePivotCacheSummary(PivotCacheModel cache) =>
        new(
            cache.CacheId,
            cache.SourceType,
            cache.SourceSheetName ?? "",
            cache.SourceReference ?? "",
            cache.SourceTableName ?? "",
            cache.ConnectionId,
            cache.IsOlap,
            cache.RefreshOnLoad,
            cache.SaveData,
            cache.EnableRefresh,
            cache.PreserveSourceSortFilter,
            cache.MissingItemsLimit,
            cache.RecordCount,
            cache.CreatedVersion,
            cache.MinRefreshableVersion,
            cache.RefreshedVersion,
            cache.RefreshedBy ?? "",
            cache.RefreshedDateIso ?? "",
            cache.Fields
                .Select(field => new PivotCacheFieldSummary(
                    field.Name,
                    field.NumberFormatId,
                    field.SharedItemCount,
                    field.ContainsBlank,
                    field.ContainsString,
                    field.ContainsNumber,
                    field.ContainsDate,
                    field.ContainsMixedTypes,
                    field.ContainsSemiMixedTypes,
                    field.ContainsNonDate,
                    field.ContainsInteger,
                    field.ContainsLongText,
                    field.MinValue,
                    field.MaxValue,
                    field.MinDate ?? "",
                    field.MaxDate ?? "",
                    field.SharedItems?.ToArray() ?? []))
                .ToArray());

    private static StructuredTableSummary CaptureStructuredTableSummary(StructuredTableModel table) =>
        new(
            table.Name,
            table.DisplayName,
            table.StyleName ?? "",
            table.HasAutoFilter,
            table.TotalsRowShown,
            table.ShowFirstColumn,
            table.ShowLastColumn,
            table.ShowRowStripes,
            table.ShowColumnStripes,
            NormalizeXml(table.NativeSortStateXml),
            new ChartRangeSummary(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col),
            table.Columns
                .Select(column => new StructuredTableColumnSummary(
                    column.Id,
                    column.Name,
                    column.TotalsRowLabel ?? "",
                    column.TotalsRowFunction ?? "",
                    column.CalculatedColumnFormula ?? "",
                    column.TotalsRowFormula ?? ""))
                .ToArray(),
            table.FilterColumns
                .OrderBy(filter => filter.ColumnId)
                .Select(filter => new StructuredTableFilterColumnSummary(
                    filter.ColumnId,
                    filter.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    filter.IncludeBlank,
                    filter.CustomFilters
                        .Select(customFilter => new StructuredTableCustomFilterSummary(
                            customFilter.Operator ?? "",
                            customFilter.Value ?? "",
                            customFilter.NativeAttributes is null
                                ? []
                                : customFilter.NativeAttributes
                                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                    .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                                    .ToArray()))
                        .ToArray(),
                    filter.CustomFiltersAnd,
                    filter.CustomFiltersAndRaw ?? "",
                    filter.NativeCustomFiltersAttributes is null
                        ? []
                        : filter.NativeCustomFiltersAttributes
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                            .ToArray(),
                    filter.NativeFilterXmls.Select(NormalizeXml).ToArray(),
                    filter.NativeAttributes is null
                        ? []
                        : filter.NativeAttributes
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                            .ToArray()))
                .ToArray());

    private static PivotTableStyleSummary CapturePivotTableStyleSummary(PivotTableStyleModel style) =>
        new(
            style.Name,
            style.AppliesToPivotTables,
            style.AppliesToTables,
            style.Elements
                .OrderBy(element => element.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(element => element.DifferentialFormatId)
                .ThenBy(element => element.Size)
                .Select(element => new PivotTableStyleElementSummary(
                    element.Type,
                    element.DifferentialFormatId,
                    element.Size))
                .ToArray());

    private static PivotTableSummary CapturePivotTableSummary(PivotTableModel pivot) =>
        new(
            pivot.Name,
            pivot.CacheId,
            ToRangeSummary(pivot.SourceRange),
            ToRangeSummary(pivot.TargetRange),
            pivot.DataOnRows,
            pivot.FirstHeaderRow,
            pivot.FirstDataRow,
            pivot.FirstDataColumn,
            pivot.ShowSubtotals,
            pivot.SubtotalPlacement,
            pivot.ShowRowGrandTotals,
            pivot.ShowColumnGrandTotals,
            pivot.RepeatItemLabels,
            pivot.BlankLineAfterItems,
            pivot.ReportLayout,
            pivot.StyleName,
            pivot.ShowRowHeaders,
            pivot.ShowColumnHeaders,
            pivot.ShowRowStripes,
            pivot.ShowColumnStripes,
            pivot.ShowFieldHeaders,
            pivot.ShowContextualTooltips,
            pivot.ShowPropertiesInTooltips,
            pivot.ShowClassicLayout,
            pivot.MergeAndCenterLabels,
            pivot.ShowItemsWithNoDataOnRows,
            pivot.ShowItemsWithNoDataOnColumns,
            pivot.PageOverThenDown,
            pivot.PageWrap,
            pivot.EmptyValueText ?? "",
            pivot.ApplyNumberFormats,
            pivot.ApplyBorderFormats,
            pivot.ApplyFontFormats,
            pivot.ApplyPatternFormats,
            pivot.AutofitColumnsOnUpdate,
            pivot.PreserveFormattingOnUpdate,
            pivot.ShowExpandCollapseButtons,
            pivot.EnableDrill,
            pivot.AsteriskTotals,
            pivot.MultipleFieldFilters,
            pivot.EnableFieldDialog,
            pivot.EnableFieldProperties,
            pivot.EnableDataValueEditing,
            pivot.PrintTitles,
            pivot.PrintExpandCollapseButtons,
            pivot.AltTextTitle ?? "",
            pivot.AltTextDescription ?? "",
            pivot.DataCaption ?? "",
            pivot.GrandTotalCaption ?? "",
            pivot.MissingCaption ?? "",
            pivot.ErrorCaption ?? "",
            pivot.RowFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.ColumnFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.PageFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.DataFields.Select(CapturePivotDataFieldSummary).ToArray());

    private static PivotFieldSummary CapturePivotFieldSummary(PivotFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.SelectedItem ?? "",
            field.SelectedItems?.ToArray() ?? [],
            field.Grouping,
            field.GroupStart,
            field.GroupEnd,
            field.GroupInterval);

    private static PivotDataFieldSummary CapturePivotDataFieldSummary(PivotDataFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.Name,
            field.SummaryFunction,
            field.NumberFormatId,
            field.CalculatedFieldName ?? "",
            field.ShowValuesAs,
            field.BaseFieldIndex,
            field.BaseItem ?? "",
            field.NumberFormatCode ?? "");

    private static ChartRangeSummary ToRangeSummary(GridRange range) =>
        new(
            range.Start.Row,
            range.Start.Col,
            range.End.Row,
            range.End.Col);

    private static RepeatRangeSummary ToRepeatRangeSummary(WorksheetRepeatRange range) =>
        new(range.Start, range.End);

    private static HeaderFooterSummary CaptureHeaderFooterSummary(WorksheetHeaderFooter value) =>
        new(
            NormalizeHeaderFooterText(value.Left),
            NormalizeHeaderFooterText(value.Center),
            NormalizeHeaderFooterText(value.Right));

    private static HeaderFooterPictureSetSummary CaptureHeaderFooterPictureSetSummary(WorksheetHeaderFooterPictureSet value) =>
        new(
            CaptureHeaderFooterPictureSummary(value.Left),
            CaptureHeaderFooterPictureSummary(value.Center),
            CaptureHeaderFooterPictureSummary(value.Right));

    private static HeaderFooterPictureSummary? CaptureHeaderFooterPictureSummary(WorksheetHeaderFooterPicture? picture) =>
        picture is null
            ? null
            : new HeaderFooterPictureSummary(
                picture.ContentType,
                picture.FileName ?? "",
                picture.ImageBytes.Length,
                picture.Width,
                picture.Height);

    private static string NormalizeHeaderFooterText(string text) =>
        text
            .Replace("&[Page]", "&P", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", "&N", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", "&D", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", "&T", StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", "&F", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", "&A", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", "&Z", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return "";

        try
        {
            return XElement.Parse(xml).ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return xml.Trim();
        }
    }

    private static TextBoxSummary CaptureTextBoxSummary(TextBoxModel textBox) =>
        new(
            textBox.Name ?? "",
            textBox.Text,
            textBox.Title ?? "",
            textBox.AltText ?? "",
            textBox.Anchor.Row,
            textBox.Anchor.Col,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.IsVisible,
            textBox.FillColor,
            textBox.OutlineColor,
            textBox.FillThemeColor,
            textBox.OutlineThemeColor);

    private static DrawingShapeSummary CaptureDrawingShapeSummary(DrawingShapeModel shape) =>
        new(
            shape.Name ?? "",
            shape.Kind,
            shape.Title ?? "",
            shape.AltText ?? "",
            shape.Anchor.Row,
            shape.Anchor.Col,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.IsVisible,
            shape.FillColor,
            shape.OutlineColor,
            shape.GradientFillEndColor,
            shape.FillThemeColor,
            shape.OutlineThemeColor,
            shape.HasShadowEffect);

    private static PictureSummary CapturePictureSummary(PictureModel picture) =>
        new(
            picture.Name ?? "",
            picture.Kind,
            picture.Title ?? "",
            picture.AltText ?? "",
            picture.Anchor.Row,
            picture.Anchor.Col,
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.IsVisible,
            picture.ContentType ?? "",
            picture.ImageBytes?.Length ?? 0,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.IsLinkedToSourceRange,
            picture.LinkedSourceRange is { } linkedSourceRange ? ToRangeSummary(linkedSourceRange) : null,
            picture.LinkedSourceSheetName ?? "",
            picture.SourceRowCount,
            picture.SourceColumnCount,
            picture.Cells
                .OrderBy(cell => cell.RowOffset)
                .ThenBy(cell => cell.ColumnOffset)
                .Select(cell => new PictureCellSummary(cell.RowOffset, cell.ColumnOffset, cell.Text))
                .ToArray());

    private static ConditionalFormatSummary CaptureConditionalFormatSummary(ConditionalFormat format) =>
        new(
            format.RuleType,
            format.Priority,
            format.Operator,
            format.Value1 ?? "",
            format.Value2 ?? "",
            CaptureStyleSummary(format.FormatIfTrue),
            format.MinColor,
            format.MidColor,
            format.MaxColor,
            format.UseThreeColorScale,
            format.MinThresholdType,
            format.MinThresholdValue ?? "",
            format.MidThresholdType,
            format.MidThresholdValue ?? "",
            format.MaxThresholdType,
            format.MaxThresholdValue ?? "",
            format.DataBarColor,
            format.DataBarMinThresholdType,
            format.DataBarMinThresholdValue ?? "",
            format.DataBarMaxThresholdType,
            format.DataBarMaxThresholdValue ?? "",
            format.DataBarShowValue,
            format.DataBarMinLength,
            format.DataBarMaxLength,
            format.DataBarGradient,
            format.DataBarBorder,
            format.DataBarAxisPosition ?? "",
            format.DataBarAxisColor,
            format.DataBarNegativeFillColor,
            format.DataBarNegativeBorderColor,
            format.AboveAverage,
            format.FormulaText ?? "",
            format.IconSetStyle ?? "",
            format.IconSetShowValue,
            format.IconSetReverse,
            format.IconSetThresholds.Select(threshold => new ConditionalFormatThresholdSummary(threshold.Type, threshold.Value ?? "")).ToArray(),
            format.TopBottomRank,
            format.TopBottomPercent,
            format.TextRuleText ?? "",
            format.DateOccurringPeriod ?? "",
            format.StopIfTrue,
            ToRangeSummary(format.AppliesTo));

    private static CellStyleSummary? CaptureStyleSummary(CellStyle? style) =>
        style is null
            ? null
            : new(
                style.FontName,
                style.FontSize,
                style.Bold,
                style.Italic,
                style.Underline,
                style.Strikethrough,
                style.FontColor,
                style.FillColor,
                NormalizeFillPatternStyle(style),
                style.FillPatternColor,
                style.NumberFormat);

    private static CellFillPatternStyle NormalizeFillPatternStyle(CellStyle style) =>
        style.FillColor.HasValue && style.FillPatternStyle == CellFillPatternStyle.None
            ? CellFillPatternStyle.Solid
            : style.FillPatternStyle;

    private static void AssertExpectedPublicPackageTags(ManifestRow row, Stream package)
    {
        if (row.SourceType != "public")
            return;

        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!HasExpectedPublicPackageTags(row))
            return;

        var originalPosition = package.CanSeek ? package.Position : 0;
        if (package.CanSeek)
            package.Position = 0;

        try
        {
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            if (tags.Contains("styles") || tags.Contains("formatting"))
                archive.GetEntry("xl/styles.xml").Should().NotBeNull(row.Id);

            if (tags.Contains("hyperlinks"))
                PublicWorksheetElements(archive, "hyperlink").Should().NotBeEmpty(row.Id);

            if (tags.Contains("merged-cells"))
                PublicWorksheetElements(archive, "mergeCell").Should().NotBeEmpty(row.Id);

            if (tags.Contains("inline-strings"))
                PublicWorksheetCells(archive)
                    .Any(cell =>
                        string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal) ||
                        cell.Element(WorksheetNs + "is") is not null)
                    .Should()
                    .BeTrue(row.Id);

            if (tags.Contains("cell-types"))
                PublicWorksheetCells(archive)
                    .Select(cell => cell.Attribute("t")?.Value ?? "n")
                    .Distinct(StringComparer.Ordinal)
                    .Should()
                    .HaveCountGreaterThanOrEqualTo(3, row.Id);

            if (tags.Contains("sheet-names") && tags.Contains("boundary"))
                PublicWorkbookSheetNames(archive)
                    .Should()
                    .Contain(name => name.Length == 31, row.Id);

            if (tags.Contains("unsupported-sheet-types"))
                archive.Entries.Should().Contain(entry => entry.FullName.StartsWith("xl/chartsheets/", StringComparison.Ordinal), row.Id);
        }
        finally
        {
            if (package.CanSeek)
                package.Position = originalPosition;
        }
    }

    private static IReadOnlyList<XElement> PublicWorksheetElements(ZipArchive archive, string localName)
    {
        return PublicWorksheetXmlDocuments(archive)
            .SelectMany(document => document.Descendants(WorksheetNs + localName))
            .ToArray();
    }

    private static IReadOnlyList<XElement> PublicWorksheetCells(ZipArchive archive) =>
        PublicWorksheetElements(archive, "c");

    private static IReadOnlyList<XDocument> PublicWorksheetXmlDocuments(ZipArchive archive)
    {
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(LoadPackageXml)
            .ToArray();
    }

    private static IReadOnlyList<string> PublicWorkbookSheetNames(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        workbookEntry.Should().NotBeNull("public workbook packages should contain workbook.xml");

        return LoadPackageXml(workbookEntry!)
            .Descendants(WorksheetNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? "")
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly string[] ModeledIgnoredErrorFlags =
    [
        "numberStoredAsText",
        "evalError",
        "formula",
        "emptyCellReference"
    ];

    private static bool HasExpectedPublicPackageTags(ManifestRow row)
    {
        if (row.SourceType != "public")
            return false;

        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tags.Contains("styles") ||
               tags.Contains("formatting") ||
               tags.Contains("hyperlinks") ||
               tags.Contains("merged-cells") ||
               tags.Contains("inline-strings") ||
               tags.Contains("cell-types") ||
               (tags.Contains("sheet-names") && tags.Contains("boundary")) ||
               tags.Contains("unsupported-sheet-types");
    }

    private static DataValidationSummary CaptureDataValidationSummary(DataValidation validation) =>
        new(
            validation.Type,
            validation.Operator,
            validation.Formula1 ?? "",
            validation.Formula2 ?? "",
            validation.AllowBlank,
            validation.ShowDropdown,
            validation.AlertStyle,
            validation.ShowInputMessage,
            validation.ShowErrorMessage,
            validation.ErrorTitle ?? "",
            validation.ErrorMessage ?? "",
            validation.PromptTitle ?? "",
            validation.PromptMessage ?? "",
            ToRangeSummary(validation.AppliesTo),
            validation.AdditionalRanges.Select(ToRangeSummary).ToArray());

    private static WorkbookSummary CapturePublicComparableSummary(Workbook workbook)
    {
        var summary = CaptureSummary(workbook);
        return summary with
        {
            Sheets = summary.Sheets
                .Select(sheet => sheet with
                {
                    Cells = [],
                    HeaderFooterAlignWithMargins = true,
                    HeaderFooterScaleWithDocument = true,
                    DefaultColumnWidth = 0,
                    DefaultRowHeight = 0,
                    ColumnWidths = [],
                    RowHeights = [],
                    StyleOnlyCells = [],
                    StyleOnlyCellCount = 0
                })
                .ToArray()
        };
    }

    private static PackagePartSummary CapturePackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return new PackagePartSummary(
                archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .Where(IsFidelityCriticalPart)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipTargets)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipDetails)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReadCriticalContentTypeOverrides(archive)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static DataValidationPackageXmlSummary CaptureDataValidationPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-dv-count-package-003 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var container = worksheetXml.Root!.Element(sheetNs + "dataValidations");
            container.Should().NotBeNull("generated-dv-count-package-003 must include a dataValidations container");

            return new DataValidationPackageXmlSummary(
                container!.Attribute("count")?.Value ?? "",
                container.Elements(sheetNs + "dataValidation")
                    .Select(element =>
                    {
                        var type = element.Attribute("type")?.Value ?? "";
                        return new DataValidationRuleXmlSummary(
                            type,
                            NormalizeDataValidationOperator(type, element.Attribute("operator")?.Value ?? ""),
                            element.Attribute("sqref")?.Value ?? "",
                            NormalizeDataValidationFormula(type, element.Element(sheetNs + "formula1")?.Value ?? ""),
                            element.Element(sheetNs + "formula2")?.Value ?? "");
                    })
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static WorksheetSortFilterPackageXmlSummary CaptureWorksheetSortFilterPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-worksheet-sort-state-001 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var autoFilter = worksheetXml.Root!.Element(sheetNs + "autoFilter");
            var sortState = worksheetXml.Root.Element(sheetNs + "sortState");

            autoFilter.Should().NotBeNull("generated-worksheet-sort-state-001 must include worksheet AutoFilter metadata");
            sortState.Should().NotBeNull("generated-worksheet-sort-state-001 must include worksheet sortState metadata");

            return new WorksheetSortFilterPackageXmlSummary(
                CaptureXmlElementSummary(autoFilter!),
                CaptureXmlElementSummary(sortState!));
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static WorksheetIgnoredErrorsPackageXmlSummary CaptureWorksheetIgnoredErrorsPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-worksheet-ignored-errors-001 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var container = worksheetXml.Root!.Element(sheetNs + "ignoredErrors");
            container.Should().NotBeNull("generated-worksheet-ignored-errors-001 must include worksheet ignoredErrors metadata");

            return new WorksheetIgnoredErrorsPackageXmlSummary(
                container!.Attributes()
                    .Where(attribute => !attribute.IsNamespaceDeclaration)
                    .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
                    .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
                    .ToArray(),
                container.Elements(sheetNs + "ignoredError")
                    .Select(element => new WorksheetIgnoredErrorXmlSummary(
                        element.Attribute("sqref")?.Value ?? "",
                        HasModeledIgnoredErrorFlag(element),
                        CaptureRetainedIgnoredErrorAttributes(element)))
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static bool HasModeledIgnoredErrorFlag(XElement ignoredError) =>
        ModeledIgnoredErrorFlags.Any(flag => IsTruthyXmlBoolean(ignoredError.Attribute(flag)?.Value));

    private static IReadOnlyList<NativeAttributeSummary> CaptureRetainedIgnoredErrorAttributes(XElement ignoredError) =>
        ignoredError.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration &&
                !string.Equals(attribute.Name.LocalName, "sqref", StringComparison.Ordinal) &&
                !ModeledIgnoredErrorFlags.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
            .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
            .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
            .ToArray();

    private static bool IsTruthyXmlBoolean(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static WorksheetElementXmlSummary CaptureXmlElementSummary(XElement element) =>
        new(
            element.Name.ToString(),
            element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
                .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
                .ToArray(),
            element.Elements().Any() ? "" : element.Value.Trim(),
            element.Elements()
                .Select(CaptureXmlElementSummary)
                .ToArray());

    private static string NormalizeDataValidationOperator(string type, string op)
    {
        if (type is "list" or "custom" && string.Equals(op, "between", StringComparison.OrdinalIgnoreCase))
            return "";

        if (!string.IsNullOrWhiteSpace(op))
            return op;

        return type is "whole" or "decimal" or "date" or "time" or "textLength" ? "between" : "";
    }

    private static string NormalizeDataValidationFormula(string type, string formula)
    {
        if (type != "list" || formula.Length < 2 || formula[0] != '"' || formula[^1] != '"')
            return formula;

        return formula[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
    }

    private static void AssertPackageHealth(Stream stream, string because)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entries = archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .ToArray();
                entries.Should().OnlyHaveUniqueItems(because);

                // OPC part names are compared case-insensitively, so two names differing only by case
                // (e.g. ClosedXML's xl/drawings/vmldrawing2.vml vs Excel's xl/drawings/vmlDrawing2.vml)
                // make the package unreadable in Excel even though the zip entries are distinct.
                entries.Select(name => name.ToLowerInvariant())
                    .Should().OnlyHaveUniqueItems($"{because}: OPC part names must be unique case-insensitively");

                var entrySet = entries.ToHashSet(StringComparer.OrdinalIgnoreCase);
                archive.GetEntry("[Content_Types].xml").Should().NotBeNull(because);
                foreach (var xmlEntry in archive.Entries.Where(entry =>
                             entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                             entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    using var xmlStream = xmlEntry.Open();
                    var load = () => XDocument.Load(xmlStream);
                    load.Should().NotThrow($"{because}: {xmlEntry.FullName} should be parseable XML");
                }

                foreach (var relsEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    var sourcePart = RelationshipSourcePart(relsEntry.FullName.Replace('\\', '/'));
                    var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
                    var relsXml = LoadPackageXml(relsEntry);
                    XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                    foreach (var relationship in relsXml.Root?.Elements(relNs + "Relationship") ?? [])
                    {
                        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var target = relationship.Attribute("Target")?.Value;
                        if (string.IsNullOrWhiteSpace(target) || target.StartsWith("/", StringComparison.Ordinal))
                            continue;

                        target = Uri.UnescapeDataString(target);
                        var resolved = NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
                            ? target
                            : $"{sourceDirectory}/{target}");
                        entrySet.Should().Contain(resolved, $"{because}: {relsEntry.FullName} relationship target should exist");
                    }
                }
            }

            // The definitive check: the Open XML SDK (same OPC layer Excel uses) must be able to open
            // the package. A "Format error in package" here is exactly what makes Excel refuse the file
            // and strip features on repair.
            stream.Position = 0;
            var openPackage = () =>
            {
                using var document = SpreadsheetDocument.Open(stream, isEditable: false);
                _ = document.WorkbookPart;
            };
            openPackage.Should().NotThrow($"{because}: saved package must be OPC-readable (Excel can open it)");
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static string RelationshipSourcePart(string relsPath)
    {
        if (string.Equals(relsPath, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var relsMarker = "/_rels/";
        var markerIndex = relsPath.IndexOf(relsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !relsPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var prefix = relsPath[..markerIndex];
        var fileName = relsPath[(markerIndex + relsMarker.Length)..^".rels".Length];
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static string NormalizePackagePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }

    private static bool IsFidelityCriticalPart(string path) =>
        path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/theme/theme1.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/styles.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/pivot", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/slicer", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/timeline", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/calcChain.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/connections.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/query", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/queries/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/model/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/datamodel/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/powerpivot/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/activeX/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/webextensions/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/diagrams/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/macroSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadRelationshipTargets(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Select(rel => rel.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Where(target => !target!.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(target => $"{relsEntry.FullName.Replace('\\', '/')}=>{target!.Replace('\\', '/')}")
            .ToArray() ?? [];
    }

    private static IEnumerable<string> ReadRelationshipDetails(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Where(rel => !string.IsNullOrWhiteSpace(rel.Attribute("Target")?.Value))
            .Where(rel => !rel.Attribute("Target")!.Value.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(rel =>
            {
                var target = NormalizeRelationshipDetailTarget(
                    relsEntry.FullName.Replace('\\', '/'),
                    rel.Attribute("Target")!.Value,
                    rel.Attribute("TargetMode")?.Value);
                var type = rel.Attribute("Type")?.Value ?? "";
                var targetMode = rel.Attribute("TargetMode")?.Value ?? "";
                return $"{relsEntry.FullName.Replace('\\', '/')}=>{target}|type={type}|mode={targetMode}";
            })
            .ToArray() ?? [];
    }

    private static string NormalizeRelationshipDetailTarget(string relsPath, string target, string? targetMode)
    {
        target = target.Replace('\\', '/');
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return target;

        if (target.StartsWith("/", StringComparison.Ordinal))
            return NormalizePackagePath(target);

        var sourcePart = RelationshipSourcePart(relsPath);
        var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
        return NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
            ? target
            : $"{sourceDirectory}/{target}");
    }

    private static IEnumerable<string> ReadCriticalContentTypeOverrides(ZipArchive archive)
    {
        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null)
            return [];

        XDocument contentTypesXml;
        using (var stream = entry.Open())
            contentTypesXml = XDocument.Load(stream);

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        return contentTypesXml.Root?
            .Elements(contentTypeNs + "Override")
            .Select(element => new
            {
                PartName = element.Attribute("PartName")?.Value,
                ContentType = element.Attribute("ContentType")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PartName))
            .Select(item => new
            {
                PartName = item.PartName!.TrimStart('/').Replace('\\', '/'),
                ContentType = item.ContentType ?? ""
            })
            .Where(item => IsFidelityCriticalPart(item.PartName))
            .Select(item => $"/{item.PartName}=>{item.ContentType}")
            .ToArray() ?? [];
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WritePackageEntry(ZipArchive archive, string entryName, string content)
    {
        try
        {
            archive.GetEntry(entryName)?.Delete();
        }
        catch (NotSupportedException)
        {
            // ZipArchiveMode.Create does not allow entry lookup.
        }

        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed record WorkbookSummary(
        int SheetCount,
        IReadOnlyList<NamedRangeSummary> NamedRanges,
        int NamedRangeCount,
        bool IsStructureProtected,
        string StructureProtectionPassword,
        IReadOnlyList<PivotCacheSummary> PivotCaches,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        IReadOnlyList<PivotTableStyleSummary> PivotTableStyles,
        int PivotTableStyleCount,
        int PivotTableStyleElementCount,
        IReadOnlyList<NumberFormatCatalogSummary> NumberFormatCatalog,
        IReadOnlyList<CustomViewSummary> CustomViews,
        int CustomViewCount,
        WorkbookMetadataSummary Metadata,
        WorkbookCalculationSummary Calculation,
        WorkbookThemeSummary Theme,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record WorkbookMetadataSummary(
        IReadOnlyList<SlicerSummary> Slicers,
        IReadOnlyList<TimelineSummary> Timelines,
        IReadOnlyList<ExternalLinkSummary> ExternalLinks,
        IReadOnlyList<WatchedCellSummary> WatchedCells,
        IReadOnlyList<ScenarioSummary> Scenarios);

    private sealed record SlicerSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        IReadOnlyList<string> SelectedItems,
        string PackagePart);

    private sealed record TimelineSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        string StartDate,
        string EndDate,
        string SelectedStartDate,
        string SelectedEndDate,
        string PackagePart);

    private sealed record ExternalLinkSummary(
        string PackagePart,
        string TargetUri,
        string TargetMode);

    private sealed record WatchedCellSummary(
        string SheetName,
        uint Row,
        uint Column);

    private sealed record ScenarioSummary(
        string Name,
        IReadOnlyList<ScenarioCellSummary> ChangingCells);

    private sealed record ScenarioCellSummary(
        string SheetName,
        uint Row,
        uint Column,
        ScalarValueSummary Value);

    private sealed record WorkbookCalculationSummary(
        WorkbookCalculationMode Mode,
        bool FullCalculationOnLoad,
        bool ForceFullCalculation,
        bool IterativeCalculation,
        int? MaxIterations,
        double? MaxChange);

    private sealed record WorkbookThemeSummary(
        string Name,
        string MajorFontName,
        string MinorFontName,
        string EffectsName,
        IReadOnlyList<ThemeColorSummary> Colors);

    private sealed record ThemeColorSummary(
        WorkbookThemeColorSlot Slot,
        string Color);

    private sealed record NamedRangeSummary(
        string Name,
        string Scope,
        string Comment,
        ChartRangeSummary Range);

    private sealed record SheetSummary(
        string Name,
        IReadOnlyList<CellSummary> Cells,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        IReadOnlyList<DataValidationSummary> DataValidations,
        int DataValidationCount,
        IReadOnlyList<ConditionalFormatSummary> ConditionalFormats,
        int ConditionalFormatCount,
        int ColorScaleConditionalFormatCount,
        int DataBarConditionalFormatCount,
        int IconSetConditionalFormatCount,
        IReadOnlyList<CommentSummary> Comments,
        int CommentCount,
        IReadOnlyList<HyperlinkSummary> Hyperlinks,
        int HyperlinkCount,
        IReadOnlyList<ChartSummary> Charts,
        int ChartCount,
        IReadOnlyList<PivotTableSummary> PivotTables,
        int PivotTableCount,
        int PivotTableFieldCount,
        IReadOnlyList<StructuredTableSummary> StructuredTables,
        int StructuredTableCount,
        int StructuredTableColumnCount,
        IReadOnlyList<SparklineSummary> Sparklines,
        int SparklineCount,
        IReadOnlyList<TextBoxSummary> TextBoxes,
        int TextBoxCount,
        IReadOnlyList<DrawingShapeSummary> DrawingShapes,
        int DrawingShapeCount,
        IReadOnlyList<PictureSummary> Pictures,
        int PictureCount,
        BackgroundImageSummary? BackgroundImage,
        bool HasBackgroundImage,
        bool IsProtected,
        string ProtectionPassword,
        IReadOnlyList<ChartRangeSummary> AllowEditRanges,
        int AllowEditRangeCount,
        ChartRangeSummary? PrintArea,
        bool HasPrintArea,
        RepeatRangeSummary? PrintTitleRows,
        bool HasPrintTitleRows,
        RepeatRangeSummary? PrintTitleColumns,
        bool HasPrintTitleColumns,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageMargins PageMargins,
        double HeaderMargin,
        double FooterMargin,
        WorksheetScaleToFit ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        HeaderFooterSummary PageHeader,
        bool HasPageHeader,
        HeaderFooterSummary PageFooter,
        bool HasPageFooter,
        HeaderFooterSummary FirstPageHeader,
        HeaderFooterSummary FirstPageFooter,
        HeaderFooterSummary EvenPageHeader,
        HeaderFooterSummary EvenPageFooter,
        bool DifferentFirstPageHeaderFooter,
        bool DifferentOddEvenHeaderFooter,
        bool HeaderFooterScaleWithDocument,
        bool HeaderFooterAlignWithMargins,
        HeaderFooterPictureSetSummary PageHeaderPictures,
        HeaderFooterPictureSetSummary PageFooterPictures,
        HeaderFooterPictureSetSummary FirstPageHeaderPictures,
        HeaderFooterPictureSetSummary FirstPageFooterPictures,
        HeaderFooterPictureSetSummary EvenPageHeaderPictures,
        HeaderFooterPictureSetSummary EvenPageFooterPictures,
        bool CenterHorizontallyOnPage,
        bool CenterVerticallyOnPage,
        WorksheetPageOrder PageOrder,
        int? FirstPageNumber,
        bool PrintBlackAndWhite,
        bool PrintDraftQuality,
        int? PrintQualityDpi,
        WorksheetPrintErrorValue PrintErrorValue,
        WorksheetPrintComments PrintComments,
        double DefaultColumnWidth,
        double DefaultRowHeight,
        IReadOnlyList<DimensionSummary> ColumnWidths,
        IReadOnlyList<DimensionSummary> RowHeights,
        IReadOnlyList<uint> RowPageBreaks,
        int RowPageBreakCount,
        IReadOnlyList<uint> ColumnPageBreaks,
        int ColumnPageBreakCount,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        WorksheetViewMode ViewMode,
        uint? ViewTopRow,
        uint? ViewLeftColumn,
        uint? ActiveRow,
        uint? ActiveColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool FullCalculationOnLoad,
        PhoneticSummary? PhoneticProperties,
        bool IsHidden,
        bool IsVeryHidden,
        string CodeName,
        string TabColor,
        IReadOnlyList<WorksheetCustomPropertySummary> CustomProperties,
        IReadOnlyList<uint> HiddenRows,
        int HiddenRowCount,
        IReadOnlyList<uint> FilterHiddenRows,
        int FilterHiddenRowCount,
        IReadOnlyList<uint> HiddenColumns,
        int HiddenColumnCount,
        IReadOnlyList<OutlineLevelSummary> RowOutlineLevels,
        int RowOutlineLevelCount,
        IReadOnlyList<OutlineLevelSummary> ColumnOutlineLevels,
        int ColumnOutlineLevelCount,
        IReadOnlyList<uint> GroupHiddenRows,
        int GroupHiddenRowCount,
        IReadOnlyList<uint> GroupHiddenColumns,
        int GroupHiddenColumnCount,
        IReadOnlyList<StyleOnlyCellSummary> StyleOnlyCells,
        int StyleOnlyCellCount);

    private sealed record CellSummary(
        uint Row,
        uint Column,
        ScalarValueSummary Value,
        string FormulaText,
        bool IgnoreFormulaError,
        CellStyleSummary? Style);

    private sealed record FormulaCellSummary(
        string SheetName,
        uint Row,
        uint Column,
        string FormulaText,
        ScalarValueSummary CachedValue);

    private sealed record ScalarValueSummary(string Kind, string Value);

    private sealed record CustomViewSummary(
        string Name,
        bool IncludePrintSettings,
        bool IncludeHiddenRowsColumnsAndFilterSettings,
        IReadOnlyList<CustomViewSheetSummary> Sheets);

    private sealed record CustomViewSheetSummary(
        string SheetName,
        WorksheetViewMode ViewMode,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas);

    private sealed record CommentSummary(uint Row, uint Column, string Text);

    private sealed record HyperlinkSummary(
        uint Row,
        uint Column,
        string Target,
        HyperlinkTargetKind LinkType,
        string ScreenTip,
        string Bookmark);

    private sealed record OutlineLevelSummary(uint Index, int Level);

    private sealed record StyleOnlyCellSummary(uint Row, uint Column, CellStyleSummary? Style);

    private sealed record DimensionSummary(uint Index, double Value);

    private sealed record PhoneticSummary(string FontId, string Type, string Alignment);

    private sealed record WorksheetCustomPropertySummary(string Name, int Id);

    private sealed record RepeatRangeSummary(uint Start, uint End);

    private sealed record BackgroundImageSummary(string ContentType, string FileName, int ImageByteCount);

    private sealed record HeaderFooterSummary(string Left, string Center, string Right)
    {
        public static HeaderFooterSummary Empty { get; } = new("", "", "");
    }

    private sealed record HeaderFooterPictureSetSummary(
        HeaderFooterPictureSummary? Left,
        HeaderFooterPictureSummary? Center,
        HeaderFooterPictureSummary? Right);

    private sealed record HeaderFooterPictureSummary(
        string ContentType,
        string FileName,
        int ByteLength,
        double Width,
        double Height);

    private sealed record ChartSummary(
        ChartType Type,
        string Title,
        string XAxisTitle,
        string YAxisTitle,
        ChartVisualSummary Visual,
        ChartAxisSummary XAxis,
        ChartAxisSummary YAxis,
        bool ShowLegend,
        bool IsPivotChart,
        int? PivotSourceFormatId,
        bool Uses1904DateSystem,
        string Language,
        int? ChartStyleId,
        bool RoundedCorners,
        ChartBlankDisplayMode BlankDisplayMode,
        bool ShowDataLabelsOverMaximum,
        bool AutoTitleDeleted,
        bool ShowDataInHiddenRowsAndColumns,
        ChartProtectionSummary? Protection,
        ChartPrintSettingsSummary? PrintSettings,
        ChartColorMapSummary? ColorMapOverride,
        ChartExternalDataSummary? ExternalData,
        ChartManualLayoutSummary? PlotAreaLayout,
        ChartManualLayoutSummary? LegendLayout,
        ChartLegendPosition LegendPosition,
        bool LegendOverlay,
        bool ShowDataLabels,
        bool ShowDataLabelValue,
        bool ShowDataLabelLegendKey,
        bool ShowDataLabelBubbleSize,
        bool ShowDataLabelCategoryName,
        bool ShowDataLabelSeriesName,
        bool ShowDataLabelPercentage,
        ChartDataLabelPosition DataLabelPosition,
        ChartDataLabelSeparator DataLabelSeparator,
        ChartDataLabelNumberFormat DataLabelNumberFormat,
        bool ShowDataLabelCallouts,
        string DataLabelFillColor,
        WorkbookThemeColorReference? DataLabelFillThemeColor,
        string DataLabelBorderColor,
        WorkbookThemeColorReference? DataLabelBorderThemeColor,
        string DataLabelTextColor,
        WorkbookThemeColorReference? DataLabelTextThemeColor,
        double DataLabelBorderThickness,
        double DataLabelFontSize,
        double DataLabelAngle,
        int? BarGapWidth,
        int? BarOverlap,
        bool? VaryColorsByPoint,
        int BubbleScale,
        bool ShowNegativeBubbles,
        ChartBubbleSizeRepresents BubbleSizeRepresents,
        ChartTrendlineSummary Trendline,
        ChartErrorBarSummary ErrorBars,
        ChartGuideLineSummary DropLines,
        StockChartSubtype StockSubtype,
        ChartGuideLineSummary HighLowLines,
        ChartGuideLineSummary SeriesLines,
        ChartUpDownBarsSummary UpDownBars,
        ChartDataTableSummary? DataTable,
        Chart3DViewSummary? ThreeDView,
        ChartSurfaceFormatSummary? FloorFormat,
        ChartSurfaceFormatSummary? SideWallFormat,
        ChartSurfaceFormatSummary? BackWallFormat,
        ChartRangeSummary DataRange);

    private sealed record ChartVisualSummary(
        string ChartTitleTextColor,
        WorkbookThemeColorReference? ChartTitleTextThemeColor,
        double ChartTitleFontSize,
        string AxisTitleTextColor,
        WorkbookThemeColorReference? AxisTitleTextThemeColor,
        double AxisTitleFontSize,
        string ChartAreaFillColor,
        WorkbookThemeColorReference? ChartAreaFillThemeColor,
        string PlotAreaFillColor,
        WorkbookThemeColorReference? PlotAreaFillThemeColor,
        string PlotAreaBorderColor,
        WorkbookThemeColorReference? PlotAreaBorderThemeColor,
        double PlotAreaBorderThickness,
        string LegendTextColor,
        WorkbookThemeColorReference? LegendTextThemeColor,
        string LegendFillColor,
        WorkbookThemeColorReference? LegendFillThemeColor,
        string LegendBorderColor,
        WorkbookThemeColorReference? LegendBorderThemeColor,
        double LegendBorderThickness,
        double LegendFontSize);

    private sealed record ChartAxisSummary(
        double? Minimum,
        double? Maximum,
        double? MajorUnit,
        double? MinorUnit,
        bool LogScale,
        ChartDataLabelNumberFormat NumberFormat,
        bool ShowMajorGridlines,
        bool ShowMinorGridlines,
        bool IsDateAxis,
        string MajorGridlineColor,
        string MinorGridlineColor,
        double GridlineThickness,
        ChartAxisTickStyle MajorTickStyle,
        ChartAxisTickStyle MinorTickStyle,
        bool ShowLabels,
        string LabelTextColor,
        WorkbookThemeColorReference? LabelTextThemeColor,
        double LabelFontSize,
        double LabelAngle,
        int LabelSkip,
        int TickMarkSkip,
        int LabelOffset,
        string LineColor,
        double LineThickness);

    private sealed record ChartTrendlineSummary(
        bool Show,
        ChartTrendlineType Type,
        int Period,
        int Order,
        bool ShowEquation,
        bool ShowRSquared,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartErrorBarSummary(
        bool Show,
        ChartErrorBarKind Kind,
        ChartErrorBarDirection Direction,
        double Value,
        bool EndCaps,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartGuideLineSummary(
        bool Show,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartUpDownBarsSummary(
        bool Show,
        int? GapWidth,
        ChartBarShapeSummary UpBars,
        ChartBarShapeSummary DownBars);

    private sealed record ChartBarShapeSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartColorMapSummary(
        bool UseMasterColorMapping,
        IReadOnlyList<ChartColorMapEntrySummary> OverrideMappings);

    private sealed record ChartColorMapEntrySummary(string Key, string Value);

    private sealed record ChartExternalDataSummary(
        string RelationshipId,
        string RelationshipType,
        string Target,
        string TargetMode,
        bool? AutoUpdate);

    private sealed record ChartManualLayoutSummary(
        string LayoutTarget,
        string XMode,
        string YMode,
        string WidthMode,
        string HeightMode,
        double? X,
        double? Y,
        double? Width,
        double? Height);

    private sealed record ChartDataTableSummary(
        bool? ShowHorizontalBorder,
        bool? ShowVerticalBorder,
        bool? ShowOutline,
        bool? ShowLegendKeys);

    private sealed record Chart3DViewSummary(
        int? RotationX,
        int? HeightPercent,
        int? RotationY,
        int? DepthPercent,
        bool? RightAngleAxes,
        int? Perspective);

    private sealed record ChartSurfaceFormatSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartProtectionSummary(
        bool? ChartObject,
        bool? Data,
        bool? Formatting,
        bool? Selection,
        bool? UserInterface);

    private sealed record ChartPrintSettingsSummary(
        ChartPageMarginsSummary? PageMargins,
        ChartPageSetupSummary? PageSetup);

    private sealed record ChartPageMarginsSummary(
        double? Left,
        double? Right,
        double? Top,
        double? Bottom,
        double? Header,
        double? Footer);

    private sealed record ChartPageSetupSummary(
        string PaperSize,
        string Orientation,
        int? Copies,
        bool? BlackAndWhite,
        bool? Draft);

    private sealed record ChartRangeSummary(
        uint StartRow,
        uint StartColumn,
        uint EndRow,
        uint EndColumn);

    private sealed record StructuredTableSummary(
        string Name,
        string DisplayName,
        string StyleName,
        bool HasAutoFilter,
        bool TotalsRowShown,
        bool ShowFirstColumn,
        bool ShowLastColumn,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        string NativeSortStateXml,
        ChartRangeSummary Range,
        IReadOnlyList<StructuredTableColumnSummary> Columns,
        IReadOnlyList<StructuredTableFilterColumnSummary> FilterColumns);

    private sealed record StructuredTableColumnSummary(
        int Id,
        string Name,
        string TotalsRowLabel,
        string TotalsRowFunction,
        string CalculatedColumnFormula,
        string TotalsRowFormula);

    private sealed record StructuredTableFilterColumnSummary(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<StructuredTableCustomFilterSummary> CustomFilters,
        bool CustomFiltersAnd,
        string CustomFiltersAndRaw,
        IReadOnlyList<NativeAttributeSummary> NativeCustomFiltersAttributes,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyList<NativeAttributeSummary> NativeAttributes);

    private sealed record StructuredTableCustomFilterSummary(
        string Operator,
        string Value,
        IReadOnlyList<NativeAttributeSummary> NativeAttributes);

    private sealed record NativeAttributeSummary(string Name, string Value);

    private sealed record PivotTableSummary(
        string Name,
        int CacheId,
        ChartRangeSummary SourceRange,
        ChartRangeSummary TargetRange,
        bool DataOnRows,
        int FirstHeaderRow,
        int FirstDataRow,
        int FirstDataColumn,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool ShowItemsWithNoDataOnRows,
        bool ShowItemsWithNoDataOnColumns,
        bool PageOverThenDown,
        int PageWrap,
        string EmptyValueText,
        bool ApplyNumberFormats,
        bool ApplyBorderFormats,
        bool ApplyFontFormats,
        bool ApplyPatternFormats,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool ShowExpandCollapseButtons,
        bool EnableDrill,
        bool AsteriskTotals,
        bool MultipleFieldFilters,
        bool EnableFieldDialog,
        bool EnableFieldProperties,
        bool EnableDataValueEditing,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string AltTextTitle,
        string AltTextDescription,
        string DataCaption,
        string GrandTotalCaption,
        string MissingCaption,
        string ErrorCaption,
        IReadOnlyList<PivotFieldSummary> RowFields,
        IReadOnlyList<PivotFieldSummary> ColumnFields,
        IReadOnlyList<PivotFieldSummary> PageFields,
        IReadOnlyList<PivotDataFieldSummary> DataFields);

    private sealed record PivotCacheSummary(
        int CacheId,
        PivotCacheSourceType SourceType,
        string SourceSheetName,
        string SourceReference,
        string SourceTableName,
        int? ConnectionId,
        bool IsOlap,
        bool RefreshOnLoad,
        bool SaveData,
        bool EnableRefresh,
        bool PreserveSourceSortFilter,
        int? MissingItemsLimit,
        int? RecordCount,
        int? CreatedVersion,
        int? MinRefreshableVersion,
        int? RefreshedVersion,
        string RefreshedBy,
        string RefreshedDateIso,
        IReadOnlyList<PivotCacheFieldSummary> Fields);

    private sealed record PivotCacheFieldSummary(
        string Name,
        int? NumberFormatId,
        int? SharedItemCount,
        bool ContainsBlank,
        bool ContainsString,
        bool ContainsNumber,
        bool ContainsDate,
        bool ContainsMixedTypes,
        bool ContainsSemiMixedTypes,
        bool ContainsNonDate,
        bool ContainsInteger,
        bool ContainsLongText,
        double? MinValue,
        double? MaxValue,
        string MinDate,
        string MaxDate,
        IReadOnlyList<string> SharedItems);

    private sealed record PivotFieldSummary(
        int SourceFieldIndex,
        string SelectedItem,
        IReadOnlyList<string> SelectedItems,
        PivotFieldGrouping Grouping,
        double? GroupStart,
        double? GroupEnd,
        double? GroupInterval);

    private sealed record PivotDataFieldSummary(
        int SourceFieldIndex,
        string Name,
        string SummaryFunction,
        int? NumberFormatId,
        string CalculatedFieldName,
        PivotShowValuesAs ShowValuesAs,
        int? BaseFieldIndex,
        string BaseItem,
        string NumberFormatCode);

    private sealed record PivotTableStyleSummary(
        string Name,
        bool AppliesToPivotTables,
        bool AppliesToTables,
        IReadOnlyList<PivotTableStyleElementSummary> Elements);

    private sealed record PivotTableStyleElementSummary(
        string Type,
        int? DifferentialFormatId,
        int? Size);

    private sealed record NumberFormatCatalogSummary(int Id, string FormatCode);

    private sealed record SparklineSummary(
        SparklineKind Kind,
        ChartRangeSummary DataRange,
        uint LocationRow,
        uint LocationColumn);

    private sealed record TextBoxSummary(
        string Name,
        string Text,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor);

    private sealed record DrawingShapeSummary(
        string Name,
        DrawingShapeKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        CellColor? GradientFillEndColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor,
        bool HasShadowEffect);

    private sealed record PictureSummary(
        string Name,
        PictureKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        string ContentType,
        int ImageByteCount,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom,
        bool IsLinkedToSourceRange,
        ChartRangeSummary? LinkedSourceRange,
        string LinkedSourceSheetName,
        uint SourceRowCount,
        uint SourceColumnCount,
        IReadOnlyList<PictureCellSummary> Cells);

    private sealed record PictureCellSummary(uint RowOffset, uint ColumnOffset, string Text);

    private sealed record DataValidationSummary(
        DvType Type,
        DvOperator Operator,
        string Formula1,
        string Formula2,
        bool AllowBlank,
        bool ShowDropdown,
        DvAlertStyle AlertStyle,
        bool ShowInputMessage,
        bool ShowErrorMessage,
        string ErrorTitle,
        string ErrorMessage,
        string PromptTitle,
        string PromptMessage,
        ChartRangeSummary AppliesTo,
        IReadOnlyList<ChartRangeSummary> AdditionalRanges);

    private sealed record ConditionalFormatSummary(
        CfRuleType RuleType,
        int Priority,
        CfOperator Operator,
        string Value1,
        string Value2,
        CellStyleSummary? FormatIfTrue,
        RgbColor MinColor,
        RgbColor MidColor,
        RgbColor MaxColor,
        bool UseThreeColorScale,
        CfThresholdType MinThresholdType,
        string MinThresholdValue,
        CfThresholdType MidThresholdType,
        string MidThresholdValue,
        CfThresholdType MaxThresholdType,
        string MaxThresholdValue,
        RgbColor DataBarColor,
        CfThresholdType DataBarMinThresholdType,
        string DataBarMinThresholdValue,
        CfThresholdType DataBarMaxThresholdType,
        string DataBarMaxThresholdValue,
        bool DataBarShowValue,
        int? DataBarMinLength,
        int? DataBarMaxLength,
        bool DataBarGradient,
        bool DataBarBorder,
        string DataBarAxisPosition,
        RgbColor? DataBarAxisColor,
        RgbColor? DataBarNegativeFillColor,
        RgbColor? DataBarNegativeBorderColor,
        bool AboveAverage,
        string FormulaText,
        string IconSetStyle,
        bool IconSetShowValue,
        bool IconSetReverse,
        IReadOnlyList<ConditionalFormatThresholdSummary> IconSetThresholds,
        int TopBottomRank,
        bool TopBottomPercent,
        string TextRuleText,
        string DateOccurringPeriod,
        bool StopIfTrue,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatThresholdSummary(CfThresholdType Type, string Value);

    private sealed record CellStyleSummary(
        string FontName,
        double FontSize,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        CellColor FontColor,
        CellColor? FillColor,
        CellFillPatternStyle FillPatternStyle,
        CellColor? FillPatternColor,
        string NumberFormat);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets,
        IReadOnlyList<string> CriticalRelationshipDetails,
        IReadOnlyList<string> CriticalContentTypeOverrides);

    private sealed record WorksheetSortFilterPackageXmlSummary(
        WorksheetElementXmlSummary AutoFilter,
        WorksheetElementXmlSummary SortState);

    private sealed record WorksheetIgnoredErrorsPackageXmlSummary(
        IReadOnlyList<NativeAttributeSummary> ContainerAttributes,
        IReadOnlyList<WorksheetIgnoredErrorXmlSummary> Errors);

    private sealed record WorksheetIgnoredErrorXmlSummary(
        string Sqref,
        bool HasModeledIgnoredError,
        IReadOnlyList<NativeAttributeSummary> RetainedNativeAttributes);

    private sealed record WorksheetElementXmlSummary(
        string Name,
        IReadOnlyList<NativeAttributeSummary> Attributes,
        string Text,
        IReadOnlyList<WorksheetElementXmlSummary> Children);

    private sealed record DataValidationPackageXmlSummary(
        string CountAttribute,
        IReadOnlyList<DataValidationRuleXmlSummary> Rules);

    private sealed record DataValidationRuleXmlSummary(
        string Type,
        string Operator,
        string Sqref,
        string Formula1,
        string Formula2);

    // ── NativeXmlPreserveBag test helpers ────────────────────────────────────

    private static string? BagAttr(NativeXmlPreserveBag? bag, string key, string attrName)
    {
        if (bag is null) return null;
        var xml = bag.Get(key);
        if (xml is null) return null;
        try { return XElement.Parse(xml).Attribute(attrName)?.Value; } catch { return null; }
    }

    private static IReadOnlyList<string> BagChildren(NativeXmlPreserveBag? bag, string key)
    {
        if (bag is null) return [];
        var xml = bag.Get(key);
        if (xml is null) return [];
        try
        {
            return XElement.Parse(xml).Elements()
                .Select(e => e.ToString(SaveOptions.DisableFormatting))
                .ToList();
        }
        catch { return []; }
    }
}
