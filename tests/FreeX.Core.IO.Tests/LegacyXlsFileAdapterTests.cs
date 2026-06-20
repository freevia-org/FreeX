using FluentAssertions;
using ExcelDataReader;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System.Globalization;
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
    private const int LegacyXlsMaxColumnIndex = 255;
    private const short LegacyPaperSizeLetter = 1;
    private const short LegacyPaperSizeLegal = 5;
    private const short LegacyPaperSizeA4 = 9;

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
        workbook.ActiveSheetIndex.Should().Be(0);
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
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(1);
        sheet.RowOutlineLevels.Should().ContainKey(6).WhoseValue.Should().Be(1);
        sheet.RowOutlineLevels.Should().ContainKey(7).WhoseValue.Should().Be(1);
        sheet.ColOutlineLevels.Should().ContainKey(6).WhoseValue.Should().Be(1);
        sheet.ColOutlineLevels.Should().ContainKey(7).WhoseValue.Should().Be(1);
        sheet.PrintArea.Should().NotBeNull();
        sheet.PrintArea!.Value.Start.Should().Be(new ModelCellAddress(sheet.Id, 1, 1));
        sheet.PrintArea.Value.End.Should().Be(new ModelCellAddress(sheet.Id, 7, 5));
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.AutoFilter.Should().NotBeNull();
        sheet.AutoFilter!.Reference.Should().Be("A1:E7");
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPassword.Should().Be(ProtectionPasswordHelper.ToLegacyPasswordHash("secret"));
        GetProtectionMetadataAttribute(sheet, "objects").Should().Be("1");
        GetProtectionMetadataAttribute(sheet, "scenarios").Should().Be("1");
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Letter);
        sheet.PageMargins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.0));
        sheet.HeaderMargin.Should().BeApproximately(0.25, 0.0001);
        sheet.FooterMargin.Should().BeApproximately(0.35, 0.0001);
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeTrue();
        sheet.CenterHorizontallyOnPage.Should().BeTrue();
        sheet.CenterVerticallyOnPage.Should().BeTrue();
        sheet.FitToPage.Should().BeTrue();
        sheet.AutoPageBreaks.Should().BeTrue();
        sheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 2));
        sheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        sheet.FirstPageNumber.Should().Be(3);
        sheet.PrintCopies.Should().Be(2);
        sheet.PrintBlackAndWhite.Should().BeTrue();
        sheet.PrintDraftQuality.Should().BeTrue();
        sheet.PrintQualityDpi.Should().Be(600);
        sheet.PrintQualityVerticalDpi.Should().Be(300);
        sheet.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);
        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Legacy", "Page &P", "&D"));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Center footer", "Right footer"));
        sheet.RowPageBreaks.Should().Contain(5);
        sheet.ColumnPageBreaks.Should().Contain(4);
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowFormulas.Should().BeTrue();
        sheet.ViewTopRow.Should().Be(3);
        sheet.TabColor.Should().Be(new CellColor(255, 192, 0));

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
        hiddenSheet.SplitRow.Should().Be(4);
        hiddenSheet.SplitColumn.Should().Be(3);
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
                workbook.Sheets.Sum(sheet => sheet.Comments.Count),
                workbook.Sheets.Count(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0),
                workbook.Sheets.Sum(sheet => sheet.RowOutlineLevels.Count),
                workbook.Sheets.Sum(sheet => sheet.ColOutlineLevels.Count),
                workbook.Sheets.Count(sheet => sheet.PrintArea is not null),
                workbook.Sheets.Count(sheet => sheet.PrintTitleRows is not null),
                workbook.Sheets.Count(sheet => sheet.PrintTitleColumns is not null),
                workbook.Sheets.Count(sheet => sheet.AutoFilter is not null),
                workbook.Sheets.Count(sheet => sheet.IsProtected),
                workbook.Sheets.Count,
                workbook.Sheets.Sum(sheet => sheet.RowPageBreaks.Count + sheet.ColumnPageBreaks.Count),
                workbook.ActiveSheetIndex,
                SheetNames: workbook.Sheets.Select(sheet => sheet.Name).ToArray(),
                CellFingerprints: ReadImportedCellFingerprints(workbook),
                DefinedNameFingerprints: ReadImportedDefinedNameFingerprints(workbook),
                HyperlinkFingerprints: ReadImportedHyperlinkFingerprints(workbook),
                CommentFingerprints: ReadImportedCommentFingerprints(workbook),
                PaneFingerprints: ReadImportedPaneFingerprints(workbook),
                RowOutlineFingerprints: ReadImportedRowOutlineFingerprints(workbook),
                ColOutlineFingerprints: ReadImportedColOutlineFingerprints(workbook),
                PrintLayoutFingerprints: ReadImportedPrintLayoutFingerprints(workbook),
                PageSetupFingerprints: ReadImportedPageSetupFingerprints(workbook),
                ViewStateFingerprints: ReadImportedViewStateFingerprints(workbook),
                AutoFilterFingerprints: ReadImportedAutoFilterFingerprints(workbook),
                SheetProtectionFingerprints: ReadImportedSheetProtectionFingerprints(workbook));

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
                imported.SheetNames.Should().Equal(source.SheetNames, imported.File);
                imported.CellFingerprints.Should().BeEquivalentTo(source.CellFingerprints, imported.File);
                imported.DefinedNameFingerprints.Should().BeEquivalentTo(source.DefinedNameFingerprints, imported.File);
                imported.HyperlinkFingerprints.Should().BeEquivalentTo(source.HyperlinkFingerprints, imported.File);
                imported.CommentFingerprints.Should().BeEquivalentTo(source.CommentFingerprints, imported.File);
                imported.FreezePanes.Should().Be(source.FreezePanes, imported.File);
                imported.RowOutlineLevels.Should().Be(source.RowOutlineLevels, imported.File);
                imported.ColOutlineLevels.Should().Be(source.ColOutlineLevels, imported.File);
                imported.PrintAreas.Should().Be(source.PrintAreas, imported.File);
                imported.PrintTitleRows.Should().Be(source.PrintTitleRows, imported.File);
                imported.PrintTitleColumns.Should().Be(source.PrintTitleColumns, imported.File);
                imported.AutoFilters.Should().Be(source.AutoFilters, imported.File);
                imported.ProtectedSheets.Should().Be(source.ProtectedSheets, imported.File);
                imported.PageSetupSheets.Should().Be(source.PageSetupSheets, imported.File);
                imported.PageBreaks.Should().Be(source.PageBreaks, imported.File);
                imported.ActiveSheetIndex.Should().Be(source.ActiveSheetIndex, imported.File);
                imported.PaneFingerprints.Should().BeEquivalentTo(source.PaneFingerprints, imported.File);
                imported.RowOutlineFingerprints.Should().BeEquivalentTo(source.RowOutlineFingerprints, imported.File);
                imported.ColOutlineFingerprints.Should().BeEquivalentTo(source.ColOutlineFingerprints, imported.File);
                imported.PrintLayoutFingerprints.Should().BeEquivalentTo(source.PrintLayoutFingerprints, imported.File);
                imported.PageSetupFingerprints.Should().BeEquivalentTo(source.PageSetupFingerprints, imported.File);
                imported.ViewStateFingerprints.Should().BeEquivalentTo(source.ViewStateFingerprints, imported.File);
                imported.AutoFilterFingerprints.Should().BeEquivalentTo(source.AutoFilterFingerprints, imported.File);
                imported.SheetProtectionFingerprints.Should().BeEquivalentTo(source.SheetProtectionFingerprints, imported.File);
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
        summaries.Sum(summary => summary.PrintAreas + summary.PrintTitleRows + summary.PrintTitleColumns)
            .Should()
            .BeGreaterThan(0);
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
        var hidden = hssf.CreateSheet("Hidden");
        hssf.SetActiveSheet(0);
        hssf.SetSelectedTab(0);
        hssf.SetSheetVisibility(1, SheetVisibility.Hidden);

        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 1));
        sheet.SetColumnWidth(1, 18 * 256);
        sheet.SetColumnHidden(2, true);
        sheet.CreateFreezePane(1, 1);
        sheet.DisplayGridlines = false;
        sheet.DisplayRowColHeadings = false;
        sheet.DisplayFormulas = true;
        sheet.TopRow = 2;
        hssf.GetCustomPalette().SetColorAtIndex(0x21, 255, 192, 0);
        sheet.TabColorIndex = 0x21;
        sheet.GroupColumn(5, 6);
        hidden.CreateSplitPane(2000, 3000, 2, 3, PanePosition.LowerRight);

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

        sheet.CreateRow(5).CreateCell(0).SetCellValue("outlined one");
        sheet.CreateRow(6).CreateCell(0).SetCellValue("outlined two");
        sheet.GroupRow(5, 6);

        var inputName = hssf.CreateName();
        inputName.NameName = "InputCell";
        inputName.RefersToFormula = "'Visible'!$A$2";
        inputName.Comment = "Primary input cell";

        var formulaName = hssf.CreateName();
        formulaName.NameName = "DoubleInput";
        formulaName.RefersToFormula = "Visible!$A$2*2";

        hssf.SetPrintArea(0, 0, 4, 0, 6);
        sheet.RepeatingRows = new CellRangeAddress(0, 1, -1, -1);
        sheet.RepeatingColumns = new CellRangeAddress(-1, -1, 0, 1);
        sheet.SetAutoFilter(new CellRangeAddress(0, 6, 0, 4));
        sheet.ProtectSheet("secret");
        sheet.SetMargin(MarginType.LeftMargin, 0.7);
        sheet.SetMargin(MarginType.RightMargin, 0.8);
        sheet.SetMargin(MarginType.TopMargin, 0.9);
        sheet.SetMargin(MarginType.BottomMargin, 1.0);
        sheet.IsPrintGridlines = true;
        sheet.IsPrintRowAndColumnHeadings = true;
        sheet.HorizontallyCenter = true;
        sheet.VerticallyCenter = true;
        sheet.FitToPage = true;
        sheet.Autobreaks = true;
        sheet.Header.Left = "Legacy";
        sheet.Header.Center = "Page &P";
        sheet.Header.Right = "&D";
        sheet.Footer.Left = "Left footer";
        sheet.Footer.Center = "Center footer";
        sheet.Footer.Right = "Right footer";
        sheet.SetRowBreak(4);
        sheet.SetColumnBreak(3);

        var printSetup = sheet.PrintSetup;
        printSetup.Landscape = true;
        printSetup.PaperSize = LegacyPaperSizeLetter;
        printSetup.FitWidth = 1;
        printSetup.FitHeight = 2;
        printSetup.LeftToRight = true;
        printSetup.UsePage = true;
        printSetup.PageStart = 3;
        printSetup.Copies = 2;
        printSetup.NoColor = true;
        printSetup.Draft = true;
        printSetup.HResolution = 600;
        printSetup.VResolution = 300;
        printSetup.HeaderMargin = 0.25;
        printSetup.FooterMargin = 0.35;
        printSetup.Notes = true;

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
        var freezePanes = 0;
        var rowOutlineLevels = 0;
        var colOutlineLevels = 0;
        var pageBreaks = 0;
        var sheetNames = new List<string>();
        var cellFingerprints = new List<string>();
        var hyperlinkFingerprints = new List<string>();
        var commentFingerprints = new List<string>();
        var paneFingerprints = new List<string>();
        var rowOutlineFingerprints = new List<string>();
        var colOutlineFingerprints = new List<string>();
        var printLayoutFingerprints = new List<string>();
        var pageSetupFingerprints = new List<string>();
        var viewStateFingerprints = new List<string>();
        var autoFilterFingerprints = new List<string>();
        var sheetProtectionFingerprints = new List<string>();
        var activeSheetIndex = hssf.ActiveSheetIndex >= 0 && hssf.ActiveSheetIndex < hssf.NumberOfSheets
            ? hssf.ActiveSheetIndex
            : (int?)null;
        var palette = hssf.GetCustomPalette();

        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            sheetNames.Add(sheet.SheetName);
            merges += sheet.NumMergedRegions;

            if (sheet.PaneInformation is { } pane && pane.IsFreezePane())
            {
                freezePanes++;
                paneFingerprints.Add(CreatePaneFingerprint(
                    sheetIndex,
                    sheet.SheetName,
                    (uint)pane.HorizontalSplitPosition,
                    (uint)pane.VerticalSplitPosition));
            }

            if (TryCreateRepeatRows(sheet.RepeatingRows, out var repeatRows))
                printLayoutFingerprints.Add(CreateRepeatRangeFingerprint(sheetIndex, sheet.SheetName, "Rows", repeatRows));
            if (TryCreateRepeatColumns(sheet.RepeatingColumns, out var repeatColumns))
                printLayoutFingerprints.Add(CreateRepeatRangeFingerprint(sheetIndex, sheet.SheetName, "Cols", repeatColumns));
            pageBreaks += sheet.RowBreaks.Count(breakIndex => ToSourceModelIndex(breakIndex) >= 2);
            pageBreaks += sheet.ColumnBreaks.Count(breakIndex => ToSourceModelIndex(breakIndex) >= 2);
            pageSetupFingerprints.Add(CreateSourcePageSetupFingerprint(sheetIndex, sheet.SheetName, sheet));
            viewStateFingerprints.Add(CreateSourceViewStateFingerprint(sheetIndex, sheet.SheetName, sheet, palette));
            if (sheet.Protect || sheet.ScenarioProtect || sheet is HSSFSheet { ObjectProtect: true })
                sheetProtectionFingerprints.Add(CreateSourceSheetProtectionFingerprint(sheetIndex, sheet.SheetName, sheet));

            for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                var row = sheet.GetRow(rowIndex);
                if (row is null)
                    continue;

                if (row.ZeroHeight || row.HeightInPoints > 0)
                    dimensions++;
                if (row.OutlineLevel > 0)
                {
                    rowOutlineLevels++;
                    rowOutlineFingerprints.Add(CreateOutlineFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        "R",
                        (uint)rowIndex + 1,
                        row.OutlineLevel));
                }

                foreach (var cell in row.Cells)
                {
                    if (IsSourceContentCell(cell))
                    {
                        cells++;
                        cellFingerprints.Add(CreateCellFingerprint(
                            sheetIndex,
                            sheet.SheetName,
                            (uint)cell.RowIndex + 1,
                            (uint)cell.ColumnIndex + 1,
                            cell.CellType == CellType.Formula ? NormalizeFormulaText(cell.CellFormula) : "",
                            SourceValueToken(cell, cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType)));
                    }

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

            for (var columnIndex = 0; columnIndex <= LegacyXlsMaxColumnIndex; columnIndex++)
            {
                var outlineLevel = sheet.GetColumnOutlineLevel(columnIndex);
                if (outlineLevel > 0)
                {
                    colOutlineLevels++;
                    colOutlineFingerprints.Add(CreateOutlineFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        "C",
                        (uint)columnIndex + 1,
                        outlineLevel));
                }
            }

            if (sheet is HSSFSheet hssfSheet)
            {
                var sheetHyperlinks = hssfSheet.GetHyperlinkList();
                hyperlinks += sheetHyperlinks.Count;
                hyperlinkFingerprints.AddRange(sheetHyperlinks
                    .Select(link => CreateAddressedFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        (uint)link.FirstRow + 1,
                        (uint)link.FirstColumn + 1,
                        GetSourceHyperlinkTarget(link))));

                var sheetComments = hssfSheet.GetCellComments();
                comments += sheetComments.Count;
                commentFingerprints.AddRange(sheetComments
                    .Select(pair => CreateAddressedFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        (uint)pair.Key.Row + 1,
                        (uint)pair.Key.Column + 1,
                        pair.Value.String?.String ?? "")));
            }
        }

        var validationWorkbook = new Workbook("DefinedNameValidation");
        var definedNameFingerprints = Enumerable.Range(0, hssf.NumberOfNames)
            .Select(hssf.GetNameAt)
            .Where(name => IsImportableDefinedName(name, validationWorkbook))
            .Select(name => name.NameName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        autoFilterFingerprints.AddRange(ReadSourceAutoFilterFingerprints(hssf));
        printLayoutFingerprints.AddRange(ReadSourcePrintLayoutFingerprints(hssf));
        var orderedPrintLayoutFingerprints = printLayoutFingerprints
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var orderedAutoFilterFingerprints = autoFilterFingerprints
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

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
            definedNameFingerprints.Length,
            hyperlinks,
            comments,
            freezePanes,
            rowOutlineLevels,
            colOutlineLevels,
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Area|", StringComparison.Ordinal)),
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Rows|", StringComparison.Ordinal)),
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Cols|", StringComparison.Ordinal)),
            orderedAutoFilterFingerprints.Length,
            sheetProtectionFingerprints.Count,
            pageSetupFingerprints.Count,
            pageBreaks,
            activeSheetIndex,
            SheetNames: sheetNames,
            CellFingerprints: cellFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DefinedNameFingerprints: definedNameFingerprints,
            HyperlinkFingerprints: hyperlinkFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            CommentFingerprints: commentFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PaneFingerprints: paneFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            RowOutlineFingerprints: rowOutlineFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ColOutlineFingerprints: colOutlineFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PrintLayoutFingerprints: orderedPrintLayoutFingerprints,
            PageSetupFingerprints: pageSetupFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ViewStateFingerprints: viewStateFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            AutoFilterFingerprints: orderedAutoFilterFingerprints,
            SheetProtectionFingerprints: sheetProtectionFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray());
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
            FreezePanes: 0,
            RowOutlineLevels: 0,
            ColOutlineLevels: 0,
            PrintAreas: 0,
            PrintTitleRows: 0,
            PrintTitleColumns: 0,
            AutoFilters: 0,
            ProtectedSheets: 0,
            PageSetupSheets: 0,
            PageBreaks: 0,
            ActiveSheetIndex: null,
            RichMetadata: false,
            SheetNames: [],
            CellFingerprints: [],
            DefinedNameFingerprints: [],
            HyperlinkFingerprints: [],
            CommentFingerprints: [],
            PaneFingerprints: [],
            RowOutlineFingerprints: [],
            ColOutlineFingerprints: [],
            PrintLayoutFingerprints: [],
            PageSetupFingerprints: [],
            ViewStateFingerprints: [],
            AutoFilterFingerprints: [],
            SheetProtectionFingerprints: []);
    }

    private static IReadOnlyList<string> ReadImportedCellFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.EnumerateCells()
                .OrderBy(entry => entry.Address.Row)
                .ThenBy(entry => entry.Address.Col)
                .Select(entry => CreateCellFingerprint(
                    sheetIndex,
                    sheet.Name,
                    entry.Address.Row,
                    entry.Address.Col,
                    NormalizeFormulaText(entry.Cell.FormulaText ?? ""),
                    ImportedValueToken(entry.Cell.Value))))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedDefinedNameFingerprints(Workbook workbook) =>
        workbook.NamedRanges.Keys
            .Concat(workbook.NamedFormulas.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedHyperlinkFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.Hyperlinks
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => CreateAddressedFingerprint(
                    sheetIndex,
                    sheet.Name,
                    entry.Key.Row,
                    entry.Key.Col,
                    entry.Value)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedCommentFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.Comments
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => CreateAddressedFingerprint(
                    sheetIndex,
                    sheet.Name,
                    entry.Key.Row,
                    entry.Key.Col,
                    entry.Value)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedPaneFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => new { Sheet = sheet, SheetIndex = sheetIndex })
            .Where(entry => entry.Sheet.FrozenRows > 0 || entry.Sheet.FrozenCols > 0)
            .Select(entry => CreatePaneFingerprint(
                entry.SheetIndex,
                entry.Sheet.Name,
                entry.Sheet.FrozenRows,
                entry.Sheet.FrozenCols))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedRowOutlineFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.RowOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => CreateOutlineFingerprint(sheetIndex, sheet.Name, "R", pair.Key, pair.Value)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedColOutlineFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.ColOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => CreateOutlineFingerprint(sheetIndex, sheet.Name, "C", pair.Key, pair.Value)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedPrintLayoutFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) =>
            {
                var fingerprints = new List<string>();
                if (sheet.PrintArea is { } printArea)
                    fingerprints.Add(CreatePrintAreaFingerprint(sheetIndex, sheet.Name, printArea));
                if (sheet.PrintTitleRows is { } rows)
                    fingerprints.Add(CreateRepeatRangeFingerprint(sheetIndex, sheet.Name, "Rows", rows));
                if (sheet.PrintTitleColumns is { } columns)
                    fingerprints.Add(CreateRepeatRangeFingerprint(sheetIndex, sheet.Name, "Cols", columns));
                return fingerprints;
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedAutoFilterFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => sheet.AutoFilter?.Reference is { } reference
                ? CreateAutoFilterFingerprint(sheetIndex, sheet.Name, reference)
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedSheetProtectionFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => sheet.IsProtected
                ? CreateSheetProtectionFingerprint(
                    sheetIndex,
                    sheet.Name,
                    sheet.ProtectionPassword,
                    GetProtectionMetadataAttribute(sheet, "objects") == "1",
                    GetProtectionMetadataAttribute(sheet, "scenarios") == "1")
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedPageSetupFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreatePageSetupFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.PageOrientation,
                sheet.PaperSize,
                sheet.PageMargins,
                sheet.HeaderMargin,
                sheet.FooterMargin,
                sheet.PrintGridlines,
                sheet.PrintHeadings,
                sheet.CenterHorizontallyOnPage,
                sheet.CenterVerticallyOnPage,
                sheet.FitToPage,
                sheet.AutoPageBreaks,
                sheet.ScaleToFit,
                sheet.PageOrder,
                sheet.FirstPageNumber,
                sheet.PrintCopies,
                sheet.PrintBlackAndWhite,
                sheet.PrintDraftQuality,
                sheet.PrintQualityDpi,
                sheet.PrintQualityVerticalDpi,
                sheet.PrintComments,
                sheet.PageHeader,
                sheet.PageFooter,
                sheet.RowPageBreaks,
                sheet.ColumnPageBreaks))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedViewStateFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreateViewStateFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.ShowGridlines,
                sheet.ShowHeadings,
                sheet.ShowFormulas,
                sheet.ViewTopRow,
                sheet.ViewLeftCol,
                sheet.SplitRow,
                sheet.SplitColumn,
                sheet.TabColor))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadSourcePrintLayoutFingerprints(HSSFWorkbook hssf)
    {
        var workbook = new Workbook("PrintLayoutSource");
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
            workbook.AddSheet(hssf.GetSheetName(sheetIndex));

        for (var nameIndex = 0; nameIndex < hssf.NumberOfNames; nameIndex++)
        {
            var name = hssf.GetNameAt(nameIndex);
            if (name is null || name.IsDeleted || name.IsFunctionName)
                continue;

            TryLoadSourcePrintDefinedName(workbook, name);
        }

        return ReadImportedPrintLayoutFingerprints(workbook);
    }

    private static IReadOnlyList<string> ReadSourceAutoFilterFingerprints(HSSFWorkbook hssf)
    {
        var workbook = new Workbook("AutoFilterSource");
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
            workbook.AddSheet(hssf.GetSheetName(sheetIndex));

        for (var index = 0; index < hssf.NumberOfNames; index++)
        {
            var name = hssf.GetNameAt(index);
            if (name is not null && !name.IsDeleted && IsAutoFilterDefinedName(name.NameName))
                TryLoadSourceAutoFilterDefinedName(workbook, name);
        }

        return ReadImportedAutoFilterFingerprints(workbook);
    }

    private static string CreateSourcePageSetupFingerprint(int sheetIndex, string sheetName, ISheet sheet)
    {
        var printSetup = sheet.PrintSetup;
        var scaleToFit = printSetup.FitWidth > 0 || printSetup.FitHeight > 0
            ? new WorksheetScaleToFit(null, PositiveOrNull(printSetup.FitWidth), PositiveOrNull(printSetup.FitHeight))
            : new WorksheetScaleToFit(PositiveOrDefault(printSetup.Scale, 100), null, null);
        var rowBreaks = sheet.RowBreaks
            .Select(ToSourceModelIndex)
            .Where(index => index >= 2)
            .Order()
            .ToArray();
        var columnBreaks = sheet.ColumnBreaks
            .Select(ToSourceModelIndex)
            .Where(index => index >= 2)
            .Order()
            .ToArray();

        return CreatePageSetupFingerprint(
            sheetIndex,
            sheetName,
            printSetup.Landscape ? WorksheetPageOrientation.Landscape : WorksheetPageOrientation.Portrait,
            MapSourcePaperSize(printSetup.PaperSize),
            new WorksheetPageMargins(
                ValidMarginOrDefault(sheet.GetMargin(MarginType.LeftMargin), WorksheetPageMargins.Narrow.Left),
                ValidMarginOrDefault(sheet.GetMargin(MarginType.RightMargin), WorksheetPageMargins.Narrow.Right),
                ValidMarginOrDefault(sheet.GetMargin(MarginType.TopMargin), WorksheetPageMargins.Narrow.Top),
                ValidMarginOrDefault(sheet.GetMargin(MarginType.BottomMargin), WorksheetPageMargins.Narrow.Bottom)),
            ValidMarginOrDefault(printSetup.HeaderMargin, 0.3),
            ValidMarginOrDefault(printSetup.FooterMargin, 0.3),
            sheet.IsPrintGridlines,
            sheet.IsPrintRowAndColumnHeadings,
            sheet.HorizontallyCenter,
            sheet.VerticallyCenter,
            sheet.FitToPage,
            sheet.Autobreaks,
            scaleToFit,
            printSetup.LeftToRight ? WorksheetPageOrder.OverThenDown : WorksheetPageOrder.DownThenOver,
            printSetup.UsePage && printSetup.PageStart > 0 ? printSetup.PageStart : null,
            printSetup.Copies > 0 ? printSetup.Copies : null,
            printSetup.NoColor,
            printSetup.Draft,
            printSetup.HResolution > 0 ? printSetup.HResolution : null,
            printSetup.VResolution > 0 && printSetup.VResolution != printSetup.HResolution ? printSetup.VResolution : null,
            printSetup.Notes ? WorksheetPrintComments.AtEnd : WorksheetPrintComments.None,
            ToWorksheetHeaderFooter(sheet.Header),
            ToWorksheetHeaderFooter(sheet.Footer),
            rowBreaks,
            columnBreaks);
    }

    private static string CreateSourceViewStateFingerprint(
        int sheetIndex,
        string sheetName,
        ISheet sheet,
        HSSFPalette palette)
    {
        var pane = sheet.PaneInformation;
        uint? splitRow = null;
        uint? splitColumn = null;
        if (pane is not null && !pane.IsFreezePane())
        {
            if (pane.HorizontalSplitPosition > 0 && pane.HorizontalSplitTopRow >= 0)
                splitRow = ToSourceModelIndex(pane.HorizontalSplitTopRow);
            if (pane.VerticalSplitPosition > 0 && pane.VerticalSplitLeftColumn >= 0)
                splitColumn = ToSourceModelIndex(pane.VerticalSplitLeftColumn);
        }

        return CreateViewStateFingerprint(
            sheetIndex,
            sheetName,
            sheet.DisplayGridlines,
            sheet.DisplayRowColHeadings,
            sheet.DisplayFormulas,
            sheet.TopRow > 0 ? ToSourceModelIndex(sheet.TopRow) : null,
            null,
            splitRow,
            splitColumn,
            TryGetSourceTabColor(sheet, palette, out var tabColor) ? tabColor : null);
    }

    private static string CreateSourceSheetProtectionFingerprint(int sheetIndex, string sheetName, ISheet sheet) =>
        CreateSheetProtectionFingerprint(
            sheetIndex,
            sheetName,
            sheet is HSSFSheet { Password: not 0 } protectedSheet
                ? ((ushort)protectedSheet.Password).ToString("X4", CultureInfo.InvariantCulture)
                : null,
            sheet is HSSFSheet { ObjectProtect: true },
            sheet.ScenarioProtect);

    private static string CreateCellFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string formula,
        string value) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|F={formula}|V={value}";

    private static string CreateAddressedFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string value) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|{value}";

    private static string CreatePaneFingerprint(
        int sheetIndex,
        string sheetName,
        uint frozenRows,
        uint frozenColumns) =>
        $"{sheetIndex}:{sheetName}|Freeze|R={frozenRows}|C={frozenColumns}";

    private static string CreateOutlineFingerprint(
        int sheetIndex,
        string sheetName,
        string axis,
        uint index,
        int level) =>
        $"{sheetIndex}:{sheetName}|{axis}{index}|L={level}";

    private static string CreatePrintAreaFingerprint(
        int sheetIndex,
        string sheetName,
        GridRange range) =>
        $"{sheetIndex}:{sheetName}|Area|{range.Start.ToA1()}:{range.End.ToA1()}";

    private static string CreateRepeatRangeFingerprint(
        int sheetIndex,
        string sheetName,
        string axis,
        WorksheetRepeatRange range) =>
        $"{sheetIndex}:{sheetName}|{axis}|{range.Start}:{range.End}";

    private static string CreateAutoFilterFingerprint(int sheetIndex, string sheetName, string reference) =>
        $"{sheetIndex}:{sheetName}|AutoFilter|{reference}";

    private static string CreateSheetProtectionFingerprint(
        int sheetIndex,
        string sheetName,
        string? passwordHash,
        bool objectProtected,
        bool scenarioProtected) =>
        $"{sheetIndex}:{sheetName}|Protection|Password={passwordHash ?? ""}|Objects={objectProtected}|Scenarios={scenarioProtected}";

    private static string CreatePageSetupFingerprint(
        int sheetIndex,
        string sheetName,
        WorksheetPageOrientation orientation,
        WorksheetPaperSize paperSize,
        WorksheetPageMargins margins,
        double headerMargin,
        double footerMargin,
        bool printGridlines,
        bool printHeadings,
        bool centerHorizontally,
        bool centerVertically,
        bool? fitToPage,
        bool? autoPageBreaks,
        WorksheetScaleToFit scaleToFit,
        WorksheetPageOrder pageOrder,
        int? firstPageNumber,
        int? printCopies,
        bool blackAndWhite,
        bool draftQuality,
        int? printQualityDpi,
        int? printQualityVerticalDpi,
        WorksheetPrintComments printComments,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        IEnumerable<uint> rowBreaks,
        IEnumerable<uint> columnBreaks) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            $"Orient={orientation}",
            $"Paper={paperSize}",
            $"Margins={FormatDouble(margins.Left)},{FormatDouble(margins.Right)},{FormatDouble(margins.Top)},{FormatDouble(margins.Bottom)}",
            $"HeaderFooterMargins={FormatDouble(headerMargin)},{FormatDouble(footerMargin)}",
            $"Print={printGridlines},{printHeadings}",
            $"Center={centerHorizontally},{centerVertically}",
            $"Flags={FormatNullableBool(fitToPage)},{FormatNullableBool(autoPageBreaks)}",
            $"Scale={FormatNullableInt(scaleToFit.ScalePercent)},{FormatNullableInt(scaleToFit.FitToPagesWide)},{FormatNullableInt(scaleToFit.FitToPagesTall)}",
            $"Order={pageOrder}",
            $"FirstPage={FormatNullableInt(firstPageNumber)}",
            $"Copies={FormatNullableInt(printCopies)}",
            $"Quality={blackAndWhite},{draftQuality},{FormatNullableInt(printQualityDpi)},{FormatNullableInt(printQualityVerticalDpi)}",
            $"Comments={printComments}",
            $"Header={FormatHeaderFooter(header)}",
            $"Footer={FormatHeaderFooter(footer)}",
            $"RowBreaks={FormatBreaks(rowBreaks)}",
            $"ColBreaks={FormatBreaks(columnBreaks)}"
        ]);

    private static string CreateViewStateFingerprint(
        int sheetIndex,
        string sheetName,
        bool showGridlines,
        bool showHeadings,
        bool showFormulas,
        uint? viewTopRow,
        uint? viewLeftCol,
        uint? splitRow,
        uint? splitColumn,
        CellColor? tabColor) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            $"Display={showGridlines},{showHeadings},{showFormulas}",
            $"TopLeft={FormatNullableUInt(viewTopRow)},{FormatNullableUInt(viewLeftCol)}",
            $"Split={FormatNullableUInt(splitRow)},{FormatNullableUInt(splitColumn)}",
            $"TabColor={FormatColor(tabColor)}"
        ]);

    private static string FormatDouble(double value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string FormatNullableInt(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string FormatNullableUInt(uint? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string FormatHeaderFooter(WorksheetHeaderFooter value) =>
        $"{EscapeToken(value.Left)},{EscapeToken(value.Center)},{EscapeToken(value.Right)}";

    private static string FormatColor(CellColor? value) =>
        value is { } color
            ? $"{color.R},{color.G},{color.B}"
            : "null";

    private static string EscapeToken(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\p", StringComparison.Ordinal)
            .Replace(",", "\\c", StringComparison.Ordinal);

    private static string FormatBreaks(IEnumerable<uint> breaks) =>
        string.Join(",", breaks.Order());

    private static uint ToSourceModelIndex(int zeroBasedIndex) =>
        (uint)zeroBasedIndex + 1;

    private static WorksheetHeaderFooter ToWorksheetHeaderFooter(IHeaderFooter headerFooter)
    {
        if (headerFooter is NPOI.HSSF.UserModel.HeaderFooter legacyHeaderFooter)
            return ParseHeaderFooterRawText(legacyHeaderFooter.RawText);

        return new(headerFooter.Left ?? "", headerFooter.Center ?? "", headerFooter.Right ?? "");
    }

    private static WorksheetHeaderFooter ParseHeaderFooterRawText(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return new WorksheetHeaderFooter("", "", "");

        var left = new StringBuilder();
        var center = new StringBuilder();
        var right = new StringBuilder();
        var current = center;

        for (var index = 0; index < rawText.Length; index++)
        {
            if (rawText[index] == '&' && index + 1 < rawText.Length)
            {
                current = rawText[index + 1] switch
                {
                    'L' => left,
                    'C' => center,
                    'R' => right,
                    _ => current
                };

                if (rawText[index + 1] is 'L' or 'C' or 'R')
                {
                    index++;
                    continue;
                }
            }

            current.Append(rawText[index]);
        }

        return new WorksheetHeaderFooter(left.ToString(), center.ToString(), right.ToString());
    }

    private static WorksheetPaperSize MapSourcePaperSize(short paperSize) =>
        paperSize switch
        {
            LegacyPaperSizeLetter => WorksheetPaperSize.Letter,
            LegacyPaperSizeLegal => WorksheetPaperSize.Legal,
            LegacyPaperSizeA4 => WorksheetPaperSize.A4,
            _ => WorksheetPaperSize.A4
        };

    private static int? PositiveOrNull(short value) =>
        value > 0 ? value : null;

    private static int PositiveOrDefault(short value, int defaultValue) =>
        value > 0 ? value : defaultValue;

    private static double ValidMarginOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value >= 0 ? value : defaultValue;

    private static bool TryGetSourceTabColor(ISheet sheet, HSSFPalette palette, out CellColor tabColor)
    {
        tabColor = default;
        if (sheet is not HSSFSheet hssfSheet)
            return false;

        try
        {
            if (hssfSheet.IsAutoTabColor)
                return false;

            var color = palette.GetColor(hssfSheet.TabColorIndex);
            if (color is null)
                return false;

            var triplet = color.GetTriplet();
            if (triplet.Length < 3)
                return false;

            tabColor = new CellColor(triplet[0], triplet[1], triplet[2]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetProtectionMetadataAttribute(Sheet sheet, string name)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(sheet.ProtectionMetadata?.Get("sheetProtection"));
        return attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static string SourceValueToken(ICell cell, CellType cellType) =>
        cellType switch
        {
            CellType.Numeric when DateUtil.IsCellDateFormatted(cell) && cell.DateCellValue is { } date =>
                $"Date:{date.ToOADate().ToString("R", CultureInfo.InvariantCulture)}",
            CellType.Numeric => $"Number:{cell.NumericCellValue.ToString("R", CultureInfo.InvariantCulture)}",
            CellType.Boolean => $"Bool:{cell.BooleanCellValue}",
            CellType.String => $"Text:{cell.StringCellValue}",
            CellType.Error => $"Error:{FormulaError.ForInt(cell.ErrorCellValue).String}",
            _ => "Blank:"
        };

    private static string ImportedValueToken(ScalarValue value) =>
        value switch
        {
            NumberValue number => $"Number:{number.Value.ToString("R", CultureInfo.InvariantCulture)}",
            DateTimeValue date => $"Date:{date.Value.ToString("R", CultureInfo.InvariantCulture)}",
            BoolValue boolean => $"Bool:{boolean.Value}",
            TextValue text => $"Text:{text.Value}",
            ErrorValue error => $"Error:{error.Code}",
            BlankValue => "Blank:",
            _ => value.ToString() ?? ""
        };

    private static string NormalizeFormulaText(string formula) =>
        formula.StartsWith('=') ? formula[1..] : formula;

    private static string GetSourceHyperlinkTarget(IHyperlink hyperlink)
    {
        var address = hyperlink.Address ?? "";
        if (hyperlink is HSSFHyperlink hssfHyperlink &&
            hyperlink.Type == HyperlinkType.Document &&
            !string.IsNullOrWhiteSpace(hssfHyperlink.TextMark))
        {
            return string.IsNullOrWhiteSpace(address) ? hssfHyperlink.TextMark : $"{address}#{hssfHyperlink.TextMark}";
        }

        return address;
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

    private static bool TryLoadSourcePrintDefinedName(Workbook workbook, IName definedName)
    {
        if (!IsPrintAreaDefinedName(definedName.NameName) &&
            !IsPrintTitlesDefinedName(definedName.NameName))
        {
            return false;
        }

        var refersTo = NormalizeFormulaText(definedName.RefersToFormula ?? "");
        if (string.IsNullOrWhiteSpace(refersTo))
            return true;

        if (IsPrintAreaDefinedName(definedName.NameName))
        {
            foreach (var reference in SplitFormulaReferences(refersTo))
            {
                if (TryParseNamedRangeRefersTo(workbook, reference, out var printArea) &&
                    workbook.GetSheet(printArea.Start.Sheet) is { } sheet)
                {
                    sheet.PrintArea = printArea;
                    break;
                }
            }

            return true;
        }

        foreach (var reference in SplitFormulaReferences(refersTo))
            TryLoadPrintTitleReference(workbook, reference);

        return true;
    }

    private static bool TryLoadSourceAutoFilterDefinedName(Workbook workbook, IName definedName)
    {
        if (!IsAutoFilterDefinedName(definedName.NameName))
            return false;

        if (TryParseNamedRangeRefersTo(workbook, definedName.RefersToFormula, out var range) &&
            workbook.GetSheet(range.Start.Sheet) is { } sheet)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        }

        return true;
    }

    private static bool TryLoadPrintTitleReference(Workbook workbook, string reference)
    {
        if (!TrySplitSheetQualifiedReference(reference.Trim(), out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        if (TryParseRepeatRows(rangeText, out var rows))
        {
            sheet.PrintTitleRows = rows;
            return true;
        }

        if (TryParseRepeatColumns(rangeText, out var columns))
        {
            sheet.PrintTitleColumns = columns;
            return true;
        }

        return false;
    }

    private static bool TryParseNamedRangeRefersTo(Workbook workbook, string? refersTo, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(refersTo))
            return false;

        var text = NormalizeFormulaText(refersTo).Trim();
        if (!TrySplitSheetQualifiedReference(text, out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        var parts = rangeText.Split(':');
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseA1Part(parts[0], sheet.Id, out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseA1Part(endText, sheet.Id, out var end))
            return false;

        range = new GridRange(start, end);
        return true;
    }

    private static bool TrySplitSheetQualifiedReference(string text, out string sheetName, out string rangeText)
    {
        sheetName = "";
        rangeText = "";
        if (text.Length == 0)
            return false;

        if (text[0] == '\'')
        {
            var builder = new StringBuilder();
            for (var index = 1; index < text.Length; index++)
            {
                if (text[index] != '\'')
                {
                    builder.Append(text[index]);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index++;
                    continue;
                }

                if (index + 1 >= text.Length || text[index + 1] != '!')
                    return false;

                sheetName = builder.ToString();
                rangeText = text[(index + 2)..].Trim();
                return rangeText.Length > 0;
            }

            return false;
        }

        var separator = text.IndexOf('!', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        sheetName = text[..separator].Trim();
        rangeText = text[(separator + 1)..].Trim();
        return sheetName.Length > 0 && rangeText.Length > 0;
    }

    private static bool TryParseA1Part(string text, SheetId sheetId, out ModelCellAddress address)
    {
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        return ModelCellAddress.TryParse(normalized, sheetId, out address);
    }

    private static IEnumerable<string> SplitFormulaReferences(string formula)
    {
        var start = 0;
        var inQuote = false;
        for (var index = 0; index < formula.Length; index++)
        {
            if (formula[index] == '\'')
            {
                if (inQuote && index + 1 < formula.Length && formula[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && formula[index] == ',')
            {
                var token = formula[start..index].Trim();
                if (token.Length > 0)
                    yield return token;
                start = index + 1;
            }
        }

        var lastToken = formula[start..].Trim();
        if (lastToken.Length > 0)
            yield return lastToken;
    }

    private static bool TryParseRepeatRows(string rangeText, out WorksheetRepeatRange rows)
    {
        rows = default;
        var parts = rangeText.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseRowReference(parts[0], out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseRowReference(endText, out var end) ||
            start < 1 ||
            start > end ||
            end > ModelCellAddress.MaxRow)
        {
            return false;
        }

        rows = new WorksheetRepeatRange(start, end);
        return true;
    }

    private static bool TryCreateRepeatRows(CellRangeAddress? range, out WorksheetRepeatRange rows)
    {
        rows = default;
        if (range is null ||
            range.FirstRow < 0 ||
            range.LastRow < range.FirstRow)
        {
            return false;
        }

        rows = new WorksheetRepeatRange((uint)range.FirstRow + 1, (uint)range.LastRow + 1);
        return true;
    }

    private static bool TryParseRepeatColumns(string rangeText, out WorksheetRepeatRange columns)
    {
        columns = default;
        var parts = rangeText.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseColumnReference(parts[0], out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseColumnReference(endText, out var end) ||
            start < 1 ||
            start > end ||
            end > ModelCellAddress.MaxCol)
        {
            return false;
        }

        columns = new WorksheetRepeatRange(start, end);
        return true;
    }

    private static bool TryCreateRepeatColumns(CellRangeAddress? range, out WorksheetRepeatRange columns)
    {
        columns = default;
        if (range is null ||
            range.FirstColumn < 0 ||
            range.LastColumn < range.FirstColumn)
        {
            return false;
        }

        columns = new WorksheetRepeatRange((uint)range.FirstColumn + 1, (uint)range.LastColumn + 1);
        return true;
    }

    private static bool TryParseRowReference(string text, out uint row) =>
        uint.TryParse(text.Trim().Replace("$", "", StringComparison.Ordinal), out row);

    private static bool TryParseColumnReference(string text, out uint column)
    {
        column = default;
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        if (normalized.Length == 0 || normalized.Any(character => !IsAsciiLetter(character)))
            return false;

        try
        {
            column = ModelCellAddress.ColumnNameToNumber(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsPrintAreaDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "Print_Area");

    private static bool IsPrintTitlesDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "Print_Titles");

    private static bool IsAutoFilterDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "_FilterDatabase") ||
        IsBuiltInDefinedName(name, "FilterDatabase");

    private static bool IsBuiltInDefinedName(string? name, string builtInName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmedName = name.Trim();
        return string.Equals(trimmedName, builtInName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "_xlnm." + builtInName, StringComparison.OrdinalIgnoreCase);
    }

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
        int FreezePanes,
        int RowOutlineLevels,
        int ColOutlineLevels,
        int PrintAreas,
        int PrintTitleRows,
        int PrintTitleColumns,
        int AutoFilters,
        int ProtectedSheets,
        int PageSetupSheets,
        int PageBreaks,
        int? ActiveSheetIndex,
        bool RichMetadata = true,
        IReadOnlyList<string>? SheetNames = null,
        IReadOnlyList<string>? CellFingerprints = null,
        IReadOnlyList<string>? DefinedNameFingerprints = null,
        IReadOnlyList<string>? HyperlinkFingerprints = null,
        IReadOnlyList<string>? CommentFingerprints = null,
        IReadOnlyList<string>? PaneFingerprints = null,
        IReadOnlyList<string>? RowOutlineFingerprints = null,
        IReadOnlyList<string>? ColOutlineFingerprints = null,
        IReadOnlyList<string>? PrintLayoutFingerprints = null,
        IReadOnlyList<string>? PageSetupFingerprints = null,
        IReadOnlyList<string>? ViewStateFingerprints = null,
        IReadOnlyList<string>? AutoFilterFingerprints = null,
        IReadOnlyList<string>? SheetProtectionFingerprints = null);

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
