using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System.Reflection;
using ModelBorderStyle = FreeX.Core.Model.BorderStyle;
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
        var summaries = new List<(string File, int Sheets, int Cells, int Formulas, int Styles, int Merges, int Dimensions)>();

        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            var workbook = adapter.Load(stream);
            workbook.Sheets.Should().NotBeEmpty(Path.GetFileName(path));

            summaries.Add((
                Path.GetFileName(path),
                workbook.SheetCount,
                workbook.Sheets.Sum(sheet => sheet.CellCount),
                workbook.Sheets.Sum(sheet => sheet.FormulaCellCount),
                workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count(item => item.Cell.StyleId != StyleId.Default) + sheet.StyleOnlyCellCount),
                workbook.Sheets.Sum(sheet => sheet.MergedRegions.Count),
                workbook.Sheets.Sum(sheet => sheet.ColumnWidths.Count + sheet.RowHeights.Count + sheet.HiddenRows.Count + sheet.HiddenCols.Count)));
        }

        summaries.Should().HaveCountGreaterThanOrEqualTo(20);
        summaries.Sum(summary => summary.Cells).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Formulas).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Styles).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Merges).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Dimensions).Should().BeGreaterThan(0);
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

        var hiddenRow = sheet.CreateRow(3);
        hiddenRow.ZeroHeight = true;
        hiddenRow.CreateCell(0).SetCellValue("hidden");

        HSSFFormulaEvaluator.EvaluateAllFormulaCells(hssf);

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }

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
