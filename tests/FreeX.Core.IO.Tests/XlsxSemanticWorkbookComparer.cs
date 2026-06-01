using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeX.Core.Model;
using Xunit.Sdk;

namespace FreeX.Core.IO.Tests;

internal static class XlsxSemanticWorkbookComparer
{
    private const double DoubleTolerance = 0.000001d;

    public static XlsxSemanticComparisonResult Compare(Workbook expected, Workbook actual)
    {
        var context = new ComparisonContext(expected, actual);
        context.CompareWorkbook();
        return new XlsxSemanticComparisonResult(context.Differences);
    }

    public static void AssertEquivalent(Workbook expected, Workbook actual)
    {
        var result = Compare(expected, actual);
        if (!result.AreEquivalent)
            throw new XunitException(result.ToAssertionMessage());
    }

    private sealed class ComparisonContext
    {
        private readonly Workbook _expectedWorkbook;
        private readonly Workbook _actualWorkbook;
        private readonly Dictionary<SheetId, string> _expectedSheetNames;
        private readonly Dictionary<SheetId, string> _actualSheetNames;

        public ComparisonContext(Workbook expectedWorkbook, Workbook actualWorkbook)
        {
            _expectedWorkbook = expectedWorkbook;
            _actualWorkbook = actualWorkbook;
            _expectedSheetNames = CreateSheetNameMap(expectedWorkbook);
            _actualSheetNames = CreateSheetNameMap(actualWorkbook);
        }

        public List<string> Differences { get; } = [];

        public void CompareWorkbook()
        {
            CompareValue("Workbook.CalculationMode", _expectedWorkbook.CalculationMode, _actualWorkbook.CalculationMode);
            CompareValue("Workbook.Uses1904DateSystem", _expectedWorkbook.Uses1904DateSystem, _actualWorkbook.Uses1904DateSystem);
            CompareValue("Workbook.FullCalculationOnLoad", _expectedWorkbook.FullCalculationOnLoad, _actualWorkbook.FullCalculationOnLoad);
            CompareValue("Workbook.ForceFullCalculation", _expectedWorkbook.ForceFullCalculation, _actualWorkbook.ForceFullCalculation);
            CompareValue("Workbook.IterativeCalculation", _expectedWorkbook.IterativeCalculation, _actualWorkbook.IterativeCalculation);
            CompareNullableValue("Workbook.MaxCalculationIterations", _expectedWorkbook.MaxCalculationIterations, _actualWorkbook.MaxCalculationIterations);
            CompareNullableDouble("Workbook.MaxCalculationChange", _expectedWorkbook.MaxCalculationChange, _actualWorkbook.MaxCalculationChange);
            CompareStringSet(
                "Workbook.DisabledFormulaErrorCodes",
                _expectedWorkbook.DisabledFormulaErrorCodes,
                _actualWorkbook.DisabledFormulaErrorCodes,
                StringComparer.OrdinalIgnoreCase);
            CompareNamedRanges();

            CompareValue("Workbook.SheetCount", _expectedWorkbook.SheetCount, _actualWorkbook.SheetCount);
            var sheetCount = Math.Min(_expectedWorkbook.SheetCount, _actualWorkbook.SheetCount);
            for (var index = 0; index < sheetCount; index++)
                CompareSheet(index, _expectedWorkbook.GetSheetAt(index), _actualWorkbook.GetSheetAt(index));
        }

        private void CompareSheet(int index, Sheet expected, Sheet actual)
        {
            var path = $"Sheets[{index} '{expected.Name}']";
            CompareValue($"{path}.Name", expected.Name, actual.Name);
            CompareValue($"{path}.IsHidden", expected.IsHidden, actual.IsHidden);
            CompareValue($"{path}.IsVeryHidden", expected.IsVeryHidden, actual.IsVeryHidden);
            CompareNullableValue($"{path}.CodeName", expected.CodeName, actual.CodeName);
            CompareNullableValue($"{path}.TabColor", expected.TabColor, actual.TabColor);
            CompareCells(path, expected, actual);
            CompareStyleOnlyCells(path, expected, actual);
            CompareRanges($"{path}.MergedRegions", expected.MergedRegions, actual.MergedRegions, _expectedSheetNames, _actualSheetNames);
            CompareComments(path, expected, actual);
            CompareThreadedComments(path, expected, actual);
            CompareHyperlinks(path, expected, actual);
            CompareLayout(path, expected, actual);
            ComparePageSetup(path, expected, actual);
            CompareDataValidations(path, expected, actual);
            CompareConditionalFormats(path, expected, actual);
            CompareCharts(path, expected, actual);
            ComparePictures(path, expected, actual);
        }

        private void CompareCells(string path, Sheet expected, Sheet actual)
        {
            var expectedCells = expected.GetOccupiedCellMap();
            var actualCells = actual.GetOccupiedCellMap();
            CompareCoordinateSets($"{path}.Cells", expectedCells.Keys, actualCells.Keys);

            foreach (var key in expectedCells.Keys.Intersect(actualCells.Keys).OrderBy(key => key.Row).ThenBy(key => key.Col))
            {
                var cellPath = $"{path}.Cells[{CellAddress.NumberToColumnName(key.Col)}{key.Row}]";
                var expectedCell = expectedCells[key];
                var actualCell = actualCells[key];
                if (!expectedCell.HasFormula && !actualCell.HasFormula)
                    CompareValue($"{cellPath}.Value", FormatScalarValue(expectedCell.Value), FormatScalarValue(actualCell.Value));
                CompareValue($"{cellPath}.Formula", NormalizeFormula(expectedCell.FormulaText), NormalizeFormula(actualCell.FormulaText));
                CompareValue($"{cellPath}.IgnoreFormulaError", expectedCell.IgnoreFormulaError, actualCell.IgnoreFormulaError);
                CompareValue(
                    $"{cellPath}.Style",
                    FormatStyle(_expectedWorkbook.GetStyle(expectedCell.StyleId)),
                    FormatStyle(_actualWorkbook.GetStyle(actualCell.StyleId)));
            }
        }

