using FluentAssertions;
using ExcelDataReader;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System.Reflection;
using System.Text;
using ModelBorderStyle = FreeX.Core.Model.BorderStyle;
using ModelCellAddress = FreeX.Core.Model.CellAddress;
using ModelHorizontalAlignment = FreeX.Core.Model.HorizontalAlignment;
using ModelVerticalAlignment = FreeX.Core.Model.VerticalAlignment;
using NPOIBorderStyle = NPOI.SS.UserModel.BorderStyle;
using NPOIHorizontalAlignment = NPOI.SS.UserModel.HorizontalAlignment;
using NPOIVerticalAlignment = NPOI.SS.UserModel.VerticalAlignment;

namespace FreeX.Core.IO.Tests;

public sealed class LegacyXlsFileAdapterTests
{
    [Fact]
    public void Formats_AreOpenOnly()
    {
        var adapter = new LegacyXlsFileAdapter();

        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xls" &&
            format.CanOpen &&
            !format.CanSave);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsb" &&
            format.FormatName == "XLSB Binary Workbook" &&
            format.CanOpen &&
            !format.CanSave);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlt" &&
            format.FormatName == "XLT 97-2003 Template" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
    }

    [Fact]
    public void Load_ReadsLegacyBinaryWorkbookSheetsAndCells()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simple.xls");
        using var stream = File.OpenRead(path);
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().NotBeEmpty();
        var firstSheet = workbook.Sheets[0];
        firstSheet.Name.Should().NotBeNullOrWhiteSpace();
        firstSheet.GetUsedRange().Should().NotBeNull();
        firstSheet.EnumerateCells()
            .Any(cell => cell.Cell.Value is TextValue or NumberValue or BoolValue)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Load_ReadsLegacyXlsFormulasStylesMergesAndLayout()
    {
        using var stream = CreateRichLegacyXlsFixture();
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().HaveCount(2);
        workbook.Uses1904DateSystem.Should().BeFalse();
        workbook.StyleCount.Should().BeGreaterThan(1);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Visible");
        workbook.NamedRanges.Should().ContainKey("InputCell");
        workbook.NamedRanges["InputCell"].Start.Should().Be(new ModelCellAddress(sheet.Id, 2, 1));
        workbook.NamedRanges["InputCell"].End.Should().Be(new ModelCellAddress(sheet.Id, 2, 1));
        workbook.NamedRangeMetadataByName["InputCell"].Should().Be(new NamedRangeMetadata("Workbook", "Primary input cell"));
        workbook.NamedFormulas.Should().ContainKey("DoubleInput").WhoseValue.Should().Be("Visible!$A$2*2");

        sheet.MergedRegions.Should().ContainSingle(region =>
            region.Start.Row == 1 && region.Start.Col == 1 &&
            region.End.Row == 1 && region.End.Col == 2);
        sheet.HiddenRows.Should().Contain(4);
        sheet.HiddenCols.Should().Contain(3);
        sheet.RowHeights.Should().ContainKey(2).WhoseValue.Should().BeApproximately(32, 0.5);
        sheet.ColumnWidths.Should().ContainKey(2).WhoseValue.Should().BeApproximately(18, 0.01);

        var formulaCell = sheet.GetCell(2, 2);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("A2*2");
        formulaCell.ArrayMode.Should().Be(FormulaArrayMode.Implicit);
        formulaCell.Value.Should().Be(new NumberValue(42));
        formulaCell.StyleId.Should().NotBe(StyleId.Default);

        var formulaStyle = workbook.GetStyle(formulaCell.StyleId);
        formulaStyle.NumberFormat.Should().Be("$#,##0.00");
        formulaStyle.Bold.Should().BeTrue();
        formulaStyle.FontColor.Should().Be(CellColor.White);
        formulaStyle.FillColor.Should().Be(new CellColor(255, 255, 0));
        formulaStyle.FillPatternStyle.Should().Be(CellFillPatternStyle.Solid);
        formulaStyle.HorizontalAlignment.Should().Be(ModelHorizontalAlignment.Center);
        formulaStyle.VerticalAlignment.Should().Be(ModelVerticalAlignment.Center);
        formulaStyle.BorderBottom.Style.Should().Be(ModelBorderStyle.Thin);

        var hyperlinkAddress = new ModelCellAddress(sheet.Id, 2, 4);
        sheet.Hyperlinks.Should().ContainKey(hyperlinkAddress)
            .WhoseValue.Should().Be("https://exinfm.com/free_spreadsheets.html");
        sheet.HyperlinkMetadata[hyperlinkAddress].Should().Be(new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage));

        var commentAddress = new ModelCellAddress(sheet.Id, 2, 5);
        sheet.Comments.Should().ContainKey(commentAddress)
            .WhoseValue.Should().Be("Review before publishing");
        sheet.CommentAuthors[commentAddress].Should().Be("Analyst");

        var hiddenSheet = workbook.GetSheetAt(1);
        hiddenSheet.Name.Should().Be("Hidden");
        hiddenSheet.IsHidden.Should().BeTrue();
        hiddenSheet.IsVeryHidden.Should().BeFalse();
    }

    [Fact]
    public void Load_ExinfmLegacyXlsCorpusFiles_WhenDownloaded()
    {
        var corpusRoot = Path.Combine(
            TestWorkspaceFiles.FindWorkspaceFileDirectory("fidelity-corpus", "manifest.csv"),
            "files",
            "exinfm");
        if (!Directory.Exists(corpusRoot))
            return;

        var paths = Directory.GetFiles(corpusRoot, "*.xls", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return;

        var adapter = new LegacyXlsFileAdapter();
        var summaries = new List<LegacyXlsCorpusSummary>();

        foreach (var path in paths)
        {
            var source = ReadSourceSummary(path);
            using var stream = File.OpenRead(path);
            var workbook = adapter.Load(stream);
            workbook.Sheets.Should().NotBeEmpty(Path.GetFileName(path));

            var imported = new LegacyXlsCorpusSummary(
                Path.GetFileName(path),
                workbook.SheetCount,
                workbook.Sheets.Sum(sheet => sheet.CellCount),
                workbook.Sheets.Sum(sheet => sheet.FormulaCellCount),
                workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count(item => item.Cell.StyleId != StyleId.Default) + sheet.StyleOnlyCellCount),
                workbook.Sheets.Sum(sheet => sheet.MergedRegions.Count),
                workbook.Sheets.Sum(sheet => sheet.ColumnWidths.Count + sheet.RowHeights.Count + sheet.HiddenRows.Count + sheet.HiddenCols.Count),
                workbook.Sheets.Count(sheet => sheet.IsHidden),
                workbook.Sheets.Count(sheet => sheet.IsVeryHidden),
                workbook.NamedRanges.Count + workbook.NamedFormulas.Count,
                workbook.Sheets.Sum(sheet => sheet.Hyperlinks.Count),
                workbook.Sheets.Sum(sheet => sheet.Comments.Count));

            imported.Sheets.Should().Be(source.Sheets, imported.File);
            imported.Cells.Should().Be(source.Cells, imported.File);
            if (source.RichMetadata)
            {
                imported.Formulas.Should().Be(source.Formulas, imported.File);
                imported.Merges.Should().Be(source.Merges, imported.File);
                imported.HiddenSheets.Should().Be(source.HiddenSheets, imported.File);
                imported.VeryHiddenSheets.Should().Be(source.VeryHiddenSheets, imported.File);
                imported.DefinedNames.Should().Be(source.DefinedNames, imported.File);
                imported.Hyperlinks.Should().Be(source.Hyperlinks, imported.File);
                imported.Comments.Should().Be(source.Comments, imported.File);
                imported.Styles.Should().BeGreaterThanOrEqualTo(source.Styles, imported.File);
                imported.Dimensions.Should().BeGreaterThanOrEqualTo(source.Dimensions, imported.File);
            }

            summaries.Add(imported);
        }

        summaries.Should().HaveCountGreaterThanOrEqualTo(20);
        summaries.Sum(summary => summary.Cells).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Formulas).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Styles).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Merges).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Dimensions).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.DefinedNames).Should().BeGreaterThan(0);
        summaries.Count(summary => summary.RichMetadata).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Load_ReadsXlsbBinaryWorkbookSheetsAndCells()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simple.xlsb");
        using var stream = File.OpenRead(path);
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        // The .xlsb (BIFF12) read path shares ExcelDataReader with .xls; this hardens it
        // with an explicit fixture. The workbook has three sheets, the first carrying a
        // values-only mix of number, double, date, bool, and text cells.
        workbook.Sheets.Should().HaveCount(3);
        var firstSheet = workbook.Sheets[0];
        firstSheet.Name.Should().NotBeNullOrWhiteSpace();
        firstSheet.GetUsedRange().Should().NotBeNull();

        firstSheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        firstSheet.GetValue(1, 2).Should().Be(new NumberValue(1.02));
        firstSheet.GetValue(1, 3).Should().BeOfType<DateTimeValue>();
        firstSheet.GetValue(1, 5).Should().Be(new TextValue("next value is null"));
        firstSheet.GetValue(21, 4).Should().Be(new BoolValue(true));

        var firstSheetCells = firstSheet.EnumerateCells().Select(cell => cell.Cell.Value).ToList();
        firstSheetCells.Should().Contain(value => value is NumberValue);
        firstSheetCells.Should().Contain(value => value is TextValue);
        firstSheetCells.Should().Contain(value => value is BoolValue);
        firstSheetCells.Should().Contain(value => value is DateTimeValue);
    }

    [Fact]
    public void Load_MapsLegacyDateCellsToDateTimeValues()
    {
        var value = MapLegacyXlsValue(new DateTime(2026, 5, 17, 9, 30, 0));

        value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_MapsLegacyTimeOnlyCellsToDateTimeValues()
    {
        var value = MapLegacyXlsValue(new TimeSpan(9, 30, 0));

        value.Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
    }

    [Theory]
    [MemberData(nameof(AdditionalNumericValues))]
    public void Load_MapsLegacyNumericPrimitiveCellsToNumberValues(object legacyValue, double expected)
    {
        var value = MapLegacyXlsValue(legacyValue);

        value.Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new LegacyXlsFileAdapter();

        var act = () => adapter.Save(new Workbook("Book1"), new MemoryStream());

        act.Should().Throw<NotSupportedException>();
    }

    private static ScalarValue MapLegacyXlsValue(object? value)
    {
        var method = typeof(LegacyXlsFileAdapter).GetMethod("MapValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (ScalarValue)method!.Invoke(null, [value])!;
    }

    private static MemoryStream CreateRichLegacyXlsFixture()
    {
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Visible");
        hssf.CreateSheet("Hidden");
        hssf.SetSheetVisibility(1, SheetVisibility.Hidden);

        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 1));
        sheet.SetColumnWidth(1, 18 * 256);
        sheet.SetColumnHidden(2, true);

        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("Merged header");

        var row = sheet.CreateRow(1);
        row.HeightInPoints = 24;
        row.CreateCell(0).SetCellValue(21);

        var font = hssf.CreateFont();
        font.IsBold = true;
        font.Color = IndexedColors.White.Index;

        var style = hssf.CreateCellStyle();
        style.SetFont(font);
        style.DataFormat = hssf.CreateDataFormat().GetFormat("$#,##0.00");
        style.FillForegroundColor = IndexedColors.Yellow.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = NPOIHorizontalAlignment.Center;
        style.VerticalAlignment = NPOIVerticalAlignment.Center;
        style.BorderBottom = NPOIBorderStyle.Thin;
        style.BottomBorderColor = IndexedColors.Black.Index;

        var formula = row.CreateCell(1);
        formula.SetCellFormula("A2*2");
        formula.CellStyle = style;
        row.CreateCell(2).SetCellValue("hidden column");

        var helper = hssf.GetCreationHelper();
        var hyperlink = helper.CreateHyperlink(HyperlinkType.Url);
        hyperlink.Address = "https://exinfm.com/free_spreadsheets.html";
        hyperlink.Label = "EXINFM";
        var hyperlinkCell = row.CreateCell(3);
        hyperlinkCell.SetCellValue("EXINFM");
        hyperlinkCell.Hyperlink = hyperlink;

        var drawing = sheet.CreateDrawingPatriarch();
        var commentAnchor = new HSSFClientAnchor(0, 0, 0, 0, 4, 1, 6, 3);
        var comment = drawing.CreateCellComment(commentAnchor);
        comment.String = helper.CreateRichTextString("Review before publishing");
        comment.Author = "Analyst";
        var commentCell = row.CreateCell(4);
        commentCell.SetCellValue("commented");
        commentCell.CellComment = comment;

        var hiddenRow = sheet.CreateRow(3);
        hiddenRow.ZeroHeight = true;
        hiddenRow.CreateCell(0).SetCellValue("hidden");

        var inputName = hssf.CreateName();
        inputName.NameName = "InputCell";
        inputName.RefersToFormula = "'Visible'!$A$2";
        inputName.Comment = "Primary input cell";

        var formulaName = hssf.CreateName();
        formulaName.NameName = "DoubleInput";
        formulaName.RefersToFormula = "Visible!$A$2*2";

        HSSFFormulaEvaluator.EvaluateAllFormulaCells(hssf);

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }

    private static LegacyXlsCorpusSummary ReadSourceSummary(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        try
        {
            using var hssf = new HSSFWorkbook(stream);
            return ReadHssfSourceSummary(path, hssf);
        }
        catch
        {
            using var fallbackStream = new MemoryStream(bytes, writable: false);
            return ReadExcelDataReaderSourceSummary(path, fallbackStream);
        }
    }

    private static LegacyXlsCorpusSummary ReadHssfSourceSummary(string path, HSSFWorkbook hssf)
    {
        var cells = 0;
        var formulas = 0;
        var styles = 0;
        var merges = 0;
        var dimensions = 0;
        var hyperlinks = 0;
        var comments = 0;

        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            merges += sheet.NumMergedRegions;

            for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                var row = sheet.GetRow(rowIndex);
                if (row is null)
                    continue;

                if (row.ZeroHeight || row.HeightInPoints > 0)
                    dimensions++;

                foreach (var cell in row.Cells)
                {
                    if (IsSourceContentCell(cell))
                        cells++;
                    if (cell.CellType == CellType.Formula)
                        formulas++;
                    if (cell.CellStyle?.Index > 0)
                        styles++;
                }
            }

            var maxColumn = FindLastSourceColumn(sheet);
            for (var columnIndex = 0; columnIndex <= maxColumn; columnIndex++)
            {
                if (sheet.IsColumnHidden(columnIndex) ||
                    sheet.GetColumnWidth(columnIndex) != sheet.DefaultColumnWidth * 256)
                {
                    dimensions++;
                }
            }

            if (sheet is HSSFSheet hssfSheet)
            {
                hyperlinks += hssfSheet.GetHyperlinkList().Count;
                comments += hssfSheet.GetCellComments().Count;
            }
        }

        var validationWorkbook = new Workbook("DefinedNameValidation");
        var definedNames = Enumerable.Range(0, hssf.NumberOfNames)
            .Select(hssf.GetNameAt)
            .Where(name => IsImportableDefinedName(name, validationWorkbook))
            .Select(name => name.NameName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new LegacyXlsCorpusSummary(
            Path.GetFileName(path),
            hssf.NumberOfSheets,
            cells,
            formulas,
            styles,
            merges,
            dimensions,
            Enumerable.Range(0, hssf.NumberOfSheets).Count(index =>
                hssf.GetSheetVisibility(index) is SheetVisibility.Hidden or SheetVisibility.VeryHidden),
            Enumerable.Range(0, hssf.NumberOfSheets).Count(index =>
                hssf.GetSheetVisibility(index) is SheetVisibility.VeryHidden),
            definedNames,
            hyperlinks,
            comments);
    }

    private static LegacyXlsCorpusSummary ReadExcelDataReaderSourceSummary(string path, Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = 0;
        var cells = 0;

        do
        {
            sheets++;
            while (reader.Read())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var value = reader.GetValue(column);
                    if (value is not null && (value is not string text || text.Length > 0))
                        cells++;
                }
            }
        }
        while (reader.NextResult());

        return new LegacyXlsCorpusSummary(
            Path.GetFileName(path),
            sheets,
            cells,
            Formulas: 0,
            Styles: 0,
            Merges: 0,
            Dimensions: 0,
            HiddenSheets: 0,
            VeryHiddenSheets: 0,
            DefinedNames: 0,
            Hyperlinks: 0,
            Comments: 0,
            RichMetadata: false);
    }

    private static bool IsSourceContentCell(ICell cell)
    {
        if (cell.CellType == CellType.Blank)
            return false;

        return cell.CellType != CellType.String || !string.IsNullOrEmpty(cell.StringCellValue);
    }

    private static int FindLastSourceColumn(ISheet sheet)
    {
        var maxColumn = 0;
        for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is not null && row.LastCellNum > 0)
                maxColumn = Math.Max(maxColumn, row.LastCellNum - 1);
        }

        return maxColumn;
    }

    private static bool IsImportableDefinedName(IName? name, Workbook validationWorkbook) =>
        name is not null &&
        !name.IsDeleted &&
        !name.IsFunctionName &&
        !string.IsNullOrWhiteSpace(name.RefersToFormula) &&
        !IsExcelReservedDefinedName(name.NameName) &&
        validationWorkbook.ValidateNamedRangeName(name.NameName) is null;

    private static bool IsExcelReservedDefinedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var trimmedName = name.Trim();
        return trimmedName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Print_Area", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Print_Titles", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "_FilterDatabase", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Criteria", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Database", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Extract", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "Consolidate_Area", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LegacyXlsCorpusSummary(
        string File,
        int Sheets,
        int Cells,
        int Formulas,
        int Styles,
        int Merges,
        int Dimensions,
        int HiddenSheets,
        int VeryHiddenSheets,
        int DefinedNames,
        int Hyperlinks,
        int Comments,
        bool RichMetadata = true);

    public static TheoryData<object, double> AdditionalNumericValues() => new()
    {
        { 123L, 123d },
        { (short)-7, -7d },
        { 12.5f, 12.5d },
        { (byte)42, 42d },
        { (sbyte)-42, -42d },
        { 456u, 456d },
        { (ushort)789, 789d },
        { 900UL, 900d }
    };
}