        private void CompareStyleOnlyCells(string path, Sheet expected, Sheet actual)
        {
            var expectedEntries = expected.GetStyleOnlyEntries()
                .ToDictionary(entry => entry.Key, entry => FormatStyle(_expectedWorkbook.GetStyle(entry.StyleId)));
            var actualEntries = actual.GetStyleOnlyEntries()
                .ToDictionary(entry => entry.Key, entry => FormatStyle(_actualWorkbook.GetStyle(entry.StyleId)));

            CompareCoordinateSets($"{path}.StyleOnlyCells", expectedEntries.Keys, actualEntries.Keys);
            foreach (var key in expectedEntries.Keys.Intersect(actualEntries.Keys).OrderBy(key => key.Row).ThenBy(key => key.Col))
            {
                CompareValue(
                    $"{path}.StyleOnlyCells[{CellAddress.NumberToColumnName(key.Col)}{key.Row}]",
                    expectedEntries[key],
                    actualEntries[key]);
            }
        }

        private void CompareComments(string path, Sheet expected, Sheet actual)
        {
            CompareAddressStringDictionary(
                $"{path}.Comments",
                expected.Comments,
                actual.Comments,
                _expectedSheetNames,
                _actualSheetNames);
        }

        private void CompareThreadedComments(string path, Sheet expected, Sheet actual)
        {
            var expectedComments = expected.ThreadedComments.ToDictionary(
                pair => AddressText(pair.Key, _expectedSheetNames),
                pair => FormatThreadedComment(pair.Value),
                StringComparer.Ordinal);
            var actualComments = actual.ThreadedComments.ToDictionary(
                pair => AddressText(pair.Key, _actualSheetNames),
                pair => FormatThreadedComment(pair.Value),
                StringComparer.Ordinal);
            CompareStringDictionary($"{path}.ThreadedComments", expectedComments, actualComments);
        }

        private void CompareHyperlinks(string path, Sheet expected, Sheet actual)
        {
            var expectedLinks = expected.Hyperlinks.ToDictionary(
                pair => AddressText(pair.Key, _expectedSheetNames),
                pair =>
                {
                    expected.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
                    return $"{pair.Value}|{FormatHyperlinkMetadata(metadata)}";
                },
                StringComparer.Ordinal);
            var actualLinks = actual.Hyperlinks.ToDictionary(
                pair => AddressText(pair.Key, _actualSheetNames),
                pair =>
                {
                    actual.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
                    return $"{pair.Value}|{FormatHyperlinkMetadata(metadata)}";
                },
                StringComparer.Ordinal);
            CompareStringDictionary($"{path}.Hyperlinks", expectedLinks, actualLinks);
        }

        private void CompareLayout(string path, Sheet expected, Sheet actual)
        {
            CompareDouble($"{path}.DefaultColumnWidth", expected.DefaultColumnWidth, actual.DefaultColumnWidth);
            CompareDouble($"{path}.DefaultRowHeight", expected.DefaultRowHeight, actual.DefaultRowHeight);
            CompareDoubleDictionary($"{path}.ColumnWidths", expected.ColumnWidths, actual.ColumnWidths);
            CompareDoubleDictionary($"{path}.RowHeights", expected.RowHeights, actual.RowHeights);
            CompareUIntSet($"{path}.HiddenRows", expected.HiddenRows, actual.HiddenRows);
            CompareUIntSet($"{path}.FilterHiddenRows", expected.FilterHiddenRows, actual.FilterHiddenRows);
            CompareUIntSet($"{path}.HiddenCols", expected.HiddenCols, actual.HiddenCols);
            CompareUIntSet($"{path}.GroupHiddenRows", expected.GroupHiddenRows, actual.GroupHiddenRows);
            CompareUIntSet($"{path}.GroupHiddenCols", expected.GroupHiddenCols, actual.GroupHiddenCols);
            CompareDictionary($"{path}.RowOutlineLevels", expected.RowOutlineLevels, actual.RowOutlineLevels);
            CompareDictionary($"{path}.ColOutlineLevels", expected.ColOutlineLevels, actual.ColOutlineLevels);
            CompareValue($"{path}.OutlineSummaryBelow", expected.OutlineSummaryBelow ?? true, actual.OutlineSummaryBelow ?? true);
            CompareValue($"{path}.OutlineSummaryRight", expected.OutlineSummaryRight ?? true, actual.OutlineSummaryRight ?? true);
            CompareNullableValue($"{path}.ShowOutlineSymbols", expected.ShowOutlineSymbols, actual.ShowOutlineSymbols);
            CompareNullableValue($"{path}.ApplyOutlineStyles", expected.ApplyOutlineStyles, actual.ApplyOutlineStyles);
            CompareValue($"{path}.FrozenRows", expected.FrozenRows, actual.FrozenRows);
            CompareValue($"{path}.FrozenCols", expected.FrozenCols, actual.FrozenCols);
            CompareNullableValue($"{path}.SplitRow", expected.SplitRow, actual.SplitRow);
            CompareNullableValue($"{path}.SplitColumn", expected.SplitColumn, actual.SplitColumn);
            CompareNullableValue($"{path}.ViewTopRow", expected.ViewTopRow, actual.ViewTopRow);
            CompareNullableValue($"{path}.ViewLeftCol", expected.ViewLeftCol, actual.ViewLeftCol);
            CompareNullableValue($"{path}.ActiveRow", expected.ActiveRow, actual.ActiveRow);
            CompareNullableValue($"{path}.ActiveCol", expected.ActiveCol, actual.ActiveCol);
            CompareValue($"{path}.ViewMode", expected.ViewMode, actual.ViewMode);
            CompareValue($"{path}.ShowGridlines", expected.ShowGridlines, actual.ShowGridlines);
            CompareValue($"{path}.ShowHeadings", expected.ShowHeadings, actual.ShowHeadings);
            CompareValue($"{path}.ShowRulers", expected.ShowRulers, actual.ShowRulers);
            CompareValue($"{path}.ZoomPercent", expected.ZoomPercent, actual.ZoomPercent);
            CompareValue($"{path}.ShowFormulas", expected.ShowFormulas, actual.ShowFormulas);
            CompareValue($"{path}.FullCalculationOnLoad", expected.FullCalculationOnLoad, actual.FullCalculationOnLoad);
            CompareValue($"{path}.IsProtected", expected.IsProtected, actual.IsProtected);
            CompareNullableValue($"{path}.ProtectionPassword", expected.ProtectionPassword, actual.ProtectionPassword);
            CompareStringList(
                $"{path}.ProtectionPermissions",
                expected.ProtectionPermissions.Select(permission => permission.ToString()),
                actual.ProtectionPermissions.Select(permission => permission.ToString()));
            CompareRanges($"{path}.AllowEditRanges", expected.AllowEditRanges, actual.AllowEditRanges, _expectedSheetNames, _actualSheetNames);
        }

        private void ComparePageSetup(string path, Sheet expected, Sheet actual)
        {
            CompareNullableRange($"{path}.PrintArea", expected.PrintArea, actual.PrintArea);
            CompareValue($"{path}.PageOrientation", expected.PageOrientation, actual.PageOrientation);
            CompareValue($"{path}.PaperSize", expected.PaperSize, actual.PaperSize);
            ComparePageMargins($"{path}.PageMargins", expected.PageMargins, actual.PageMargins);
            CompareDouble($"{path}.HeaderMargin", expected.HeaderMargin, actual.HeaderMargin);
            CompareDouble($"{path}.FooterMargin", expected.FooterMargin, actual.FooterMargin);
            CompareValue($"{path}.PrintGridlines", expected.PrintGridlines, actual.PrintGridlines);
            CompareValue($"{path}.PrintHeadings", expected.PrintHeadings, actual.PrintHeadings);
            CompareScaleToFit($"{path}.ScaleToFit", expected.ScaleToFit, actual.ScaleToFit);
            CompareNullableValue($"{path}.FitToPage", NormalizeFitToPage(expected), NormalizeFitToPage(actual));
            CompareNullableValue($"{path}.AutoPageBreaks", expected.AutoPageBreaks, actual.AutoPageBreaks);
            CompareNullableValue($"{path}.PrintTitleRows", expected.PrintTitleRows, actual.PrintTitleRows);
            CompareNullableValue($"{path}.PrintTitleColumns", expected.PrintTitleColumns, actual.PrintTitleColumns);
            CompareValue($"{path}.PageHeader", expected.PageHeader, actual.PageHeader);
            CompareValue($"{path}.PageFooter", expected.PageFooter, actual.PageFooter);
            CompareValue($"{path}.DifferentFirstPageHeaderFooter", expected.DifferentFirstPageHeaderFooter, actual.DifferentFirstPageHeaderFooter);
            CompareValue($"{path}.DifferentOddEvenHeaderFooter", expected.DifferentOddEvenHeaderFooter, actual.DifferentOddEvenHeaderFooter);
            if (expected.DifferentFirstPageHeaderFooter || actual.DifferentFirstPageHeaderFooter)
            {
                CompareValue($"{path}.FirstPageHeader", expected.FirstPageHeader, actual.FirstPageHeader);
                CompareValue($"{path}.FirstPageFooter", expected.FirstPageFooter, actual.FirstPageFooter);
            }
            if (expected.DifferentOddEvenHeaderFooter || actual.DifferentOddEvenHeaderFooter)
            {
                CompareValue($"{path}.EvenPageHeader", expected.EvenPageHeader, actual.EvenPageHeader);
                CompareValue($"{path}.EvenPageFooter", expected.EvenPageFooter, actual.EvenPageFooter);
            }
            CompareValue($"{path}.HeaderFooterScaleWithDocument", expected.HeaderFooterScaleWithDocument, actual.HeaderFooterScaleWithDocument);
            CompareValue($"{path}.HeaderFooterAlignWithMargins", expected.HeaderFooterAlignWithMargins, actual.HeaderFooterAlignWithMargins);
            CompareValue($"{path}.CenterHorizontallyOnPage", expected.CenterHorizontallyOnPage, actual.CenterHorizontallyOnPage);
            CompareValue($"{path}.CenterVerticallyOnPage", expected.CenterVerticallyOnPage, actual.CenterVerticallyOnPage);
            CompareValue($"{path}.PageOrder", expected.PageOrder, actual.PageOrder);
            CompareNullableValue($"{path}.FirstPageNumber", expected.FirstPageNumber, actual.FirstPageNumber);
            CompareNullableValue($"{path}.UsePrinterDefaults", expected.UsePrinterDefaults, actual.UsePrinterDefaults);
            CompareNullableValue($"{path}.PrintCopies", expected.PrintCopies, actual.PrintCopies);
            CompareValue($"{path}.PrintBlackAndWhite", expected.PrintBlackAndWhite, actual.PrintBlackAndWhite);
            CompareValue($"{path}.PrintDraftQuality", expected.PrintDraftQuality, actual.PrintDraftQuality);
            CompareNullableValue($"{path}.PrintQualityDpi", expected.PrintQualityDpi, actual.PrintQualityDpi);
            CompareNullableValue(
                $"{path}.PrintQualityVerticalDpi",
                expected.PrintQualityVerticalDpi ?? expected.PrintQualityDpi,
                actual.PrintQualityVerticalDpi ?? actual.PrintQualityDpi);
            CompareValue($"{path}.PrintErrorValue", expected.PrintErrorValue, actual.PrintErrorValue);
            CompareValue($"{path}.PrintComments", expected.PrintComments, actual.PrintComments);
            CompareUIntSet($"{path}.RowPageBreaks", expected.RowPageBreaks, actual.RowPageBreaks);
            CompareUIntSet($"{path}.ColumnPageBreaks", expected.ColumnPageBreaks, actual.ColumnPageBreaks);
            ComparePictureSet($"{path}.PageHeaderPictures", expected.PageHeaderPictures, actual.PageHeaderPictures);
            ComparePictureSet($"{path}.PageFooterPictures", expected.PageFooterPictures, actual.PageFooterPictures);
            ComparePictureSet($"{path}.FirstPageHeaderPictures", expected.FirstPageHeaderPictures, actual.FirstPageHeaderPictures);
            ComparePictureSet($"{path}.FirstPageFooterPictures", expected.FirstPageFooterPictures, actual.FirstPageFooterPictures);
            ComparePictureSet($"{path}.EvenPageHeaderPictures", expected.EvenPageHeaderPictures, actual.EvenPageHeaderPictures);
            ComparePictureSet($"{path}.EvenPageFooterPictures", expected.EvenPageFooterPictures, actual.EvenPageFooterPictures);
        }

        private void CompareDataValidations(string path, Sheet expected, Sheet actual)
        {
            CompareStringList(
                $"{path}.DataValidations",
                expected.DataValidations.Select(validation => FormatDataValidation(validation, _expectedSheetNames)).OrderBy(text => text, StringComparer.Ordinal),
                actual.DataValidations.Select(validation => FormatDataValidation(validation, _actualSheetNames)).OrderBy(text => text, StringComparer.Ordinal));
        }

        private void CompareConditionalFormats(string path, Sheet expected, Sheet actual)
        {
            CompareStringList(
                $"{path}.ConditionalFormats",
                expected.ConditionalFormats.Select(format => FormatConditionalFormat(format, _expectedSheetNames)).OrderBy(text => text, StringComparer.Ordinal),
                actual.ConditionalFormats.Select(format => FormatConditionalFormat(format, _actualSheetNames)).OrderBy(text => text, StringComparer.Ordinal));
        }

        private void CompareCharts(string path, Sheet expected, Sheet actual)
        {
            CompareStringList(
                $"{path}.Charts",
                expected.Charts.Select(chart => FormatChart(chart, _expectedSheetNames)).OrderBy(text => text, StringComparer.Ordinal),
                actual.Charts.Select(chart => FormatChart(chart, _actualSheetNames)).OrderBy(text => text, StringComparer.Ordinal));
        }

        private void ComparePictures(string path, Sheet expected, Sheet actual)
        {
            CompareStringList(
                $"{path}.Pictures",
                expected.Pictures.Select(picture => FormatPicture(picture, _expectedSheetNames)).OrderBy(text => text, StringComparer.Ordinal),
                actual.Pictures.Select(picture => FormatPicture(picture, _actualSheetNames)).OrderBy(text => text, StringComparer.Ordinal));
        }

        private void CompareNamedRanges()
        {
            var expectedRanges = _expectedWorkbook.NamedRanges.ToDictionary(
                pair => pair.Key,
                pair => RangeText(pair.Value, _expectedSheetNames),
                StringComparer.OrdinalIgnoreCase);
            var actualRanges = _actualWorkbook.NamedRanges.ToDictionary(
                pair => pair.Key,
                pair => RangeText(pair.Value, _actualSheetNames),
                StringComparer.OrdinalIgnoreCase);
            CompareStringDictionary("Workbook.NamedRanges", expectedRanges, actualRanges);

            var expectedMetadata = _expectedWorkbook.NamedRangeMetadataByName.ToDictionary(
                pair => pair.Key,
                pair => $"{pair.Value.Scope}|{pair.Value.Comment}",
                StringComparer.OrdinalIgnoreCase);
            var actualMetadata = _actualWorkbook.NamedRangeMetadataByName.ToDictionary(
                pair => pair.Key,
                pair => $"{pair.Value.Scope}|{pair.Value.Comment}",
                StringComparer.OrdinalIgnoreCase);
            CompareStringDictionary("Workbook.NamedRangeMetadata", expectedMetadata, actualMetadata);
        }

        private void CompareNullableRange(string path, GridRange? expected, GridRange? actual)
        {
            var expectedText = expected is { } expectedRange ? RangeText(expectedRange, _expectedSheetNames) : "<null>";
            var actualText = actual is { } actualRange ? RangeText(actualRange, _actualSheetNames) : "<null>";
            CompareValue(path, expectedText, actualText);
        }

        private void CompareRanges(
            string path,
            IEnumerable<GridRange> expected,
            IEnumerable<GridRange> actual,
            IReadOnlyDictionary<SheetId, string> expectedSheetNames,
            IReadOnlyDictionary<SheetId, string> actualSheetNames)
        {
            CompareStringList(
                path,
                expected.Select(range => RangeText(range, expectedSheetNames)).OrderBy(text => text, StringComparer.Ordinal),
                actual.Select(range => RangeText(range, actualSheetNames)).OrderBy(text => text, StringComparer.Ordinal));
        }

        private void CompareAddressStringDictionary(
            string path,
            IReadOnlyDictionary<CellAddress, string> expected,
            IReadOnlyDictionary<CellAddress, string> actual,
            IReadOnlyDictionary<SheetId, string> expectedSheetNames,
            IReadOnlyDictionary<SheetId, string> actualSheetNames)
        {
            var expectedByAddress = expected.ToDictionary(
                pair => AddressText(pair.Key, expectedSheetNames),
                pair => pair.Value,
                StringComparer.Ordinal);
            var actualByAddress = actual.ToDictionary(
                pair => AddressText(pair.Key, actualSheetNames),
                pair => pair.Value,
                StringComparer.Ordinal);
            CompareStringDictionary(path, expectedByAddress, actualByAddress);
        }

        private void CompareCoordinateSets(
            string path,
            IEnumerable<(uint Row, uint Col)> expected,
            IEnumerable<(uint Row, uint Col)> actual)
        {
            CompareStringSet(
                path,
                expected.Select(key => $"{CellAddress.NumberToColumnName(key.Col)}{key.Row}"),
                actual.Select(key => $"{CellAddress.NumberToColumnName(key.Col)}{key.Row}"),
                StringComparer.Ordinal);
        }

        private void CompareUIntSet(string path, IEnumerable<uint> expected, IEnumerable<uint> actual)
        {
            CompareStringSet(
                path,
                expected.Select(value => value.ToString(CultureInfo.InvariantCulture)),
                actual.Select(value => value.ToString(CultureInfo.InvariantCulture)),
                StringComparer.Ordinal);
        }

        private void CompareStringSet(
            string path,
            IEnumerable<string> expected,
            IEnumerable<string> actual,
            IEqualityComparer<string> comparer)
        {
            var expectedSet = expected.ToHashSet(comparer);
            var actualSet = actual.ToHashSet(comparer);
            var missing = expectedSet.Except(actualSet, comparer).OrderBy(value => value, StringComparer.Ordinal).ToList();
            var extra = actualSet.Except(expectedSet, comparer).OrderBy(value => value, StringComparer.Ordinal).ToList();

            if (missing.Count > 0)
                Differences.Add($"{path}: missing [{string.Join(", ", missing)}]");
            if (extra.Count > 0)
                Differences.Add($"{path}: extra [{string.Join(", ", extra)}]");
        }

        private void CompareStringList(string path, IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var expectedList = expected.ToList();
            var actualList = actual.ToList();
            CompareValue($"{path}.Count", expectedList.Count, actualList.Count);
            var count = Math.Min(expectedList.Count, actualList.Count);
            for (var index = 0; index < count; index++)
                CompareValue($"{path}[{index}]", expectedList[index], actualList[index]);
        }

        private void CompareStringDictionary(
            string path,
            IReadOnlyDictionary<string, string> expected,
            IReadOnlyDictionary<string, string> actual)
        {
            CompareStringSet(path, expected.Keys, actual.Keys, expected.ComparerOrOrdinalIgnoreCase());
            foreach (var key in expected.Keys.Intersect(actual.Keys, expected.ComparerOrOrdinalIgnoreCase()).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                CompareValue($"{path}[{key}]", expected[key], actual[key]);
        }

        private void CompareDictionary<TValue>(
            string path,
            IReadOnlyDictionary<uint, TValue> expected,
            IReadOnlyDictionary<uint, TValue> actual)
            where TValue : notnull
        {
            CompareUIntSet($"{path}.Keys", expected.Keys, actual.Keys);
            foreach (var key in expected.Keys.Intersect(actual.Keys).OrderBy(key => key))
                CompareValue($"{path}[{key}]", expected[key], actual[key]);
        }

        private void CompareDoubleDictionary(
            string path,
            IReadOnlyDictionary<uint, double> expected,
            IReadOnlyDictionary<uint, double> actual)
        {
            CompareUIntSet($"{path}.Keys", expected.Keys, actual.Keys);
            foreach (var key in expected.Keys.Intersect(actual.Keys).OrderBy(key => key))
                CompareDouble($"{path}[{key}]", expected[key], actual[key]);
        }

        private void ComparePageMargins(string path, WorksheetPageMargins expected, WorksheetPageMargins actual)
        {
            CompareDouble($"{path}.Left", expected.Left, actual.Left);
            CompareDouble($"{path}.Right", expected.Right, actual.Right);
            CompareDouble($"{path}.Top", expected.Top, actual.Top);
            CompareDouble($"{path}.Bottom", expected.Bottom, actual.Bottom);
        }

        private void CompareScaleToFit(string path, WorksheetScaleToFit expected, WorksheetScaleToFit actual)
        {
            CompareNullableValue($"{path}.ScalePercent", expected.ScalePercent, actual.ScalePercent);
            CompareNullableValue($"{path}.FitToPagesWide", expected.FitToPagesWide, actual.FitToPagesWide);
            CompareNullableValue($"{path}.FitToPagesTall", expected.FitToPagesTall, actual.FitToPagesTall);
        }

        private void ComparePictureSet(string path, WorksheetHeaderFooterPictureSet expected, WorksheetHeaderFooterPictureSet actual)
        {
            CompareValue($"{path}.Left", FormatHeaderFooterPicture(expected.Left), FormatHeaderFooterPicture(actual.Left));
            CompareValue($"{path}.Center", FormatHeaderFooterPicture(expected.Center), FormatHeaderFooterPicture(actual.Center));
            CompareValue($"{path}.Right", FormatHeaderFooterPicture(expected.Right), FormatHeaderFooterPicture(actual.Right));
        }

        private void CompareDouble(string path, double expected, double actual)
        {
            if (!AreEquivalentDoubles(expected, actual))
                Differences.Add($"{path}: expected {DoubleText(expected)}, actual {DoubleText(actual)}");
        }

        private void CompareNullableDouble(string path, double? expected, double? actual)
        {
            if (!expected.HasValue || !actual.HasValue)
            {
                CompareValue(path, expected, actual);
                return;
            }

            CompareDouble(path, expected.Value, actual.Value);
        }

        private void CompareNullableValue<T>(string path, T? expected, T? actual)
        {
            CompareValue(path, expected, actual);
        }

        private void CompareValue<T>(string path, T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Differences.Add($"{path}: expected {FormatObject(expected)}, actual {FormatObject(actual)}");
        }
    }

    private static Dictionary<SheetId, string> CreateSheetNameMap(Workbook workbook) =>
        workbook.Sheets.ToDictionary(sheet => sheet.Id, sheet => sheet.Name);

    private static bool AreEquivalentDoubles(double expected, double actual)
    {
        if (double.IsNaN(expected) && double.IsNaN(actual))
            return true;
        if (double.IsInfinity(expected) || double.IsInfinity(actual))
            return expected.Equals(actual);
        return Math.Abs(expected - actual) <= DoubleTolerance;
    }

    private static string NormalizeFormula(string? formula) =>
        formula is null ? "<null>" : formula.TrimStart().TrimStart('=').ReplaceLineEndings("\n");

    private static string FormatScalarValue(ScalarValue value)
    {
        return value switch
        {
            BlankValue => "Blank",
            NumberValue number => $"Number:{DoubleText(number.Value)}",
            BoolValue boolean => $"Bool:{boolean.Value}",
            TextValue text => $"Text:{text.Value}",
            DateTimeValue dateTime => $"DateTime:{DoubleText(dateTime.Value)}",
            ErrorValue error => $"Error:{error.Code}",
            RangeValue range => FormatRangeValue(range),
            _ => value.ToString() ?? value.GetType().Name
        };
    }

    private static string FormatRangeValue(RangeValue range)
    {
        var cells = new List<string>(range.RowCount * range.ColCount);
        for (var row = 1; row <= range.RowCount; row++)
            for (var col = 1; col <= range.ColCount; col++)
                cells.Add(FormatScalarValue(range.At(row, col)));

        return $"Range:{range.SheetName ?? ""}!{range.StartRow},{range.StartCol}:{range.RowCount}x{range.ColCount}=[{string.Join(",", cells)}]";
    }

    private static string FormatStyle(CellStyle style)
    {
        var fillPattern = style.FillColor is not null && style.FillPatternStyle == CellFillPatternStyle.None
            ? CellFillPatternStyle.Solid
            : style.FillPatternStyle;
        var fields = new List<string>
        {
            $"Font={style.FontName}",
            $"FontSize={DoubleText(style.FontSize)}",
            $"Bold={style.Bold}",
            $"Italic={style.Italic}",
            $"Underline={style.Underline}",
            $"Strikethrough={style.Strikethrough}",
            $"Superscript={style.Superscript}",
            $"Subscript={style.Subscript}",
            $"FontColor={ColorText(style.FontColor)}",
            $"FillColor={NullableColorText(style.FillColor)}",
            $"FillPattern={fillPattern}",
            $"FillPatternColor={NullableColorText(style.FillPatternColor)}",
            $"BorderTop={BorderText(style.BorderTop)}",
            $"BorderRight={BorderText(style.BorderRight)}",
            $"BorderBottom={BorderText(style.BorderBottom)}",
            $"BorderLeft={BorderText(style.BorderLeft)}",
            $"NumberFormat={style.NumberFormat}",
            $"HorizontalAlignment={style.HorizontalAlignment}",
            $"VerticalAlignment={style.VerticalAlignment}",
            $"WrapText={style.WrapText}",
            $"ShrinkToFit={style.ShrinkToFit}",
            $"DoubleUnderline={style.DoubleUnderline}",
            $"IndentLevel={style.IndentLevel}",
            $"TextRotation={style.TextRotation}",
            $"Locked={style.Locked}",
            $"Hidden={style.Hidden}"
        };
        return string.Join("; ", fields);
    }

    private static string FormatDataValidation(DataValidation validation, IReadOnlyDictionary<SheetId, string> sheetNames)
    {
        return string.Join("|", new[]
        {
            $"AppliesTo={RangeText(validation.AppliesTo, sheetNames)}",
            $"AdditionalRanges={string.Join(",", validation.AdditionalRanges.Select(range => RangeText(range, sheetNames)).OrderBy(text => text, StringComparer.Ordinal))}",
            $"Type={validation.Type}",
            $"Operator={validation.Operator}",
            $"Formula1={validation.Formula1 ?? ""}",
            $"Formula2={validation.Formula2 ?? ""}",
            $"AllowBlank={validation.AllowBlank}",
            $"ShowDropdown={validation.ShowDropdown}",
            $"AlertStyle={validation.AlertStyle}",
            $"ShowInputMessage={validation.ShowInputMessage}",
            $"ShowErrorMessage={validation.ShowErrorMessage}",
            $"ErrorTitle={validation.ErrorTitle ?? ""}",
            $"ErrorMessage={validation.ErrorMessage ?? ""}",
            $"PromptTitle={validation.PromptTitle ?? ""}",
            $"PromptMessage={validation.PromptMessage ?? ""}"
        });
    }

    private static string FormatConditionalFormat(ConditionalFormat format, IReadOnlyDictionary<SheetId, string> sheetNames)
    {
        return string.Join("|", new[]
        {
            $"AppliesTo={RangeText(format.AppliesTo, sheetNames)}",
            $"Priority={format.Priority}",
            $"RuleType={format.RuleType}",
            $"Operator={format.Operator}",
            $"Value1={format.Value1 ?? ""}",
            $"Value2={format.Value2 ?? ""}",
            $"FormulaText={NormalizeFormula(format.FormulaText)}",
            $"FormatIfTrue={FormatOptionalStyle(format.FormatIfTrue)}",
            $"MinColor={RgbText(format.MinColor)}",
            $"MidColor={RgbText(format.MidColor)}",
            $"MaxColor={RgbText(format.MaxColor)}",
            $"UseThreeColorScale={format.UseThreeColorScale}",
            $"MinThreshold={format.MinThresholdType}:{format.MinThresholdValue ?? ""}:{format.MinThresholdGreaterThanOrEqual}",
            $"MidThreshold={format.MidThresholdType}:{format.MidThresholdValue ?? ""}:{format.MidThresholdGreaterThanOrEqual}",
            $"MaxThreshold={format.MaxThresholdType}:{format.MaxThresholdValue ?? ""}:{format.MaxThresholdGreaterThanOrEqual}",
            $"DataBarColor={RgbText(format.DataBarColor)}",
            $"DataBarMin={format.DataBarMinThresholdType}:{format.DataBarMinThresholdValue ?? ""}",
            $"DataBarMax={format.DataBarMaxThresholdType}:{format.DataBarMaxThresholdValue ?? ""}",
            $"DataBarShowValue={format.DataBarShowValue}",
            $"DataBarMinLength={format.DataBarMinLength}",
            $"DataBarMaxLength={format.DataBarMaxLength}",
            $"DataBarGradient={format.DataBarGradient}",
            $"DataBarBorder={format.DataBarBorder}",
            $"DataBarAxisPosition={format.DataBarAxisPosition ?? ""}",
            $"DataBarAxisColor={NullableRgbText(format.DataBarAxisColor)}",
            $"DataBarNegativeFillColor={NullableRgbText(format.DataBarNegativeFillColor)}",
            $"DataBarNegativeBorderColor={NullableRgbText(format.DataBarNegativeBorderColor)}",
            $"AboveAverage={format.AboveAverage}",
            $"IconSetStyle={format.IconSetStyle ?? ""}",
            $"IconSetShowValue={format.IconSetShowValue}",
            $"IconSetReverse={format.IconSetReverse}",
            $"IconSetThresholds={string.Join(",", format.IconSetThresholds.Select(threshold => $"{threshold.Type}:{threshold.Value ?? ""}:{threshold.GreaterThanOrEqual}"))}",
            $"IconOverrides={string.Join(",", format.IconOverrides.Select(icon => $"{icon.IconSet}:{icon.IconId}"))}",
            $"TopBottomRank={format.TopBottomRank}",
            $"TopBottomPercent={format.TopBottomPercent}",
            $"TextRuleText={format.TextRuleText ?? ""}",
            $"DateOccurringPeriod={format.DateOccurringPeriod ?? ""}",
            $"StopIfTrue={format.StopIfTrue}"
        });
    }

    private static string FormatOptionalStyle(CellStyle? style) =>
        style is null ? "<null>" : FormatStyle(style);

    private static bool? NormalizeFitToPage(Sheet sheet) =>
        sheet.FitToPage ??
        (sheet.ScaleToFit.FitToPagesWide.HasValue || sheet.ScaleToFit.FitToPagesTall.HasValue
            ? true
            : null);

    private static string FormatChart(ChartModel chart, IReadOnlyDictionary<SheetId, string> sheetNames)
    {
        return string.Join("|", new[]
        {
            $"Name={chart.Name ?? ""}",
            $"Type={chart.Type}",
            $"DataRange={RangeText(chart.DataRange, sheetNames)}",
            $"IsVisible={chart.IsVisible}",
            $"IsPivotChart={chart.IsPivotChart}",
            $"PivotSourceSheetName={chart.PivotSourceSheetName ?? ""}",
            $"PivotTableName={chart.PivotTableName ?? ""}",
            $"PivotCacheId={chart.PivotCacheId}",
            $"ChartStyleId={chart.ChartStyleId}",
            $"RoundedCorners={chart.RoundedCorners}",
            $"BlankDisplayMode={chart.BlankDisplayMode}",
            $"ShowDataInHiddenRowsAndColumns={chart.ShowDataInHiddenRowsAndColumns}",
            $"BarGapWidth={chart.BarGapWidth}",
            $"BarOverlap={chart.BarOverlap}",
            $"VaryColorsByPoint={chart.VaryColorsByPoint}",
            $"FirstRowIsHeader={chart.FirstRowIsHeader}",
            $"FirstColIsCategories={chart.FirstColIsCategories}",
            $"Title={chart.Title ?? ""}",
            $"XAxisTitle={chart.XAxisTitle ?? ""}",
            $"YAxisTitle={chart.YAxisTitle ?? ""}",
            $"HideXAxis={chart.HideXAxis}",
            $"HideYAxis={chart.HideYAxis}",
            $"XAxisPosition={chart.XAxisPosition}",
            $"YAxisPosition={chart.YAxisPosition}",
            $"LegendPosition={chart.LegendPosition}",
            $"LegendOverlay={chart.LegendOverlay}",
            $"ShowLegend={chart.ShowLegend}",
            $"ShowDataLabels={chart.ShowDataLabels}",
            $"DataLabelPosition={chart.DataLabelPosition}",
            $"DataLabelNumberFormat={chart.DataLabelNumberFormat}",
            $"DataLabelNumberFormatCode={chart.DataLabelNumberFormatCode ?? ""}",
            $"ShowLinearTrendline={chart.ShowLinearTrendline}",
            $"TrendlineName={chart.TrendlineName ?? ""}",
            $"TrendlineType={chart.TrendlineType}",
            $"ShowErrorBars={chart.ShowErrorBars}",
            $"ErrorBarKind={chart.ErrorBarKind}",
            $"ErrorBarDirection={chart.ErrorBarDirection}",
            $"ShowSecondaryAxis={chart.ShowSecondaryAxis}",
            $"SecondaryAxisSeriesIndexes={string.Join(",", chart.SecondaryAxisSeriesIndexes)}",
            $"ComboLineSeriesIndexes={string.Join(",", chart.ComboLineSeriesIndexes)}",
            $"WaterfallTotalPointIndices={string.Join(",", chart.WaterfallTotalPointIndices ?? [])}",
            $"Left={DoubleText(chart.Left)}",
            $"Top={DoubleText(chart.Top)}",
            $"Width={DoubleText(chart.Width)}",
            $"Height={DoubleText(chart.Height)}",
            $"DrawingAnchorKind={chart.DrawingAnchorKind}"
        });
    }

    private static string FormatPicture(PictureModel picture, IReadOnlyDictionary<SheetId, string> sheetNames)
    {
        return string.Join("|", new[]
        {
            $"Name={picture.Name ?? ""}",
            $"Anchor={AddressText(picture.Anchor, sheetNames)}",
            $"Kind={picture.Kind}",
            $"SourceRowCount={picture.SourceRowCount}",
            $"SourceColumnCount={picture.SourceColumnCount}",
            $"IsLinkedToSourceRange={picture.IsLinkedToSourceRange}",
            $"LinkedSourceRange={(picture.LinkedSourceRange is { } range ? RangeText(range, sheetNames) : "<null>")}",
            $"LinkedSourceSheetName={picture.LinkedSourceSheetName ?? ""}",
            $"Cells={string.Join(",", picture.Cells.Select(cell => $"{cell.RowOffset}:{cell.ColumnOffset}:{cell.Text}"))}",
            $"Image={ByteHashText(picture.ImageBytes)}",
            $"ContentType={picture.ContentType ?? ""}",
            $"Title={picture.Title ?? ""}",
            $"AltText={picture.AltText ?? ""}",
            $"Width={DoubleText(picture.Width)}",
            $"Height={DoubleText(picture.Height)}",
            $"LockAspectRatio={picture.LockAspectRatio}",
            $"RotationDegrees={DoubleText(picture.RotationDegrees)}",
            $"IsVisible={picture.IsVisible}",
            $"CropLeft={DoubleText(picture.CropLeft)}",
            $"CropTop={DoubleText(picture.CropTop)}",
            $"CropRight={DoubleText(picture.CropRight)}",
            $"CropBottom={DoubleText(picture.CropBottom)}"
        });
    }

    private static string FormatThreadedComment(ThreadedComment comment)
    {
        return string.Join("|", new[]
        {
            $"Text={comment.Text}",
            $"Author={comment.Author}",
            $"IsResolved={comment.IsResolved}",
            $"CreatedAtUtc={comment.CreatedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}",
            $"ModifiedAtUtc={comment.ModifiedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}",
            $"Replies={string.Join(",", comment.Replies.Select(FormatCommentReply))}"
        });
    }

    private static string FormatCommentReply(CommentReply reply) =>
        $"{reply.Text}:{reply.Author}:{reply.CreatedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}:{reply.ModifiedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""}";

    private static string FormatHyperlinkMetadata(HyperlinkMetadata? metadata) =>
        metadata is null
            ? "<null>"
            : $"{metadata.LinkType}:{metadata.ScreenTip}:{metadata.Bookmark}";

    private static string FormatHeaderFooterPicture(WorksheetHeaderFooterPicture? picture)
    {
        return picture is null
            ? "<null>"
            : string.Join("|", new[]
            {
                $"Image={ByteHashText(picture.ImageBytes)}",
                $"ContentType={picture.ContentType}",
                $"FileName={picture.FileName ?? ""}",
                $"Width={DoubleText(picture.Width)}",
                $"Height={DoubleText(picture.Height)}"
            });
    }

    private static string RangeText(GridRange range, IReadOnlyDictionary<SheetId, string> sheetNames) =>
        $"{SheetText(range.Start.Sheet, sheetNames)}!{range.Start.ToA1()}:{range.End.ToA1()}";

    private static string AddressText(CellAddress address, IReadOnlyDictionary<SheetId, string> sheetNames) =>
        $"{SheetText(address.Sheet, sheetNames)}!{address.ToA1()}";

    private static string SheetText(SheetId sheetId, IReadOnlyDictionary<SheetId, string> sheetNames) =>
        sheetNames.TryGetValue(sheetId, out var name)
            ? name
            : sheetId.Value.ToString("N");

    private static string ColorText(CellColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string NullableColorText(CellColor? color) => color is { } value ? ColorText(value) : "<null>";

    private static string RgbText(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string NullableRgbText(RgbColor? color) => color is { } value ? RgbText(value) : "<null>";

    private static string BorderText(CellBorder border) => $"{border.Style}:{ColorText(border.Color)}";

    private static string DoubleText(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string ByteHashText(byte[]? bytes)
    {
        if (bytes is null)
            return "<null>";

        var hash = SHA256.HashData(bytes);
        return $"{bytes.Length}:{Convert.ToHexString(hash)}";
    }

    private static string FormatObject<T>(T value)
    {
        return value switch
        {
            null => "<null>",
            double number => DoubleText(number),
            string text => $"\"{text}\"",
            _ => value.ToString() ?? typeof(T).Name
        };
    }
}

internal sealed class XlsxSemanticComparisonResult
{
    public XlsxSemanticComparisonResult(IReadOnlyList<string> differences)
    {
        Differences = differences;
    }

    public IReadOnlyList<string> Differences { get; }

    public bool AreEquivalent => Differences.Count == 0;

    public string ToAssertionMessage()
    {
        if (AreEquivalent)
            return "Workbook semantics are equivalent.";

        var builder = new StringBuilder();
        builder.AppendLine("Workbook semantic comparison failed:");
        foreach (var difference in Differences)
            builder.AppendLine("- " + difference);
        return builder.ToString();
    }
}

internal static class XlsxSemanticDictionaryExtensions
{
    public static IEqualityComparer<string> ComparerOrOrdinalIgnoreCase(this IReadOnlyDictionary<string, string> dictionary) =>
        dictionary is Dictionary<string, string> concrete ? concrete.Comparer : StringComparer.OrdinalIgnoreCase;
}
