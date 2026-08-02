using FluentAssertions;
using ExcelDataReader;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.Model;
using NPOI.HSSF.Record;
using NPOI.HSSF.Record.PivotTable;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ModelBorderStyle = FreeX.Core.Model.BorderStyle;
using ModelCellAddress = FreeX.Core.Model.CellAddress;
using ModelCellStyle = FreeX.Core.Model.CellStyle;
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

    private static readonly FieldInfo? LbsSelectedIndexField =
        typeof(LbsDataSubRecord).GetField("_iSel", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? UnknownRecordRawDataField =
        typeof(UnknownRecord).GetField("_rawData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? TabIdRecordTabIdsField =
        typeof(TabIdRecord).GetField("_tabids", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? UseSelFsRecordOptionsField =
        typeof(UseSelFSRecord).GetField("_options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? HssfGetObjRecordMethod =
        typeof(HSSFSimpleShape).GetMethod(
            "GetObjRecord",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
    public void Load_DateCellFrom1904Workbook_FeedsCorrectCalendarValuesToDateFunctions()
    {
        // Regression for the 1904-date-system storage/interpretation mismatch (review finding F4) in the
        // legacy .xls (NPOI) path: a date cell loaded from a 1904-system workbook was stored as a
        // 1900-epoch OADate serial, while the 1904-aware date functions (YEAR/MONTH/DAY/...) reinterpret
        // that same serial as day-count-since-1904-01-01 when Workbook.Uses1904DateSystem is true — so
        // every date formula was off by the 1462-day (~4-year) epoch difference. The fix stores the
        // 1904-epoch-relative serial on load (LegacyXlsFileAdapter.MapDateTimeValue) so storage and
        // function interpretation agree.
        var knownDate = new DateTime(2024, 6, 15);
        using var stream = BuildHssfWorkbookWithDateCell(knownDate, uses1904DateSystem: true);

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        workbook.Uses1904DateSystem.Should().BeTrue();

        var sheet = workbook.GetSheetAt(0);
        sheet.GetValue(1, 1).Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().BeApproximately((knownDate - new DateTime(1904, 1, 1)).TotalDays, 1e-6,
                "the stored serial must be the 1904-epoch-relative serial the date functions expect, not the 1462-day-larger 1900 OADate");

        sheet.SetFormula(new ModelCellAddress(sheet.Id, 2, 1), "YEAR(A1)");
        sheet.SetFormula(new ModelCellAddress(sheet.Id, 3, 1), "MONTH(A1)");
        sheet.SetFormula(new ModelCellAddress(sheet.Id, 4, 1), "DAY(A1)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(2024), "YEAR() must not be off by the ~4-year 1904 epoch shift");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Load_DateCellFromDefault1900Workbook_StoresUnshiftedOADateSerial()
    {
        // Control for the conditional in LegacyXlsFileAdapter.MapDateTimeValue: a non-1904 workbook must
        // still store the plain 1900-epoch OADate serial (no 1462-day shift) so the common case keeps its
        // correct calendar value — i.e. the 1904 fix does not regress ordinary .xls files.
        var knownDate = new DateTime(2024, 6, 15);
        using var stream = BuildHssfWorkbookWithDateCell(knownDate, uses1904DateSystem: false);

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        workbook.Uses1904DateSystem.Should().BeFalse();

        var sheet = workbook.GetSheetAt(0);
        sheet.GetValue(1, 1).Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().BeApproximately(knownDate.ToOADate(), 1e-6);

        sheet.SetFormula(new ModelCellAddress(sheet.Id, 2, 1), "YEAR(A1)");
        sheet.SetFormula(new ModelCellAddress(sheet.Id, 3, 1), "MONTH(A1)");
        sheet.SetFormula(new ModelCellAddress(sheet.Id, 4, 1), "DAY(A1)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(2024));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void R114_Load_LegacyCseArrayFormula_ConfinesResultToDeclaredExtentOnRecalc()
    {
        // Regression for the .xls (BIFF8/NPOI) counterpart of the XlsxFileAdapter legacy-CSE-array fix:
        // LoadCells detects a multi-cell CSE array formula via NPOI's IsPartOfArrayFormulaGroup/
        // ArrayFormulaRange and correctly marks the anchor ArrayMode.Dynamic, but (before the fix) never
        // set Cell.LegacyArrayRows/LegacyArrayCols, so RecalcEngine's confining branch (RecalcEngine.cs
        // line 399, "if (cell.LegacyArrayRows > 0)") never fired and the formula free-spilled like a
        // modern dynamic array instead of staying confined to its originally CSE-selected range, exactly
        // like R80_LegacyCseArrayFixedExtentTests but exercised through the real .xls loader entry point
        // (LegacyXlsFileAdapter.Load) rather than by hand-setting LegacyArrayRows/Cols on a model Cell.
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");

        // A1:A3 = 1,2,3 (a 3-row x 1-col column).
        var row0 = sheet.CreateRow(0);
        row0.CreateCell(0).SetCellValue(1);
        var row1 = sheet.CreateRow(1);
        row1.CreateCell(0).SetCellValue(2);
        var row2 = sheet.CreateRow(2);
        row2.CreateCell(0).SetCellValue(3);

        // H1:I1 (row 0, cols 7-8) was CSE-entered as {=TRANSPOSE(A1:A3)}: a 1-row x 2-col selection
        // over a formula whose natural result is 1x3. Excel fills only H1/I1 and silently drops the
        // third transposed value; J1 (col 9) is never touched, no matter how large the natural result is.
        sheet.SetArrayFormula("TRANSPOSE(A1:A3)", new CellRangeAddress(0, 0, 7, 8));

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        var modelSheet = workbook.GetSheetAt(0);

        var h1 = new ModelCellAddress(modelSheet.Id, 1, 8);
        var loadedAnchor = modelSheet.GetCell(1, 8);
        loadedAnchor.Should().NotBeNull();
        loadedAnchor!.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        loadedAnchor.LegacyArrayRows.Should().Be(1u, "the loader must confine this to the originally CSE-declared 1-row extent");
        loadedAnchor.LegacyArrayCols.Should().Be(2u, "the loader must confine this to the originally CSE-declared 2-col extent");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        modelSheet.GetValue(1, 8).Should().Be(new NumberValue(1), "H1 gets the first transposed value");
        modelSheet.GetValue(1, 9).Should().Be(new NumberValue(2), "I1 gets the second transposed value");
        modelSheet.GetValue(1, 10).Should().Be(BlankValue.Instance,
            "J1 sits outside the originally declared H1:I1 ref range and Excel's legacy CSE semantics " +
            "never grow into it, unlike a modern dynamic-array spill");
        modelSheet.GetCell(new ModelCellAddress(modelSheet.Id, 1, 10)).Should().BeNull(
            "J1 must not gain a spill-value/cell entry at all -- confirming the formula never free-spilled");
    }

    [Fact]
    public void R114_Load_LegacyCseArrayFormula_DeclaredRangeMatchesNaturalResult_FillsEveryDeclaredCell()
    {
        // No-regression sibling: when the CSE-declared range exactly matches the formula's natural
        // result size, every declared cell must still be filled correctly through the real .xls loader
        // (the confining branch is a no-op path when declaredRows/Cols == rv.RowCount/ColCount).
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Sheet1");

        // A1:B2 = a genuine 2x2 block of values.
        var row0 = sheet.CreateRow(0);
        row0.CreateCell(0).SetCellValue(1);
        row0.CreateCell(1).SetCellValue(2);
        var row1 = sheet.CreateRow(1);
        row1.CreateCell(0).SetCellValue(3);
        row1.CreateCell(1).SetCellValue(4);

        // D1:E2 (row 0-1, cols 3-4) was CSE-entered as {=A1:B2}: a 2-row x 2-col selection whose
        // natural result is exactly 2x2, so every declared cell gets its corresponding value.
        sheet.SetArrayFormula("A1:B2", new CellRangeAddress(0, 1, 3, 4));

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        var modelSheet = workbook.GetSheetAt(0);

        var loadedAnchor = modelSheet.GetCell(1, 4);
        loadedAnchor.Should().NotBeNull();
        loadedAnchor!.LegacyArrayRows.Should().Be(2u);
        loadedAnchor.LegacyArrayCols.Should().Be(2u);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        modelSheet.GetValue(1, 4).Should().Be(new NumberValue(1), "D1");
        modelSheet.GetValue(1, 5).Should().Be(new NumberValue(2), "E1");
        modelSheet.GetValue(2, 4).Should().Be(new NumberValue(3), "D2");
        modelSheet.GetValue(2, 5).Should().Be(new NumberValue(4), "E2");
    }

    // Note: the legacy .xls adapter is open-only (Save throws NotSupportedException), so there is no NPOI
    // write path to carry the mirror-image 1904 conversion — the fix is load-side only. A 1904 workbook is
    // reproduced here the way a genuine Excel-authored file exists on disk: the BIFF DateWindow1904 record
    // is flipped (NPOI serializes it, and the re-read workbook reports IsDate1904() == true and resolves
    // HSSFCell.DateCellValue against the 1904 epoch) and the cell holds the RAW on-disk serial for that date
    // system. SetCellValue(DateTime) cannot be used for the 1904 case because NPOI's in-memory 1904 flag is
    // not refreshed from the mutated record until the file is re-read, so it would emit a 1900-epoch serial.
    private static MemoryStream BuildHssfWorkbookWithDateCell(DateTime date, bool uses1904DateSystem)
    {
        var hssf = new HSSFWorkbook();

        if (uses1904DateSystem)
        {
            var dateWindow = hssf.Workbook.FindFirstRecordBySid(DateWindow1904Record.sid) as DateWindow1904Record ??
                throw new InvalidOperationException("Expected a BIFF DateWindow1904 record in the HSSF fixture.");
            dateWindow.Windowing = 1;
        }

        var sheet = hssf.CreateSheet("Data");
        var row = sheet.CreateRow(0);
        var dateStyle = hssf.CreateCellStyle();
        dateStyle.DataFormat = hssf.CreateDataFormat().GetFormat("yyyy-mm-dd");

        var cell = row.CreateCell(0);
        cell.CellStyle = dateStyle;
        // Raw on-disk serial for the workbook's date system: day-count since 1904-01-01 for a 1904 workbook,
        // otherwise the 1900-epoch OADate. Both equal what MapDateTimeValue must produce on load.
        cell.SetCellValue(uses1904DateSystem
            ? (date - new DateTime(1904, 1, 1)).TotalDays
            : date.ToOADate());

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
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
        workbook.FirstVisibleSheetIndex.Should().Be(1);
        workbook.ShowSheetTabs.Should().BeFalse();
        workbook.SheetTabRatio.Should().Be(650);
        workbook.IsStructureProtected.Should().BeTrue();
        workbook.StructureProtectionPassword.Should().Be(ProtectionPasswordHelper.ToLegacyPasswordHash("structure"));
        GetWorkbookProtectionMetadataAttribute(workbook, "lockWindows").Should().Be("1");
        workbook.StyleCount.Should().BeGreaterThan(1);
        workbook.FileSharing.Should().NotBeNull();
        workbook.FileSharing!.ReadOnlyRecommended.Should().BeTrue();
        workbook.FileSharing.UserName.Should().Be("Analyst");
        workbook.FileSharing.ReservationPassword.Should().Be(ProtectionPasswordHelper.ToLegacyPasswordHash("reserve"));

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
        sheet.OutlineSummaryBelow.Should().BeFalse();
        sheet.OutlineSummaryRight.Should().BeFalse();
        sheet.ShowOutlineSymbols.Should().BeFalse();
        sheet.PrintArea.Should().NotBeNull();
        sheet.PrintArea!.Value.Start.Should().Be(new ModelCellAddress(sheet.Id, 1, 1));
        sheet.PrintArea.Value.End.Should().Be(new ModelCellAddress(sheet.Id, 7, 5));
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.AutoFilter.Should().NotBeNull();
        sheet.AutoFilter!.Reference.Should().Be("A1:E7");
        sheet.DataValidations.Should().ContainSingle();
        var validation = sheet.DataValidations.Single();
        validation.AppliesTo.ToString().Should().Be("G2:G7");
        validation.AdditionalRanges.Should().BeEmpty();
        validation.Type.Should().Be(DvType.List);
        validation.Formula1.Should().Be("Open,Closed");
        validation.AllowBlank.Should().BeFalse();
        validation.ShowDropdown.Should().BeTrue();
        validation.AlertStyle.Should().Be(DvAlertStyle.Warning);
        validation.ShowInputMessage.Should().BeTrue();
        validation.ShowErrorMessage.Should().BeTrue();
        validation.PromptTitle.Should().Be("Status");
        validation.PromptMessage.Should().Be("Choose a status");
        validation.ErrorTitle.Should().Be("Invalid status");
        validation.ErrorMessage.Should().Be("Pick Open or Closed");
        sheet.ConditionalFormats.Should().HaveCount(2);
        var conditionalFormat = sheet.ConditionalFormats.Single(format => format.AppliesTo.ToString() == "H2:H7");
        conditionalFormat.AppliesTo.ToString().Should().Be("H2:H7");
        conditionalFormat.RuleType.Should().Be(CfRuleType.CellValue);
        conditionalFormat.Operator.Should().Be(CfOperator.GreaterThan);
        conditionalFormat.Value1.Should().Be("10");
        conditionalFormat.FormatIfTrue.Should().NotBeNull();
        conditionalFormat.FormatIfTrue!.FontColor.Should().Be(new CellColor(255, 0, 0));
        conditionalFormat.FormatIfTrue.FillColor.Should().Be(new CellColor(255, 255, 0));
        conditionalFormat.FormatIfTrue.FillPatternStyle.Should().Be(CellFillPatternStyle.Solid);
        conditionalFormat.FormatIfTrue.BorderBottom.Style.Should().Be(ModelBorderStyle.Thin);
        sheet.ConditionalFormats.Single(format => format.AppliesTo.ToString() == "J2:J7")
            .FormatIfTrue!.FontColor.Should().Be(new CellColor(255, 0, 0));
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
        sheet.ShowZeros.Should().BeFalse();
        sheet.FullCalculationOnLoad.Should().BeTrue();
        sheet.ViewTopRow.Should().Be(3);
        sheet.ViewLeftCol.Should().Be(4);
        sheet.ActiveRow.Should().Be(2);
        sheet.ActiveCol.Should().Be(4);
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        sheet.ZoomPercent.Should().Be(85);
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
        sheet.Pictures.Should().ContainSingle();
        var picture = sheet.Pictures.Single();
        picture.Anchor.Should().Be(new ModelCellAddress(sheet.Id, 5, 2));
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ContentType.Should().Be("image/png");
        picture.ImageBytes.Should().Equal(MinimalPngBytes());
        picture.AnchorOffsetX.Should().BeGreaterThan(0);
        picture.AnchorOffsetY.Should().BeGreaterThan(0);
        picture.Width.Should().BeGreaterThan(0);
        picture.Height.Should().BeGreaterThan(0);
        sheet.TextBoxes.Should().ContainSingle();
        var textBox = sheet.TextBoxes.Single();
        textBox.Anchor.Should().Be(new ModelCellAddress(sheet.Id, 8, 2));
        textBox.Text.Should().Be("Legacy textbox");
        textBox.FillColor.Should().Be(new CellColor(204, 255, 255));
        textBox.IsSourceLoaded.Should().BeTrue();
        textBox.Width.Should().BeGreaterThan(0);
        textBox.Height.Should().BeGreaterThan(0);
        sheet.DrawingShapes.Should().ContainSingle();
        var shape = sheet.DrawingShapes.Single();
        shape.Anchor.Should().Be(new ModelCellAddress(sheet.Id, 11, 2));
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.FillColor.Should().Be(new CellColor(255, 230, 153));
        shape.OutlineColor.Should().Be(new CellColor(156, 101, 0));
        shape.IsSourceLoaded.Should().BeTrue();
        shape.Width.Should().BeGreaterThan(0);
        shape.Height.Should().BeGreaterThan(0);
        sheet.FormControls.Should().ContainSingle();
        var control = sheet.FormControls.Single();
        control.Kind.Should().Be(FormControlKind.DropDown);
        control.Anchor.Should().NotBeNull();
        control.Anchor!.Value.Start.Should().Be(new ModelCellAddress(sheet.Id, 11, 6));
        control.Anchor.Value.End.Should().Be(new ModelCellAddress(sheet.Id, 13, 8));
        control.AnchorOffsets.Should().NotBeNull();
        control.AnchorOffsets!.From.Column.Should().Be(5);
        control.AnchorOffsets.From.Row.Should().Be(10);
        control.AnchorOffsets.To.Column.Should().Be(7);
        control.AnchorOffsets.To.Row.Should().Be(12);

        var hiddenSheet = workbook.GetSheetAt(1);
        hiddenSheet.Name.Should().Be("Hidden");
        hiddenSheet.IsHidden.Should().BeTrue();
        hiddenSheet.IsVeryHidden.Should().BeFalse();
        hiddenSheet.SplitRow.Should().Be(4);
        hiddenSheet.SplitColumn.Should().Be(3);
    }

    [Fact]
    public void Load_PreservesLegacyWindowProtectionWithoutStructureProtection()
    {
        var hssf = new HSSFWorkbook();
        hssf.CreateSheet("Visible");
        var protect = hssf.Workbook.FindFirstRecordBySid(ProtectRecord.sid) as ProtectRecord ??
            throw new InvalidOperationException("Expected a BIFF workbook Protect record in the HSSF fixture.");
        protect.Protect = false;
        var windowProtect = hssf.Workbook.FindFirstRecordBySid(WindowProtectRecord.sid) as WindowProtectRecord ??
            throw new InvalidOperationException("Expected a BIFF workbook WindowProtect record in the HSSF fixture.");
        windowProtect.Protect = true;
        using var stream = new MemoryStream();
        hssf.Write(stream);
        stream.Position = 0;

        var workbook = new LegacyXlsFileAdapter().Load(stream);

        workbook.IsStructureProtected.Should().BeFalse();
        workbook.StructureProtectionPassword.Should().BeNull();
        GetWorkbookProtectionMetadataAttribute(workbook, "lockWindows").Should().Be("1");
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
                workbook.Sheets.Sum(sheet => sheet.Pictures.Count),
                workbook.Sheets.Sum(sheet => sheet.TextBoxes.Count),
                workbook.Sheets.Sum(sheet => sheet.DrawingShapes.Count),
                workbook.Sheets.Sum(sheet => sheet.FormControls.Count),
                workbook.Sheets.Count(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0),
                workbook.Sheets.Sum(sheet => sheet.RowOutlineLevels.Count),
                workbook.Sheets.Sum(sheet => sheet.ColOutlineLevels.Count),
                workbook.Sheets.Count(sheet => sheet.PrintArea is not null),
                workbook.Sheets.Count(sheet => sheet.PrintTitleRows is not null),
                workbook.Sheets.Count(sheet => sheet.PrintTitleColumns is not null),
                workbook.Sheets.Count(sheet => sheet.AutoFilter is not null),
                workbook.Sheets.Count(sheet => sheet.IsProtected),
                workbook.Sheets.Sum(sheet => sheet.DataValidations.Count),
                workbook.Sheets.Sum(sheet => sheet.ConditionalFormats.Count),
                workbook.Sheets.Count,
                workbook.Sheets.Sum(sheet => sheet.RowPageBreaks.Count + sheet.ColumnPageBreaks.Count),
                workbook.ActiveSheetIndex,
                workbook.Uses1904DateSystem,
                RichMetadata: source.RichMetadata,
                HasVbaProjectPackage: workbook.HasVbaProjectPackage,
                SheetNames: workbook.Sheets.Select(sheet => sheet.Name).ToArray(),
                SheetKindFingerprints: ReadImportedSheetKindFingerprints(workbook),
                SheetVisibilityFingerprints: ReadImportedSheetVisibilityFingerprints(workbook),
                WorkbookCodeName: ReadImportedWorkbookCodeName(workbook),
                SheetCodeNameFingerprints: ReadImportedSheetCodeNameFingerprints(workbook),
                WorkbookCountryFingerprints: ReadImportedWorkbookCountryFingerprints(workbook),
                WorkbookLegacyMenuFingerprints: ReadImportedWorkbookLegacyMenuFingerprints(workbook),
                WorkbookLegacyWorkbookFingerprints: ReadImportedWorkbookLegacyWorkbookFingerprints(workbook),
                WorkbookFunctionGroupFingerprints: ReadImportedWorkbookFunctionGroupFingerprints(workbook),
                WorkbookPropertiesFingerprints: ReadImportedWorkbookPropertiesFingerprints(workbook),
                WorkbookViewFingerprints: ReadImportedWorkbookViewFingerprints(workbook),
                WorkbookProtectionFingerprints: ReadImportedWorkbookProtectionFingerprints(workbook),
                WorkbookFileSharingFingerprints: ReadImportedWorkbookFileSharingFingerprints(workbook),
                WorkbookCalculationFingerprints: ReadImportedWorkbookCalculationFingerprints(workbook),
                SheetCalculationFingerprints: ReadImportedSheetCalculationFingerprints(workbook),
                CellFingerprints: ReadImportedCellFingerprints(workbook),
                MergeFingerprints: ReadImportedMergeFingerprints(workbook),
                DimensionFingerprints: ReadImportedDimensionFingerprints(workbook),
                DefaultDimensionFingerprints: ReadImportedDefaultDimensionFingerprints(workbook),
                StyleFingerprints: source.RichMetadata
                    ? ReadImportedRichStyleFingerprints(workbook)
                    : ReadImportedFallbackStyleFingerprints(workbook),
                HeaderFooterFingerprints: ReadImportedFallbackHeaderFooterFingerprints(workbook),
                DefinedNameFingerprints: ReadImportedDefinedNameFingerprints(workbook),
                HyperlinkFingerprints: ReadImportedHyperlinkFingerprints(workbook),
                CommentFingerprints: ReadImportedCommentFingerprints(workbook),
                PictureFingerprints: ReadImportedPictureFingerprints(workbook),
                TextBoxFingerprints: ReadImportedTextBoxFingerprints(workbook),
                DrawingShapeFingerprints: ReadImportedDrawingShapeFingerprints(workbook),
                FormControlFingerprints: ReadImportedFormControlFingerprints(workbook),
                PaneFingerprints: ReadImportedPaneFingerprints(workbook),
                RowOutlineFingerprints: ReadImportedRowOutlineFingerprints(workbook),
                ColOutlineFingerprints: ReadImportedColOutlineFingerprints(workbook),
                OutlineSettingFingerprints: ReadImportedOutlineSettingFingerprints(workbook),
                PrintLayoutFingerprints: ReadImportedPrintLayoutFingerprints(workbook),
                PrintOptionsFingerprints: ReadImportedPrintOptionsFingerprints(workbook),
                SheetLegacyPrintSizeFingerprints: ReadImportedLegacyPrintSizeFingerprints(workbook),
                PrimaryViewMetadataFingerprints: ReadImportedPrimaryViewMetadataFingerprints(workbook),
                PageSetupFingerprints: ReadImportedPageSetupFingerprints(workbook),
                ViewStateFingerprints: ReadImportedViewStateFingerprints(workbook),
                AutoFilterFingerprints: ReadImportedAutoFilterFingerprints(workbook),
                SheetProtectionFingerprints: ReadImportedSheetProtectionFingerprints(workbook),
                DataValidationFingerprints: ReadImportedDataValidationFingerprints(workbook),
                ConditionalFormatFingerprints: ReadImportedConditionalFormatFingerprints(workbook));

            imported.Sheets.Should().Be(source.Sheets, imported.File);
            imported.Cells.Should().Be(source.Cells, imported.File);
            imported.Uses1904DateSystem.Should().Be(source.Uses1904DateSystem, imported.File);
            imported.HasVbaProjectPackage.Should().Be(source.HasVbaProjectPackage, imported.File);
            if (!source.RichMetadata)
            {
                imported.Styles.Should().Be(source.Styles, imported.File);
                imported.Merges.Should().Be(source.Merges, imported.File);
                imported.HiddenSheets.Should().Be(source.HiddenSheets, imported.File);
                imported.VeryHiddenSheets.Should().Be(source.VeryHiddenSheets, imported.File);
                imported.Dimensions.Should().BeGreaterThanOrEqualTo(source.Dimensions, imported.File);
                imported.ActiveSheetIndex.Should().Be(source.ActiveSheetIndex, imported.File);
                imported.SheetNames.Should().Equal(source.SheetNames, imported.File);
                imported.SheetVisibilityFingerprints.Should().BeEquivalentTo(source.SheetVisibilityFingerprints, imported.File);
                imported.WorkbookCodeName.Should().Be(source.WorkbookCodeName, imported.File);
                imported.SheetCodeNameFingerprints.Should().BeEquivalentTo(source.SheetCodeNameFingerprints, imported.File);
                imported.CellFingerprints.Should().BeEquivalentTo(source.CellFingerprints, imported.File);
                imported.MergeFingerprints.Should().BeEquivalentTo(source.MergeFingerprints, imported.File);
                imported.DimensionFingerprints.Should().BeEquivalentTo(source.DimensionFingerprints, imported.File);
                imported.StyleFingerprints.Should().BeEquivalentTo(source.StyleFingerprints, imported.File);
                imported.HeaderFooterFingerprints.Should().BeEquivalentTo(source.HeaderFooterFingerprints, imported.File);
            }

            if (source.RichMetadata)
            {
                imported.Formulas.Should().Be(source.Formulas, imported.File);
                imported.Merges.Should().Be(source.Merges, imported.File);
                imported.HiddenSheets.Should().Be(source.HiddenSheets, imported.File);
                imported.VeryHiddenSheets.Should().Be(source.VeryHiddenSheets, imported.File);
                imported.DefinedNames.Should().Be(source.DefinedNames, imported.File);
                imported.Hyperlinks.Should().Be(source.Hyperlinks, imported.File);
                imported.Comments.Should().Be(source.Comments, imported.File);
                imported.Pictures.Should().Be(source.Pictures, imported.File);
                imported.TextBoxes.Should().Be(source.TextBoxes, imported.File);
                imported.DrawingShapes.Should().Be(source.DrawingShapes, imported.File);
                imported.FormControls.Should().Be(source.FormControls, imported.File);
                imported.SheetNames.Should().Equal(source.SheetNames, imported.File);
                imported.SheetKindFingerprints.Should().BeEquivalentTo(source.SheetKindFingerprints, imported.File);
                imported.SheetVisibilityFingerprints.Should().BeEquivalentTo(source.SheetVisibilityFingerprints, imported.File);
                imported.WorkbookCodeName.Should().Be(source.WorkbookCodeName, imported.File);
                imported.SheetCodeNameFingerprints.Should().BeEquivalentTo(source.SheetCodeNameFingerprints, imported.File);
                imported.WorkbookCountryFingerprints.Should().BeEquivalentTo(source.WorkbookCountryFingerprints, imported.File);
                imported.WorkbookLegacyMenuFingerprints.Should().BeEquivalentTo(source.WorkbookLegacyMenuFingerprints, imported.File);
                imported.WorkbookLegacyWorkbookFingerprints.Should().BeEquivalentTo(source.WorkbookLegacyWorkbookFingerprints, imported.File);
                imported.WorkbookFunctionGroupFingerprints.Should().BeEquivalentTo(source.WorkbookFunctionGroupFingerprints, imported.File);
                imported.WorkbookPropertiesFingerprints.Should().BeEquivalentTo(source.WorkbookPropertiesFingerprints, imported.File);
                imported.WorkbookViewFingerprints.Should().BeEquivalentTo(source.WorkbookViewFingerprints, imported.File);
                imported.WorkbookProtectionFingerprints.Should().BeEquivalentTo(source.WorkbookProtectionFingerprints, imported.File);
                imported.WorkbookFileSharingFingerprints.Should().BeEquivalentTo(source.WorkbookFileSharingFingerprints, imported.File);
                imported.WorkbookCalculationFingerprints.Should().BeEquivalentTo(source.WorkbookCalculationFingerprints, imported.File);
                imported.SheetCalculationFingerprints.Should().BeEquivalentTo(source.SheetCalculationFingerprints, imported.File);
                imported.CellFingerprints.Should().BeEquivalentTo(source.CellFingerprints, imported.File);
                imported.MergeFingerprints.Should().BeEquivalentTo(source.MergeFingerprints, imported.File);
                imported.DimensionFingerprints.Should().BeEquivalentTo(source.DimensionFingerprints, imported.File);
                imported.DefaultDimensionFingerprints.Should().BeEquivalentTo(source.DefaultDimensionFingerprints, imported.File);
                imported.StyleFingerprints.Should().BeEquivalentTo(source.StyleFingerprints, imported.File);
                imported.DefinedNameFingerprints.Should().BeEquivalentTo(source.DefinedNameFingerprints, imported.File);
                imported.HyperlinkFingerprints.Should().BeEquivalentTo(source.HyperlinkFingerprints, imported.File);
                imported.CommentFingerprints.Should().BeEquivalentTo(source.CommentFingerprints, imported.File);
                imported.PictureFingerprints.Should().BeEquivalentTo(source.PictureFingerprints, imported.File);
                imported.TextBoxFingerprints.Should().BeEquivalentTo(source.TextBoxFingerprints, imported.File);
                imported.DrawingShapeFingerprints.Should().BeEquivalentTo(source.DrawingShapeFingerprints, imported.File);
                imported.FormControlFingerprints.Should().BeEquivalentTo(source.FormControlFingerprints, imported.File);
                imported.FreezePanes.Should().Be(source.FreezePanes, imported.File);
                imported.RowOutlineLevels.Should().Be(source.RowOutlineLevels, imported.File);
                imported.ColOutlineLevels.Should().Be(source.ColOutlineLevels, imported.File);
                imported.PrintAreas.Should().Be(source.PrintAreas, imported.File);
                imported.PrintTitleRows.Should().Be(source.PrintTitleRows, imported.File);
                imported.PrintTitleColumns.Should().Be(source.PrintTitleColumns, imported.File);
                imported.AutoFilters.Should().Be(source.AutoFilters, imported.File);
                imported.ProtectedSheets.Should().Be(source.ProtectedSheets, imported.File);
                imported.DataValidations.Should().Be(source.DataValidations, imported.File);
                imported.ConditionalFormats.Should().Be(source.ConditionalFormats, imported.File);
                imported.PageSetupSheets.Should().Be(source.PageSetupSheets, imported.File);
                imported.PageBreaks.Should().Be(source.PageBreaks, imported.File);
                imported.ActiveSheetIndex.Should().Be(source.ActiveSheetIndex, imported.File);
                imported.PaneFingerprints.Should().BeEquivalentTo(source.PaneFingerprints, imported.File);
                imported.RowOutlineFingerprints.Should().BeEquivalentTo(source.RowOutlineFingerprints, imported.File);
                imported.ColOutlineFingerprints.Should().BeEquivalentTo(source.ColOutlineFingerprints, imported.File);
                imported.OutlineSettingFingerprints.Should().BeEquivalentTo(source.OutlineSettingFingerprints, imported.File);
                imported.PrintLayoutFingerprints.Should().BeEquivalentTo(source.PrintLayoutFingerprints, imported.File);
                imported.PrintOptionsFingerprints.Should().BeEquivalentTo(source.PrintOptionsFingerprints, imported.File);
                imported.SheetLegacyPrintSizeFingerprints.Should().BeEquivalentTo(source.SheetLegacyPrintSizeFingerprints, imported.File);
                imported.PrimaryViewMetadataFingerprints.Should().BeEquivalentTo(source.PrimaryViewMetadataFingerprints, imported.File);
                imported.PageSetupFingerprints.Should().BeEquivalentTo(source.PageSetupFingerprints, imported.File);
                imported.ViewStateFingerprints.Should().BeEquivalentTo(source.ViewStateFingerprints, imported.File);
                imported.AutoFilterFingerprints.Should().BeEquivalentTo(source.AutoFilterFingerprints, imported.File);
                imported.SheetProtectionFingerprints.Should().BeEquivalentTo(source.SheetProtectionFingerprints, imported.File);
                imported.DataValidationFingerprints.Should().BeEquivalentTo(source.DataValidationFingerprints, imported.File);
                imported.ConditionalFormatFingerprints.Should().BeEquivalentTo(source.ConditionalFormatFingerprints, imported.File);
                imported.Styles.Should().BeGreaterThanOrEqualTo(source.Styles, imported.File);
                imported.Dimensions.Should().BeGreaterThanOrEqualTo(source.Dimensions, imported.File);
            }

            summaries.Add(imported);
        }

        summaries.Should().HaveCountGreaterThanOrEqualTo(20);
        summaries.Sum(summary => summary.Cells).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Formulas).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Styles).Should().BeGreaterThan(0);
        summaries.Where(summary => !summary.RichMetadata).Sum(summary => summary.Styles).Should().BeGreaterThan(0);
        summaries.Where(summary => !summary.RichMetadata).Sum(summary => summary.HeaderFooterFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.Merges).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Dimensions).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.HiddenSheets).Should().BeGreaterThan(0);
        summaries.Count(summary => summary.Uses1904DateSystem).Should().BeGreaterThan(0);
        summaries.Count(summary => summary.HasVbaProjectPackage).Should().BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata).Sum(summary => summary.DefaultDimensionFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.ViewStateFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|Active=", StringComparison.Ordinal) &&
                !fingerprint.Contains("|Active=null,null|", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.WorkbookCalculationFingerprints?.Count(fingerprint =>
                !fingerprint.Contains("|Mode=Automatic|Full=False|Iterate=False|Count=null|Delta=null", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Count(summary => !string.IsNullOrWhiteSpace(summary.WorkbookCodeName))
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.SheetCodeNameFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.WorkbookCountryFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.WorkbookLegacyMenuFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.WorkbookLegacyWorkbookFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.WorkbookLegacyWorkbookFingerprints?.Count(fingerprint =>
                fingerprint.Contains("TabIds=3,2,12", StringComparison.Ordinal) ||
                fingerprint.Contains("UseNaturalLanguageFormulas=False", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.WorkbookFunctionGroupFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.WorkbookPropertiesFingerprints?.Count(fingerprint =>
                fingerprint.Contains("ShowObjects=all", StringComparison.Ordinal) &&
                fingerprint.Contains("Backup=0", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.ViewStateFingerprints?.Count(fingerprint =>
                fingerprint.Contains(",False|TopLeft", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.ViewStateFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|View=Normal,80|", StringComparison.Ordinal) ||
                fingerprint.Contains("|View=Normal,85|", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.SheetKindFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|Kind=DialogSheet", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.DefinedNames).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Hyperlinks).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Comments).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.Pictures).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.TextBoxes).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.DrawingShapes).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.FormControls).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.PrintAreas + summary.PrintTitleRows + summary.PrintTitleColumns)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.PrintOptionsFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|GridLinesSet=0", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.SheetLegacyPrintSizeFingerprints?.Count ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.PrimaryViewMetadataFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|TabSelected=1", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.PrimaryViewMetadataFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|DefaultGridColor=0|ColorId=8", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Where(summary => summary.RichMetadata)
            .Sum(summary => summary.PrimaryViewMetadataFingerprints?.Count(fingerprint =>
                fingerprint.Contains("|Selection=A1,A1:F2,null", StringComparison.Ordinal)) ?? 0)
            .Should()
            .BeGreaterThan(0);
        summaries.Sum(summary => summary.ProtectedSheets).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.ConditionalFormats).Should().BeGreaterThan(0);
        summaries.Sum(summary => summary.PageBreaks).Should().BeGreaterThan(0);
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

    [Theory]
    [InlineData(nameof(ExcelDataReader.CellError.NULL), "#NULL!")]
    [InlineData(nameof(ExcelDataReader.CellError.DIV0), "#DIV/0!")]
    [InlineData(nameof(ExcelDataReader.CellError.VALUE), "#VALUE!")]
    [InlineData(nameof(ExcelDataReader.CellError.REF), "#REF!")]
    [InlineData(nameof(ExcelDataReader.CellError.NAME), "#NAME?")]
    [InlineData(nameof(ExcelDataReader.CellError.NUM), "#NUM!")]
    [InlineData(nameof(ExcelDataReader.CellError.NA), "#N/A")]
    [InlineData(nameof(ExcelDataReader.CellError.GETTING_DATA), "#GETTING_DATA")]
    public void Load_MapsExcelDataReaderCellErrors(string errorName, string expectedCode)
    {
        var method = typeof(LegacyXlsFileAdapter).GetMethod(
            "MapExcelDataReaderErrorValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var error = Enum.Parse<ExcelDataReader.CellError>(errorName);
        var value = method!.Invoke(null, [error]);

        value.Should().Be(new ErrorValue(expectedCode));
    }

    [Fact]
    public void Load_ReadsLegacyXlsFormControlListMetadataFromLbsDataSubRecord()
    {
        var hssf = new HSSFWorkbook();
        var sheet = (HSSFSheet)hssf.CreateSheet("Visible");
        var drawing = (HSSFPatriarch)sheet.CreateDrawingPatriarch();
        var comboBox = drawing.CreateComboBox(new HSSFClientAnchor(0, 0, 0, 0, 0, 0, 2, 2));
        var objMethod = typeof(LegacyXlsFileAdapter).GetMethod(
            "TryGetObjRecord",
            BindingFlags.NonPublic | BindingFlags.Static);
        objMethod.Should().NotBeNull();
        objMethod!.Invoke(null, [comboBox]).Should().BeOfType<ObjRecord>();

        var lbsData = LbsDataSubRecord.CreateAutoFilterInstance();
        SetPrivateField(lbsData, "_linkPtg", HSSFFormulaParser.Parse("Visible!$A$20:$A$22", hssf).Single());
        SetPrivateField(lbsData, "_iSel", 2);

        var formatMethod = typeof(LegacyXlsFileAdapter).GetMethod(
            "TryFormatLbsListFillRange",
            BindingFlags.NonPublic | BindingFlags.Static);
        formatMethod.Should().NotBeNull();
        var formatArgs = new object?[] { hssf, lbsData, "" };
        formatMethod!.Invoke(null, formatArgs).Should().Be(true);
        formatArgs[2].Should().Be("Visible!$A$20:$A$22");

        var selectedMethod = typeof(LegacyXlsFileAdapter).GetMethod(
            "TryGetLbsSelectedIndex",
            BindingFlags.NonPublic | BindingFlags.Static);
        selectedMethod.Should().NotBeNull();
        var selectedArgs = new object?[] { lbsData, 0 };
        selectedMethod!.Invoke(null, selectedArgs).Should().Be(true);
        selectedArgs[1].Should().Be(2);
    }

    [Fact]
    public void CreateLegacyPivotTable_ReadsBiffPivotViewRecords()
    {
        var workbook = new Workbook("LegacyPivot");
        var sheet = workbook.AddSheet("Financial History PivotTable");
        sheet.SetCell(new ModelCellAddress(sheet.Id, 6, 1), new TextValue("Category"));
        sheet.SetCell(new ModelCellAddress(sheet.Id, 20, 4), new NumberValue(42));
        var definition = CreateUninitializedPivotRecord<ViewDefinitionRecord>(
            ("name", "PivotTable1"),
            ("iCache", (short)0),
            ("rwFirst", (short)5),
            ("rwFirstHead", (short)6),
            ("rwFirstData", (short)7),
            ("rwLast", (short)19),
            ("colFirst", (short)0),
            ("colFirstData", (short)1),
            ("colLast", (short)3),
            ("dataField", "Data"));
        var viewFields = new[]
        {
            CreateUninitializedPivotRecord<ViewFieldsRecord>(("sxaxis", (short)2)),
            CreateUninitializedPivotRecord<ViewFieldsRecord>(("sxaxis", (short)4)),
            CreateUninitializedPivotRecord<ViewFieldsRecord>(("sxaxis", (short)1)),
            CreateUninitializedPivotRecord<ViewFieldsRecord>(("sxaxis", (short)8))
        };
        var dataItems = new[]
        {
            CreateUninitializedPivotRecord<DataItemRecord>(
                ("isxvdData", (short)3),
                ("df", (short)0),
                ("ifmt", (short)171),
                ("name", "Sum of Amount"))
        };

        var pivot = LegacyXlsFileAdapter.CreateLegacyPivotTable(sheet, definition, viewFields, dataItems, 1);

        pivot.Should().NotBeNull();
        pivot!.Name.Should().Be("PivotTable1");
        pivot.CacheId.Should().Be(0);
        pivot.TargetRange.Start.Should().Be(new ModelCellAddress(sheet.Id, 6, 1));
        pivot.TargetRange.End.Should().Be(new ModelCellAddress(sheet.Id, 20, 4));
        pivot.LastRenderedRange.Should().Be(pivot.TargetRange);
        pivot.FirstHeaderRow.Should().Be(2);
        pivot.FirstDataRow.Should().Be(3);
        pivot.FirstDataColumn.Should().Be(2);
        pivot.ColumnFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivot.PageFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(1);
        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(2);
        pivot.DataFields.Should().ContainSingle().Which.Should().Be(
            new PivotDataFieldModel(3, "Sum of Amount", "sum", 171));
    }

    private static ScalarValue MapLegacyXlsValue(object? value)
    {
        var method = typeof(LegacyXlsFileAdapter).GetMethod("MapValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (ScalarValue)method!.Invoke(null, [value])!;
    }

    private static T CreateUninitializedPivotRecord<T>(params (string FieldName, object? Value)[] fields)
        where T : class
    {
        var record = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        foreach (var (fieldName, value) in fields)
            SetPrivateField(record, fieldName, value);

        return record;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"NPOI {target.GetType().Name} should expose {fieldName} for fixture authoring");
        field!.SetValue(target, value);
    }

    private static MemoryStream CreateRichLegacyXlsFixture()
    {
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Visible");
        var hidden = hssf.CreateSheet("Hidden");
        hssf.SetActiveSheet(0);
        hssf.SetSelectedTab(0);
        hssf.FirstVisibleTab = 1;
        hssf.SetSheetVisibility(1, SheetVisibility.Hidden);
        if (hssf.Workbook.FindFirstRecordBySid(WindowOneRecord.sid) is WindowOneRecord window)
        {
            window.DisplayTabs = false;
            window.TabWidthRatio = 650;
        }
        var protect = hssf.Workbook.FindFirstRecordBySid(ProtectRecord.sid) as ProtectRecord ??
            throw new InvalidOperationException("Expected a BIFF workbook Protect record in the HSSF fixture.");
        protect.Protect = true;
        var windowProtect = hssf.Workbook.FindFirstRecordBySid(WindowProtectRecord.sid) as WindowProtectRecord ??
            throw new InvalidOperationException("Expected a BIFF workbook WindowProtect record in the HSSF fixture.");
        windowProtect.Protect = true;
        var password = hssf.Workbook.FindFirstRecordBySid(PasswordRecord.sid) as PasswordRecord ??
            throw new InvalidOperationException("Expected a BIFF workbook Password record in the HSSF fixture.");
        password.Password = unchecked((short)Convert.ToUInt16(ProtectionPasswordHelper.ToLegacyPasswordHash("structure"), 16));
        hssf.WriteProtectWorkbook("reserve", "Analyst");
        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 1));
        sheet.SetColumnWidth(1, 18 * 256);
        sheet.SetColumnHidden(2, true);
        sheet.CreateFreezePane(1, 1);
        sheet.DisplayGridlines = false;
        sheet.DisplayRowColHeadings = false;
        sheet.DisplayFormulas = true;
        sheet.DisplayZeros = false;
        sheet.ShowInPane(2, 3);
        sheet.ForceFormulaRecalculation = true;
        var windowTwo = TryGetWindowTwoRecord(sheet) ??
            throw new InvalidOperationException("Expected a BIFF sheet Window2 record in the HSSF fixture.");
        windowTwo.SavedInPageBreakPreview = true;
        windowTwo.PageBreakZoom = 85;
        windowTwo.NormalZoom = 125;
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
        hyperlinkCell.SetAsActiveCell();

        var drawing = (HSSFPatriarch)sheet.CreateDrawingPatriarch();
        var commentAnchor = new HSSFClientAnchor(0, 0, 0, 0, 4, 1, 6, 3);
        var comment = drawing.CreateCellComment(commentAnchor);
        comment.String = helper.CreateRichTextString("Review before publishing");
        comment.Author = "Analyst";
        var commentCell = row.CreateCell(4);
        commentCell.SetCellValue("commented");
        commentCell.CellComment = comment;
        var pictureIndex = hssf.AddPicture(MinimalPngBytes(), PictureType.PNG);
        drawing.CreatePicture(new HSSFClientAnchor(128, 64, 512, 192, 1, 4, 3, 7), pictureIndex);
        var textBox = (HSSFTextbox)drawing.CreateTextbox(new HSSFClientAnchor(64, 32, 900, 220, 1, 7, 4, 10));
        textBox.String = helper.CreateRichTextString("Legacy textbox");
        textBox.SetFillColor(204, 255, 255);
        var simpleShape = drawing.CreateSimpleShape(new HSSFClientAnchor(32, 64, 800, 220, 1, 10, 4, 13));
        simpleShape.ShapeType = HSSFSimpleShape.OBJECT_TYPE_OVAL;
        simpleShape.SetFillColor(255, 230, 153);
        simpleShape.SetLineStyleColor(156, 101, 0);
        var comboBox = drawing.CreateComboBox(new HSSFClientAnchor(128, 32, 900, 220, 5, 10, 7, 12));

        var hiddenRow = sheet.CreateRow(3);
        hiddenRow.ZeroHeight = true;
        hiddenRow.CreateCell(0).SetCellValue("hidden");

        sheet.CreateRow(5).CreateCell(0).SetCellValue("outlined one");
        sheet.CreateRow(6).CreateCell(0).SetCellValue("outlined two");
        sheet.GroupRow(5, 6);
        sheet.RowSumsBelow = false;
        sheet.RowSumsRight = false;
        sheet.DisplayGuts = false;

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
        var validationHelper = sheet.GetDataValidationHelper();
        var validationRegions = new CellRangeAddressList(1, 6, 6, 6);
        var validationConstraint = validationHelper.CreateExplicitListConstraint(["Open", "Closed"]);
        var validation = validationHelper.CreateValidation(validationConstraint, validationRegions);
        validation.EmptyCellAllowed = false;
        validation.SuppressDropDownArrow = false;
        validation.ShowPromptBox = true;
        validation.CreatePromptBox("Status", "Choose a status");
        validation.ShowErrorBox = true;
        validation.ErrorStyle = ERRORSTYLE.WARNING;
        validation.CreateErrorBox("Invalid status", "Pick Open or Closed");
        sheet.AddValidationData(validation);
        var conditionalFormatting = sheet.SheetConditionalFormatting;
        var conditionalRule = conditionalFormatting.CreateConditionalFormattingRule(ComparisonOperator.GreaterThan, "10");
        var conditionalFont = conditionalRule.CreateFontFormatting();
        conditionalFont.SetFontStyle(true, false);
        conditionalFont.FontColorIndex = IndexedColors.Red.Index;
        var conditionalPattern = conditionalRule.CreatePatternFormatting();
        conditionalPattern.FillPattern = FillPattern.SolidForeground;
        conditionalPattern.FillForegroundColor = IndexedColors.Yellow.Index;
        var conditionalBorder = conditionalRule.CreateBorderFormatting();
        conditionalBorder.BorderBottom = NPOIBorderStyle.Thin;
        conditionalBorder.BottomBorderColor = IndexedColors.Blue.Index;
        conditionalFormatting.AddConditionalFormatting(
            [new CellRangeAddress(1, 6, 7, 7), new CellRangeAddress(1, 6, 9, 9)],
            conditionalRule);
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

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0xF8, 0x0F, 0x00, 0x01,
        0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

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
        var pictures = 0;
        var textBoxes = 0;
        var drawingShapes = 0;
        var formControls = 0;
        var freezePanes = 0;
        var rowOutlineLevels = 0;
        var colOutlineLevels = 0;
        var pageBreaks = 0;
        var dataValidations = 0;
        var conditionalFormats = 0;
        var sheetNames = new List<string>();
        var sheetVisibilityFingerprints = new List<string>();
        var sheetCodeNameFingerprints = new List<string>();
        var cellFingerprints = new List<string>();
        var mergeFingerprints = new List<string>();
        var dimensionFingerprints = new List<string>();
        var defaultDimensionFingerprints = new List<string>();
        var styleFingerprints = new List<string>();
        var hyperlinkFingerprints = new List<string>();
        var commentFingerprints = new List<string>();
        var pictureFingerprints = new List<string>();
        var textBoxFingerprints = new List<string>();
        var drawingShapeFingerprints = new List<string>();
        var formControlFingerprints = new List<string>();
        var paneFingerprints = new List<string>();
        var rowOutlineFingerprints = new List<string>();
        var colOutlineFingerprints = new List<string>();
        var printLayoutFingerprints = new List<string>();
        var printOptionsFingerprints = new List<string>();
        var primaryViewMetadataFingerprints = new List<string>();
        var pageSetupFingerprints = new List<string>();
        var viewStateFingerprints = new List<string>();
        var sheetCalculationFingerprints = new List<string>();
        var autoFilterFingerprints = new List<string>();
        var sheetProtectionFingerprints = new List<string>();
        var dataValidationFingerprints = new List<string>();
        var conditionalFormatFingerprints = new List<string>();
        var activeSheetIndex = hssf.ActiveSheetIndex >= 0 && hssf.ActiveSheetIndex < hssf.NumberOfSheets
            ? hssf.ActiveSheetIndex
            : (int?)null;
        var palette = hssf.GetCustomPalette();

        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            sheetNames.Add(sheet.SheetName);
            sheetVisibilityFingerprints.Add(CreateSheetVisibilityFingerprint(
                sheetIndex,
                sheet.SheetName,
                NormalizeSourceSheetVisibility(hssf.GetSheetVisibility(sheetIndex))));
            if (ReadHssfSheetCodeName(sheet) is { } codeName)
                sheetCodeNameFingerprints.Add(CreateSheetCodeNameFingerprint(sheetIndex, sheet.SheetName, codeName));
            defaultDimensionFingerprints.Add(CreateDefaultDimensionFingerprint(
                sheetIndex,
                sheet.SheetName,
                sheet.DefaultColumnWidth,
                PointsToPixels(sheet.DefaultRowHeightInPoints)));
            merges += sheet.NumMergedRegions;
            for (var mergeIndex = 0; mergeIndex < sheet.NumMergedRegions; mergeIndex++)
                mergeFingerprints.Add(CreateMergeFingerprint(sheetIndex, sheet.SheetName, sheet.GetMergedRegion(mergeIndex)));

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
            if (TryCreateSourcePrintOptionsFingerprint(sheetIndex, sheet.SheetName, sheet, out var printOptionsFingerprint))
                printOptionsFingerprints.Add(printOptionsFingerprint);
            if (TryCreateSourcePrimaryViewMetadataFingerprint(sheetIndex, sheet.SheetName, sheet, out var primaryViewMetadataFingerprint))
                primaryViewMetadataFingerprints.Add(primaryViewMetadataFingerprint);
            pageBreaks += sheet.RowBreaks.Count(breakIndex => ToSourceModelIndex(breakIndex) >= 2);
            pageBreaks += sheet.ColumnBreaks.Count(breakIndex => ToSourceModelIndex(breakIndex) >= 2);
            pageSetupFingerprints.Add(CreateSourcePageSetupFingerprint(sheetIndex, sheet.SheetName, sheet));
            viewStateFingerprints.Add(CreateSourceViewStateFingerprint(sheetIndex, sheet.SheetName, sheet, palette));
            sheetCalculationFingerprints.Add(CreateSheetCalculationFingerprint(
                sheetIndex,
                sheet.SheetName,
                sheet.ForceFormulaRecalculation));
            if (sheet.Protect || sheet.ScenarioProtect || sheet is HSSFSheet { ObjectProtect: true })
                sheetProtectionFingerprints.Add(CreateSourceSheetProtectionFingerprint(sheetIndex, sheet.SheetName, sheet));

            for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                var row = sheet.GetRow(rowIndex);
                if (row is null)
                    continue;

                if (row.ZeroHeight || row.HeightInPoints > 0)
                    dimensions++;
                if (row.ZeroHeight)
                    dimensionFingerprints.Add(CreateDimensionFingerprint(sheetIndex, sheet.SheetName, "HiddenRow", (uint)rowIndex + 1, "true"));
                if (row.HeightInPoints > 0)
                    dimensionFingerprints.Add(CreateDimensionFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        "RowHeight",
                        (uint)rowIndex + 1,
                        FormatDouble(PointsToPixels(row.HeightInPoints))));

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
                    {
                        styles++;
                        styleFingerprints.Add(CreateSourceRichStyleFingerprint(
                            hssf,
                            sheetIndex,
                            sheet.SheetName,
                            (uint)cell.RowIndex + 1,
                            (uint)cell.ColumnIndex + 1,
                            cell.CellStyle));
                    }
                }
            }

            var maxColumn = FindLastSourceColumn(sheet);
            for (var columnIndex = 0; columnIndex <= maxColumn; columnIndex++)
            {
                var hidden = sheet.IsColumnHidden(columnIndex);
                var width = sheet.GetColumnWidth(columnIndex);
                if (hidden ||
                    width != sheet.DefaultColumnWidth * 256)
                {
                    dimensions++;
                }

                if (hidden)
                    dimensionFingerprints.Add(CreateDimensionFingerprint(sheetIndex, sheet.SheetName, "HiddenCol", (uint)columnIndex + 1, "true"));
                if (width > 0)
                    dimensionFingerprints.Add(CreateDimensionFingerprint(
                        sheetIndex,
                        sheet.SheetName,
                        "ColWidth",
                        (uint)columnIndex + 1,
                        FormatDouble(width / 256.0)));
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

                var sheetPictureFingerprints = ReadSourcePictureFingerprints(sheetIndex, hssfSheet);
                pictures += sheetPictureFingerprints.Count;
                pictureFingerprints.AddRange(sheetPictureFingerprints);

                var sheetTextBoxFingerprints = ReadSourceTextBoxFingerprints(sheetIndex, hssfSheet);
                textBoxes += sheetTextBoxFingerprints.Count;
                textBoxFingerprints.AddRange(sheetTextBoxFingerprints);

                var sheetDrawingShapeFingerprints = ReadSourceDrawingShapeFingerprints(sheetIndex, hssfSheet);
                drawingShapes += sheetDrawingShapeFingerprints.Count;
                drawingShapeFingerprints.AddRange(sheetDrawingShapeFingerprints);

                var sheetFormControlFingerprints = ReadSourceFormControlFingerprints(hssf, sheetIndex, hssfSheet);
                formControls += sheetFormControlFingerprints.Count;
                formControlFingerprints.AddRange(sheetFormControlFingerprints);

                try
                {
                    var sheetDataValidationFingerprints = hssfSheet.GetDataValidations()
                        .Select(validation => CreateSourceDataValidationFingerprint(sheetIndex, sheet.SheetName, validation))
                        .Where(value => value is not null)
                        .Select(value => value!)
                        .ToArray();
                    dataValidations += sheetDataValidationFingerprints.Length;
                    dataValidationFingerprints.AddRange(sheetDataValidationFingerprints);
                }
                catch
                {
                    // Match the production importer: malformed DV records should not discard the rest of the sheet.
                }

                try
                {
                    var sheetConditionalFormatFingerprints = ReadSourceConditionalFormatFingerprints(hssf, hssfSheet, sheetIndex);
                    conditionalFormats += sheetConditionalFormatFingerprints.Count;
                    conditionalFormatFingerprints.AddRange(sheetConditionalFormatFingerprints);
                }
                catch
                {
                    // Match the production importer: malformed CF records should not discard the rest of the sheet.
                }
            }
        }

        var definedNameWorkbook = CreateSourceDefinedNameWorkbook(hssf);
        var definedNameFingerprints = ReadImportedDefinedNameFingerprints(definedNameWorkbook);
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
        var workbookViewFingerprints = ReadSourceWorkbookViewFingerprints(hssf);
        var workbookProtectionFingerprints = ReadSourceWorkbookProtectionFingerprints(hssf);
        var workbookFileSharingFingerprints = ReadSourceWorkbookFileSharingFingerprints(hssf);
        var workbookPropertiesFingerprints = ReadSourceWorkbookPropertiesFingerprints(hssf);
        var workbookCalculationFingerprints = ReadSourceWorkbookCalculationFingerprints(hssf);
        var outlineSettingFingerprints = ReadSourceOutlineSettingFingerprints(hssf);

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
            definedNameFingerprints.Count,
            hyperlinks,
            comments,
            pictures,
            textBoxes,
            drawingShapes,
            formControls,
            freezePanes,
            rowOutlineLevels,
            colOutlineLevels,
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Area|", StringComparison.Ordinal)),
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Rows|", StringComparison.Ordinal)),
            orderedPrintLayoutFingerprints.Count(value => value.Contains("|Cols|", StringComparison.Ordinal)),
            orderedAutoFilterFingerprints.Length,
            sheetProtectionFingerprints.Count,
            dataValidations,
            conditionalFormats,
            pageSetupFingerprints.Count,
            pageBreaks,
            activeSheetIndex,
            hssf.IsDate1904(),
            HasVbaProjectPackage: SourceHasVbaProjectPackage(path),
            SheetNames: sheetNames,
            SheetKindFingerprints: ReadSourceSheetKindFingerprints(hssf),
            SheetVisibilityFingerprints: sheetVisibilityFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            WorkbookCodeName: ReadHssfWorkbookCodeName(hssf),
            SheetCodeNameFingerprints: sheetCodeNameFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            WorkbookCountryFingerprints: ReadSourceWorkbookCountryFingerprints(hssf),
            WorkbookLegacyMenuFingerprints: ReadSourceWorkbookLegacyMenuFingerprints(hssf),
            WorkbookLegacyWorkbookFingerprints: ReadSourceWorkbookLegacyWorkbookFingerprints(hssf),
            WorkbookFunctionGroupFingerprints: ReadSourceWorkbookFunctionGroupFingerprints(hssf),
            WorkbookPropertiesFingerprints: workbookPropertiesFingerprints,
            WorkbookViewFingerprints: workbookViewFingerprints,
            WorkbookProtectionFingerprints: workbookProtectionFingerprints,
            WorkbookFileSharingFingerprints: workbookFileSharingFingerprints,
            WorkbookCalculationFingerprints: workbookCalculationFingerprints,
            SheetCalculationFingerprints: sheetCalculationFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            CellFingerprints: cellFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            MergeFingerprints: mergeFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DimensionFingerprints: dimensionFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DefaultDimensionFingerprints: defaultDimensionFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            StyleFingerprints: styleFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            HeaderFooterFingerprints: [],
            DefinedNameFingerprints: definedNameFingerprints,
            HyperlinkFingerprints: hyperlinkFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            CommentFingerprints: commentFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PictureFingerprints: pictureFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            TextBoxFingerprints: textBoxFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DrawingShapeFingerprints: drawingShapeFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            FormControlFingerprints: formControlFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PaneFingerprints: paneFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            RowOutlineFingerprints: rowOutlineFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ColOutlineFingerprints: colOutlineFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            OutlineSettingFingerprints: outlineSettingFingerprints,
            PrintLayoutFingerprints: orderedPrintLayoutFingerprints,
            PrintOptionsFingerprints: printOptionsFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SheetLegacyPrintSizeFingerprints: ReadSourceLegacyPrintSizeFingerprints(hssf),
            PrimaryViewMetadataFingerprints: primaryViewMetadataFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PageSetupFingerprints: pageSetupFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ViewStateFingerprints: viewStateFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            AutoFilterFingerprints: orderedAutoFilterFingerprints,
            SheetProtectionFingerprints: sheetProtectionFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DataValidationFingerprints: dataValidationFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ConditionalFormatFingerprints: conditionalFormatFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static LegacyXlsCorpusSummary ReadExcelDataReaderSourceSummary(string path, Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = 0;
        var cells = 0;
        var merges = 0;
        var dimensions = 0;
        var styles = 0;
        var hiddenSheets = 0;
        var veryHiddenSheets = 0;
        int? activeSheetIndex = null;
        var sheetNames = new List<string>();
        var sheetVisibilityFingerprints = new List<string>();
        var sheetCodeNameFingerprints = new List<string>();
        var cellFingerprints = new List<string>();
        var mergeFingerprints = new List<string>();
        var dimensionFingerprints = new List<string>();
        var styleFingerprints = new List<string>();
        var headerFooterFingerprints = new List<string>();

        do
        {
            sheets++;
            var sheetIndex = sheets - 1;
            sheetNames.Add(reader.Name);
            sheetVisibilityFingerprints.Add(CreateSheetVisibilityFingerprint(
                sheetIndex,
                reader.Name,
                NormalizeExcelDataReaderVisibleState(reader.VisibleState)));
            if (!string.IsNullOrWhiteSpace(reader.CodeName))
                sheetCodeNameFingerprints.Add(CreateSheetCodeNameFingerprint(sheetIndex, reader.Name, reader.CodeName));
            if (reader.IsActiveSheet)
                activeSheetIndex = sheetIndex;
            if (!string.Equals(reader.VisibleState, "visible", StringComparison.OrdinalIgnoreCase))
                hiddenSheets++;
            if (string.Equals(reader.VisibleState, "veryHidden", StringComparison.OrdinalIgnoreCase))
                veryHiddenSheets++;
            if (TryCreateExcelDataReaderHeaderFooterFingerprint(reader, sheetIndex, reader.Name, out var headerFooterFingerprint))
                headerFooterFingerprints.Add(headerFooterFingerprint);

            merges += reader.MergeCells?.Length ?? 0;
            foreach (var range in reader.MergeCells ?? [])
            {
                if (range.FromRow <= range.ToRow && range.FromColumn <= range.ToColumn)
                    mergeFingerprints.Add(CreateMergeFingerprint(sheetIndex, reader.Name, range));
            }

            for (var column = 0; column < reader.FieldCount; column++)
            {
                var width = reader.GetColumnWidth(column);
                if (width > 0)
                {
                    dimensions++;
                    dimensionFingerprints.Add(CreateDimensionFingerprint(
                        sheetIndex,
                        reader.Name,
                        "ColWidth",
                        (uint)column + 1,
                        FormatDouble(width)));
                }
            }

            while (reader.Read())
            {
                if (reader.RowHeight > 0)
                {
                    dimensions++;
                    dimensionFingerprints.Add(CreateDimensionFingerprint(
                        sheetIndex,
                        reader.Name,
                        "RowHeight",
                        (uint)reader.Depth + 1,
                        FormatDouble(PointsToPixels(reader.RowHeight))));
                }

                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var value = MapExcelDataReaderCellValue(reader, column);
                    if (value is not BlankValue)
                    {
                        cells++;
                        cellFingerprints.Add(CreateCellFingerprint(
                            sheetIndex,
                            reader.Name,
                            (uint)reader.Depth + 1,
                            (uint)column + 1,
                            "",
                            ImportedValueToken(value)));

                        if (TryCreateExcelDataReaderStyleFingerprint(reader, sheetIndex, reader.Name, column, out var fingerprint))
                        {
                            styles++;
                            styleFingerprints.Add(fingerprint);
                        }
                    }
                }
            }
        }
        while (reader.NextResult());

        return new LegacyXlsCorpusSummary(
            Path.GetFileName(path),
            sheets,
            cells,
            Formulas: 0,
            Styles: styles,
            Merges: merges,
            Dimensions: dimensions,
            HiddenSheets: hiddenSheets,
            VeryHiddenSheets: veryHiddenSheets,
            DefinedNames: 0,
            Hyperlinks: 0,
            Comments: 0,
            Pictures: 0,
            TextBoxes: 0,
            DrawingShapes: 0,
            FormControls: 0,
            FreezePanes: 0,
            RowOutlineLevels: 0,
            ColOutlineLevels: 0,
            PrintAreas: 0,
            PrintTitleRows: 0,
            PrintTitleColumns: 0,
            AutoFilters: 0,
            ProtectedSheets: 0,
            DataValidations: 0,
            ConditionalFormats: 0,
            PageSetupSheets: 0,
            PageBreaks: 0,
            ActiveSheetIndex: activeSheetIndex,
            Uses1904DateSystem: false,
            RichMetadata: false,
            HasVbaProjectPackage: SourceHasVbaProjectPackage(path),
            SheetNames: sheetNames,
            SheetVisibilityFingerprints: sheetVisibilityFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            WorkbookCodeName: null,
            SheetCodeNameFingerprints: sheetCodeNameFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            WorkbookCountryFingerprints: [],
            WorkbookLegacyMenuFingerprints: [],
            WorkbookFunctionGroupFingerprints: [],
            WorkbookPropertiesFingerprints: [],
            WorkbookViewFingerprints: [],
            WorkbookProtectionFingerprints: [],
            WorkbookFileSharingFingerprints: [],
            WorkbookCalculationFingerprints: [],
            SheetCalculationFingerprints: [],
            CellFingerprints: cellFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            MergeFingerprints: mergeFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DimensionFingerprints: dimensionFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DefaultDimensionFingerprints: [],
            StyleFingerprints: styleFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            HeaderFooterFingerprints: headerFooterFingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DefinedNameFingerprints: [],
            HyperlinkFingerprints: [],
            CommentFingerprints: [],
            PictureFingerprints: [],
            TextBoxFingerprints: [],
            DrawingShapeFingerprints: [],
            FormControlFingerprints: [],
            PaneFingerprints: [],
            RowOutlineFingerprints: [],
            ColOutlineFingerprints: [],
            OutlineSettingFingerprints: [],
            PrintLayoutFingerprints: [],
            PrintOptionsFingerprints: [],
            SheetLegacyPrintSizeFingerprints: [],
            PrimaryViewMetadataFingerprints: [],
            PageSetupFingerprints: [],
            ViewStateFingerprints: [],
            AutoFilterFingerprints: [],
            SheetProtectionFingerprints: [],
            DataValidationFingerprints: [],
            ConditionalFormatFingerprints: []);
    }

    private static IReadOnlyList<string> ReadImportedSheetVisibilityFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreateSheetVisibilityFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.IsVeryHidden ? "VeryHidden" : sheet.IsHidden ? "Hidden" : "Visible"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedSheetKindFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreateSheetKindFingerprint(sheetIndex, sheet.Name, sheet.Kind))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadSourceSheetKindFingerprints(HSSFWorkbook hssf)
    {
        var fingerprints = new List<string>();
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            var kind = sheet is HSSFSheet hssfSheet &&
                       hssfSheet.Sheet.FindFirstRecordBySid(WSBoolRecord.sid) is WSBoolRecord { Dialog: true }
                ? SheetKind.DialogSheet
                : SheetKind.Worksheet;
            fingerprints.Add(CreateSheetKindFingerprint(sheetIndex, sheet.SheetName, kind));
        }

        return fingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string? ReadImportedWorkbookCodeName(Workbook workbook)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(workbook.Properties?.Get("workbookPr"));
        return attributes.TryGetValue("codeName", out var codeName) && !string.IsNullOrWhiteSpace(codeName)
            ? codeName
            : null;
    }

    private static IReadOnlyList<string> ReadImportedSheetCodeNameFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => string.IsNullOrWhiteSpace(sheet.CodeName)
                ? null
                : CreateSheetCodeNameFingerprint(sheetIndex, sheet.Name, sheet.CodeName))
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedWorkbookCountryFingerprints(Workbook workbook) =>
        workbook.CountrySettings is { } countrySettings
            ? [CreateWorkbookCountryFingerprint(countrySettings.DefaultCountryId, countrySettings.CurrentCountryId)]
            : [];

    private static IReadOnlyList<string> ReadImportedWorkbookLegacyMenuFingerprints(Workbook workbook) =>
        workbook.LegacyMenuSettings is { } menuSettings
            ? [CreateWorkbookLegacyMenuFingerprint(menuSettings.AddMenuCount, menuSettings.DeleteMenuCount)]
            : [];

    private static IReadOnlyList<string> ReadImportedWorkbookLegacyWorkbookFingerprints(Workbook workbook)
    {
        if (workbook.LegacyWorkbookSettings is not { } settings)
            return [];

        var sheetTabIds = settings.SheetTabIds ?? [];
        return sheetTabIds.Count == 0 && settings.UseNaturalLanguageFormulas is null
            ? []
            : [CreateWorkbookLegacyWorkbookFingerprint(sheetTabIds, settings.UseNaturalLanguageFormulas)];
    }

    private static IReadOnlyList<string> ReadImportedWorkbookFunctionGroupFingerprints(Workbook workbook) =>
        workbook.FunctionGroups?.BuiltInGroupCount is { Length: > 0 } builtInGroupCount
            ? [CreateWorkbookFunctionGroupFingerprint(builtInGroupCount)]
            : [];

    private static IReadOnlyList<string> ReadImportedWorkbookPropertiesFingerprints(Workbook workbook)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(workbook.Properties?.Get("workbookPr"));
        var backupFile = attributes.GetValueOrDefault("backupFile");
        var showObjects = attributes.GetValueOrDefault("showObjects");
        var saveExternalLinkValues = attributes.GetValueOrDefault("saveExternalLinkValues");
        var refreshAllConnections = attributes.GetValueOrDefault("refreshAllConnections");
        return HasAnyWorkbookProperties(backupFile, showObjects, saveExternalLinkValues, refreshAllConnections)
            ? [CreateWorkbookPropertiesFingerprint(backupFile, showObjects, saveExternalLinkValues, refreshAllConnections)]
            : [];
    }

    private static IReadOnlyList<string> ReadImportedWorkbookViewFingerprints(Workbook workbook) =>
    [
        CreateWorkbookViewFingerprint(
            workbook.ShowSheetTabs,
            workbook.SheetTabRatio,
            workbook.FirstVisibleSheetIndex,
            workbook.ActiveSheetIndex)
    ];

    private static IReadOnlyList<string> ReadSourceWorkbookViewFingerprints(HSSFWorkbook hssf)
    {
        bool? showSheetTabs = null;
        int? sheetTabRatio = null;
        int? firstVisibleSheetIndex = hssf.FirstVisibleTab >= 0 && hssf.FirstVisibleTab < hssf.NumberOfSheets
            ? hssf.FirstVisibleTab
            : null;
        var activeSheetIndex = hssf.ActiveSheetIndex >= 0 && hssf.ActiveSheetIndex < hssf.NumberOfSheets
            ? hssf.ActiveSheetIndex
            : (int?)null;

        if (hssf.Workbook.FindFirstRecordBySid(WindowOneRecord.sid) is WindowOneRecord window)
        {
            showSheetTabs = window.DisplayTabs;
            sheetTabRatio = Math.Clamp((int)window.TabWidthRatio, 0, 1000);
            if (window.FirstVisibleTab >= 0 && window.FirstVisibleTab < hssf.NumberOfSheets)
                firstVisibleSheetIndex = window.FirstVisibleTab;
        }

        return
        [
            CreateWorkbookViewFingerprint(
                showSheetTabs,
                sheetTabRatio,
                firstVisibleSheetIndex,
                activeSheetIndex)
        ];
    }

    private static IReadOnlyList<string> ReadSourceWorkbookPropertiesFingerprints(HSSFWorkbook hssf)
    {
        var backupFile = hssf.Workbook.FindFirstRecordBySid(BackupRecord.sid) is BackupRecord backup
            ? FormatWorkbookPropertyBool(backup.Backup != 0)
            : null;
        var showObjects = hssf.Workbook.FindFirstRecordBySid(HideObjRecord.sid) is HideObjRecord hideObjects
            ? MapSourceShowObjects(hideObjects.GetHideObj())
            : null;
        var saveExternalLinkValues = hssf.Workbook.FindFirstRecordBySid(BookBoolRecord.sid) is BookBoolRecord bookBool
            ? FormatWorkbookPropertyBool(bookBool.SaveLinkValues != 0)
            : null;
        var refreshAllConnections = hssf.Workbook.FindFirstRecordBySid(RefreshAllRecord.sid) is RefreshAllRecord refreshAll
            ? FormatWorkbookPropertyBool(refreshAll.RefreshAll)
            : null;

        return HasAnyWorkbookProperties(backupFile, showObjects, saveExternalLinkValues, refreshAllConnections)
            ? [CreateWorkbookPropertiesFingerprint(backupFile, showObjects, saveExternalLinkValues, refreshAllConnections)]
            : [];
    }

    private static IReadOnlyList<string> ReadSourceWorkbookCountryFingerprints(HSSFWorkbook hssf) =>
        hssf.Workbook.FindFirstRecordBySid(CountryRecord.sid) is CountryRecord country
            ? [CreateWorkbookCountryFingerprint(PositiveOrNull(country.DefaultCountry), PositiveOrNull(country.CurrentCountry))]
            : [];

    private static IReadOnlyList<string> ReadSourceWorkbookLegacyMenuFingerprints(HSSFWorkbook hssf)
    {
        if (hssf.Workbook.FindFirstRecordBySid(MMSRecord.sid) is not MMSRecord menuSettings)
            return [];

        var addMenuCount = PositiveOrNull(menuSettings.AddMenuCount);
        var deleteMenuCount = PositiveOrNull(menuSettings.DelMenuCount);
        return addMenuCount is null && deleteMenuCount is null
            ? []
            : [CreateWorkbookLegacyMenuFingerprint(addMenuCount, deleteMenuCount)];
    }

    private static IReadOnlyList<string> ReadSourceWorkbookLegacyWorkbookFingerprints(HSSFWorkbook hssf)
    {
        var sheetTabIds = ReadHssfSheetTabIds(hssf);
        var useNaturalLanguageFormulas = ReadHssfUseNaturalLanguageFormulas(hssf);
        return sheetTabIds.Count == 0 && useNaturalLanguageFormulas is null
            ? []
            : [CreateWorkbookLegacyWorkbookFingerprint(sheetTabIds, useNaturalLanguageFormulas)];
    }

    private static List<int> ReadHssfSheetTabIds(HSSFWorkbook hssf)
    {
        if (hssf.Workbook.FindFirstRecordBySid(TabIdRecord.sid) is not TabIdRecord tabIdRecord ||
            TabIdRecordTabIdsField?.GetValue(tabIdRecord) is not short[] tabIds)
        {
            return [];
        }

        return tabIds
            .Select(value => (int)value)
            .Where(value => value >= 0)
            .ToList();
    }

    private static bool? ReadHssfUseNaturalLanguageFormulas(HSSFWorkbook hssf)
    {
        if (hssf.Workbook.FindFirstRecordBySid(UseSelFSRecord.sid) is not UseSelFSRecord useSelFs ||
            UseSelFsRecordOptionsField?.GetValue(useSelFs) is not { } options)
        {
            return null;
        }

        return Convert.ToInt32(options, CultureInfo.InvariantCulture) != 0;
    }

    private static IReadOnlyList<string> ReadSourceWorkbookFunctionGroupFingerprints(HSSFWorkbook hssf) =>
        hssf.Workbook.FindFirstRecordBySid(FnGroupCountRecord.sid) is FnGroupCountRecord functionGroups &&
        PositiveOrNull(functionGroups.Count) is { } builtInGroupCount
            ? [CreateWorkbookFunctionGroupFingerprint(builtInGroupCount.ToString(CultureInfo.InvariantCulture))]
            : [];

    private static IReadOnlyList<string> ReadImportedWorkbookProtectionFingerprints(Workbook workbook)
    {
        var lockWindows = GetWorkbookProtectionMetadataAttribute(workbook, "lockWindows") == "1";
        if (!workbook.IsStructureProtected &&
            string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword) &&
            !lockWindows)
        {
            return [];
        }

        return
        [
            CreateWorkbookProtectionFingerprint(
                workbook.IsStructureProtected,
                workbook.StructureProtectionPassword,
                lockWindows)
        ];
    }

    private static IReadOnlyList<string> ReadSourceWorkbookProtectionFingerprints(HSSFWorkbook hssf)
    {
        var structureProtected =
            hssf.Workbook.FindFirstRecordBySid(ProtectRecord.sid) is ProtectRecord protect &&
            protect.Protect;
        var windowProtected =
            hssf.Workbook.FindFirstRecordBySid(WindowProtectRecord.sid) is WindowProtectRecord windowProtect &&
            windowProtect.Protect;
        var passwordHash = hssf.Workbook.FindFirstRecordBySid(PasswordRecord.sid) is PasswordRecord { Password: not 0 } password
            ? ((ushort)password.Password).ToString("X4", CultureInfo.InvariantCulture)
            : null;

        if (!structureProtected && !windowProtected && string.IsNullOrWhiteSpace(passwordHash))
            return [];

        return
        [
            CreateWorkbookProtectionFingerprint(
                structureProtected,
                passwordHash,
                windowProtected)
        ];
    }

    private static IReadOnlyList<string> ReadImportedWorkbookFileSharingFingerprints(Workbook workbook)
    {
        if (workbook.FileSharing is not { } fileSharing)
            return [];

        return
        [
            CreateWorkbookFileSharingFingerprint(
                fileSharing.ReadOnlyRecommended,
                fileSharing.UserName,
                fileSharing.ReservationPassword)
        ];
    }

    private static IReadOnlyList<string> ReadSourceWorkbookFileSharingFingerprints(HSSFWorkbook hssf)
    {
        var writeAccessUser = GetSourceWriteAccessUser(hssf);
        if (hssf.Workbook.FindFirstRecordBySid(FileSharingRecord.sid) is not FileSharingRecord fileSharing)
        {
            return writeAccessUser is null
                ? []
                :
                [
                    CreateWorkbookFileSharingFingerprint(
                        null,
                        writeAccessUser,
                        null)
                ];
        }

        var readOnlyRecommended = fileSharing.ReadOnly != 0;
        var userName = string.IsNullOrWhiteSpace(fileSharing.Username) ? writeAccessUser : fileSharing.Username.Trim();
        var reservationPassword = fileSharing.Password != 0
            ? ((ushort)fileSharing.Password).ToString("X4", CultureInfo.InvariantCulture)
            : null;

        if (!readOnlyRecommended &&
            userName is null &&
            reservationPassword is null)
        {
            return [];
        }

        return
        [
            CreateWorkbookFileSharingFingerprint(
                readOnlyRecommended,
                userName,
                reservationPassword)
        ];
    }

    private static string? GetSourceWriteAccessUser(HSSFWorkbook hssf)
    {
        if (hssf.Workbook.FindFirstRecordBySid(WriteAccessRecord.sid) is not WriteAccessRecord writeAccess)
            return null;

        var userName = writeAccess.Username?.Trim();
        return string.IsNullOrWhiteSpace(userName) ? null : userName;
    }

    private static IReadOnlyList<string> ReadImportedWorkbookCalculationFingerprints(Workbook workbook) =>
    [
        CreateWorkbookCalculationFingerprint(
            workbook.CalculationMode,
            workbook.FullCalculationOnLoad,
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange)
    ];

    private static IReadOnlyList<string> ReadSourceWorkbookCalculationFingerprints(HSSFWorkbook hssf)
    {
        var mode = FindSourceCalculationRecord<CalcModeRecord>(hssf, CalcModeRecord.sid) is { } calcMode &&
                   calcMode.GetCalcMode() == CalcModeRecord.MANUAL
            ? WorkbookCalculationMode.Manual
            : WorkbookCalculationMode.Automatic;
        var iterative = FindSourceCalculationRecord<IterationRecord>(hssf, IterationRecord.sid) is { } iteration &&
                        iteration.Iteration;
        var maxIterations = FindSourceCalculationRecord<CalcCountRecord>(hssf, CalcCountRecord.sid) is { } calcCount &&
                            calcCount.Iterations is > 0 and not 100
            ? calcCount.Iterations
            : (int?)null;
        var maxChange = FindSourceCalculationRecord<DeltaRecord>(hssf, DeltaRecord.sid) is { } delta &&
                        delta.MaxChange > 0 &&
                        Math.Abs(delta.MaxChange - 0.001) > 0.0000000001
            ? delta.MaxChange
            : (double?)null;

        return
        [
            CreateWorkbookCalculationFingerprint(
                mode,
                hssf.ForceFormulaRecalculation,
                iterative,
                maxIterations,
                maxChange)
        ];
    }

    private static TRecord? FindSourceCalculationRecord<TRecord>(HSSFWorkbook hssf, short sid)
        where TRecord : class
    {
        if (hssf.Workbook.FindFirstRecordBySid(sid) is TRecord workbookRecord)
            return workbookRecord;

        if (hssf.NumberOfSheets == 0 ||
            hssf.GetSheetAt(0) is not HSSFSheet firstSheet)
        {
            return null;
        }

        return firstSheet.Sheet.FindFirstRecordBySid(sid) as TRecord;
    }

    private static IReadOnlyList<string> ReadImportedSheetCalculationFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreateSheetCalculationFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.FullCalculationOnLoad))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

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

    private static IReadOnlyList<string> ReadImportedMergeFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.MergedRegions
                .Select(range => CreateMergeFingerprint(sheetIndex, sheet.Name, range)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedDimensionFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) =>
            {
                var fingerprints = new List<string>();
                fingerprints.AddRange(sheet.HiddenRows
                    .Select(row => CreateDimensionFingerprint(sheetIndex, sheet.Name, "HiddenRow", row, "true")));
                fingerprints.AddRange(sheet.RowHeights
                    .Select(entry => CreateDimensionFingerprint(sheetIndex, sheet.Name, "RowHeight", entry.Key, FormatDouble(entry.Value))));
                fingerprints.AddRange(sheet.HiddenCols
                    .Select(column => CreateDimensionFingerprint(sheetIndex, sheet.Name, "HiddenCol", column, "true")));
                fingerprints.AddRange(sheet.ColumnWidths
                    .Select(entry => CreateDimensionFingerprint(sheetIndex, sheet.Name, "ColWidth", entry.Key, FormatDouble(entry.Value))));
                return fingerprints;
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedDefaultDimensionFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreateDefaultDimensionFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.DefaultColumnWidth,
                sheet.DefaultRowHeight))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedFallbackStyleFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.EnumerateCells()
                .OrderBy(entry => entry.Address.Row)
                .ThenBy(entry => entry.Address.Col)
                .Select(entry =>
                {
                    var style = workbook.GetStyle(entry.Cell.StyleId);
                    return Equals(style, ModelCellStyle.Default)
                        ? null
                        : CreateFallbackStyleFingerprint(
                            sheetIndex,
                            sheet.Name,
                            entry.Address.Row,
                            entry.Address.Col,
                            style.NumberFormat,
                            style.HorizontalAlignment,
                            style.VerticalAlignment,
                            style.IndentLevel,
                            style.Locked,
                            style.Hidden);
                }))
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedRichStyleFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.EnumerateCells()
                .Where(entry => entry.Cell.StyleId != StyleId.Default)
                .Select(entry => CreateRichStyleFingerprint(
                    sheetIndex,
                    sheet.Name,
                    entry.Address.Row,
                    entry.Address.Col,
                    workbook.GetStyle(entry.Cell.StyleId)))
                .Concat(sheet.GetStyleOnlyEntries()
                    .Select(entry => CreateRichStyleFingerprint(
                        sheetIndex,
                        sheet.Name,
                        entry.Key.Row,
                        entry.Key.Col,
                        workbook.GetStyle(entry.StyleId)))))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedFallbackHeaderFooterFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => TryCreateHeaderFooterFingerprint(
                sheetIndex,
                sheet.Name,
                sheet.PageHeader,
                sheet.PageFooter,
                out var fingerprint)
                    ? fingerprint
                    : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedDefinedNameFingerprints(Workbook workbook) =>
        workbook.NamedRanges
            .Select(entry =>
            {
                workbook.TryGetNamedRangeMetadata(entry.Key, out var metadata);
                return CreateDefinedNameFingerprint(
                    entry.Key,
                    "Range",
                    CreateRangeToken(entry.Value),
                    metadata.Scope,
                    metadata.Comment);
            })
            .Concat(workbook.NamedFormulas.Select(entry => CreateDefinedNameFingerprint(
                entry.Key,
                "Formula",
                NormalizeFormulaText(entry.Value),
                "",
                "")))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
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

    private static IReadOnlyList<string> ReadImportedPictureFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.Pictures
                .Select(picture => CreatePictureFingerprint(
                    sheetIndex,
                    sheet.Name,
                    picture.Anchor.Row,
                    picture.Anchor.Col,
                    picture.ContentType,
                    picture.ImageBytes)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedTextBoxFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.TextBoxes
                .Select(textBox => CreateTextBoxFingerprint(
                    sheetIndex,
                    sheet.Name,
                    textBox.Anchor.Row,
                    textBox.Anchor.Col,
                    textBox.Text,
                    textBox.FillColor)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedDrawingShapeFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.DrawingShapes
                .Select(shape => CreateDrawingShapeFingerprint(
                    sheetIndex,
                    sheet.Name,
                    shape.Anchor.Row,
                    shape.Anchor.Col,
                    shape.Kind,
                    shape.HasFill,
                    shape.FillColor,
                    shape.OutlineColor)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedFormControlFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.FormControls
                .Where(control => control.Anchor is not null)
                .Select(control => CreateFormControlFingerprint(
                    sheetIndex,
                    sheet.Name,
                    control.Anchor!.Value,
                    control.Kind,
                    control.Name,
                    control.ListFillRange,
                    control.SelectedIndex)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadSourcePictureFingerprints(int sheetIndex, HSSFSheet sheet)
    {
        if (sheet.DrawingPatriarch is not HSSFPatriarch patriarch)
            return [];

        return EnumerateSourcePictures(patriarch.Children)
            .Select(picture =>
            {
                if (picture.Anchor is not HSSFClientAnchor anchor)
                    return null;

                return CreatePictureFingerprint(
                    sheetIndex,
                    sheet.SheetName,
                    ToSourceModelIndex(Math.Min(anchor.Row1, anchor.Row2)),
                    ToSourceModelIndex(Math.Min(anchor.Col1, anchor.Col2)),
                    picture.PictureData?.MimeType,
                    picture.PictureData?.Data);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadSourceTextBoxFingerprints(int sheetIndex, HSSFSheet sheet)
    {
        if (sheet.DrawingPatriarch is not HSSFPatriarch patriarch)
            return [];

        return EnumerateSourceTextBoxes(patriarch.Children)
            .Select(textBox =>
            {
                if (textBox.Anchor is not HSSFClientAnchor anchor)
                    return null;

                var fillColor = TryGetSourceHssfRgbColor(textBox.FillColor, out var color)
                    ? color
                    : (CellColor?)null;
                return CreateTextBoxFingerprint(
                    sheetIndex,
                    sheet.SheetName,
                    ToSourceModelIndex(Math.Min(anchor.Row1, anchor.Row2)),
                    ToSourceModelIndex(Math.Min(anchor.Col1, anchor.Col2)),
                    textBox.String?.String ?? "",
                    fillColor);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadSourceDrawingShapeFingerprints(int sheetIndex, HSSFSheet sheet)
    {
        if (sheet.DrawingPatriarch is not HSSFPatriarch patriarch)
            return [];

        return EnumerateSourceSimpleShapes(patriarch.Children)
            .Select(shape =>
            {
                if (MapSourceHssfShapeKind(shape.ShapeType) is not { } kind ||
                    shape.Anchor is not HSSFClientAnchor anchor)
                {
                    return null;
                }

                var fillColor = TryGetSourceHssfRgbColor(shape.FillColor, out var fill)
                    ? fill
                    : (CellColor?)null;
                var outlineColor = TryGetSourceHssfRgbColor(shape.LineStyleColor, out var outline)
                    ? outline
                    : (CellColor?)null;

                return CreateDrawingShapeFingerprint(
                    sheetIndex,
                    sheet.SheetName,
                    ToSourceModelIndex(Math.Min(anchor.Row1, anchor.Row2)),
                    ToSourceModelIndex(Math.Min(anchor.Col1, anchor.Col2)),
                    kind,
                    kind is not DrawingShapeKind.Line && !shape.IsNoFill,
                    fillColor,
                    outlineColor);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadSourceFormControlFingerprints(HSSFWorkbook hssf, int sheetIndex, HSSFSheet sheet)
    {
        if (sheet.DrawingPatriarch is not HSSFPatriarch patriarch)
            return [];

        return EnumerateSourceFormControls(patriarch.Children)
            .Select(shape =>
            {
                if (MapSourceHssfFormControlKind(shape.ShapeType) is not { } kind ||
                    shape.Anchor is not HSSFClientAnchor anchor ||
                    IsSourceAutoFilterDropDown(hssf, sheet, anchor))
                {
                    return null;
                }

                var range = new GridRange(
                    new ModelCellAddress(
                        default,
                        ToSourceModelIndex(Math.Min(anchor.Row1, anchor.Row2)),
                        ToSourceModelIndex(Math.Min(anchor.Col1, anchor.Col2))),
                    new ModelCellAddress(
                        default,
                        ToSourceModelIndex(Math.Max(anchor.Row1, anchor.Row2)),
                        ToSourceModelIndex(Math.Max(anchor.Col1, anchor.Col2))));

                return CreateFormControlFingerprint(
                    sheetIndex,
                    sheet.SheetName,
                    range,
                    kind,
                    FirstNonBlank(shape.Name, shape.ShapeName),
                    TryGetSourceLbsDataSubRecord(shape) is { } lbsData &&
                    TryFormatSourceLbsListFillRange(hssf, lbsData, out var listFillRange)
                        ? listFillRange
                        : null,
                    TryGetSourceLbsDataSubRecord(shape) is { } selectedLbsData &&
                    TryGetSourceLbsSelectedIndex(selectedLbsData, out var selectedIndex)
                        ? selectedIndex
                        : null);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static LbsDataSubRecord? TryGetSourceLbsDataSubRecord(HSSFSimpleShape sourceControl)
    {
        try
        {
            return TryGetSourceObjRecord(sourceControl)?.SubRecords
                    .OfType<LbsDataSubRecord>()
                    .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static ObjRecord? TryGetSourceObjRecord(HSSFSimpleShape sourceControl) =>
        HssfGetObjRecordMethod?.Invoke(sourceControl, null) as ObjRecord;

    private static bool TryFormatSourceLbsListFillRange(
        HSSFWorkbook hssf,
        LbsDataSubRecord lbsData,
        out string listFillRange)
    {
        listFillRange = "";
        if (lbsData.Formula is not { } formula)
            return false;

        try
        {
            var text = NormalizeFormulaText(HSSFFormulaParser.ToFormulaString(hssf, [formula])).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            listFillRange = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSourceLbsSelectedIndex(LbsDataSubRecord lbsData, out int selectedIndex)
    {
        selectedIndex = 0;
        if (LbsSelectedIndexField?.GetValue(lbsData) is not int raw || raw <= 0)
            return false;

        selectedIndex = raw;
        return true;
    }

    private static bool IsSourceAutoFilterDropDown(HSSFWorkbook hssf, HSSFSheet sheet, HSSFClientAnchor anchor)
    {
        var anchorRow = ToSourceModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var anchorCol = ToSourceModelIndex(Math.Min(anchor.Col1, anchor.Col2));

        for (var index = 0; index < hssf.NumberOfNames; index++)
        {
            var name = hssf.GetNameAt(index);
            if (name is null ||
                name.IsDeleted ||
                !IsAutoFilterDefinedName(name.NameName) ||
                !TryParseSourceAutoFilterRange(name.RefersToFormula, out var sheetName, out var range) ||
                !string.Equals(sheetName, sheet.SheetName, StringComparison.Ordinal))
            {
                continue;
            }

            if (anchorRow == range.Start.Row &&
                anchorCol >= range.Start.Col &&
                anchorCol <= range.End.Col)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseSourceAutoFilterRange(string? refersTo, out string sheetName, out GridRange range)
    {
        sheetName = "";
        range = default;
        var text = NormalizeFormulaText(refersTo ?? "").Trim();
        if (!TrySplitSheetQualifiedReference(text, out sheetName, out var rangeText))
            return false;

        var parts = rangeText.Split(':');
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseA1Part(parts[0], default, out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseA1Part(endText, default, out var end))
            return false;

        range = new GridRange(start, end);
        return true;
    }

    private static IEnumerable<HSSFPicture> EnumerateSourcePictures(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFPicture picture)
                yield return picture;

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedPicture in EnumerateSourcePictures(group.Children))
                    yield return nestedPicture;
            }
        }
    }

    private static IEnumerable<HSSFTextbox> EnumerateSourceTextBoxes(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFTextbox textBox && shape is not HSSFComment)
                yield return textBox;

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedTextBox in EnumerateSourceTextBoxes(group.Children))
                    yield return nestedTextBox;
            }
        }
    }

    private static IEnumerable<HSSFSimpleShape> EnumerateSourceSimpleShapes(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFSimpleShape simpleShape &&
                shape is not HSSFTextbox &&
                shape is not HSSFComment &&
                shape is not HSSFPicture &&
                shape is not HSSFCombobox)
            {
                yield return simpleShape;
            }

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedShape in EnumerateSourceSimpleShapes(group.Children))
                    yield return nestedShape;
            }
        }
    }

    private static IEnumerable<HSSFSimpleShape> EnumerateSourceFormControls(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFCombobox comboBox)
            {
                yield return comboBox;
            }
            else if (shape is HSSFSimpleShape { ShapeType: HSSFSimpleShape.OBJECT_TYPE_COMBO_BOX } comboShape)
            {
                yield return comboShape;
            }

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedControl in EnumerateSourceFormControls(group.Children))
                    yield return nestedControl;
            }
        }
    }

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

    private static IReadOnlyList<string> ReadImportedOutlineSettingFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) =>
            {
                var hasOutlineLevels = sheet.RowOutlineLevels.Count > 0 || sheet.ColOutlineLevels.Count > 0;
                if (!hasOutlineLevels &&
                    sheet.OutlineSummaryBelow is null &&
                    sheet.OutlineSummaryRight is null &&
                    sheet.ShowOutlineSymbols is null)
                {
                    return null;
                }

                return CreateOutlineSettingFingerprint(
                    sheetIndex,
                    sheet.Name,
                    sheet.OutlineSummaryBelow,
                    sheet.OutlineSummaryRight,
                    sheet.ShowOutlineSymbols);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadSourceOutlineSettingFingerprints(HSSFWorkbook hssf)
    {
        var fingerprints = new List<string>();
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            if (!HasSourceOutlineLevels(sheet) &&
                sheet.RowSumsBelow &&
                sheet.RowSumsRight &&
                sheet.DisplayGuts)
            {
                continue;
            }

            fingerprints.Add(CreateOutlineSettingFingerprint(
                sheetIndex,
                sheet.SheetName,
                sheet.RowSumsBelow,
                sheet.RowSumsRight,
                sheet.DisplayGuts));
        }

        return fingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool HasSourceOutlineLevels(ISheet sheet)
    {
        for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            if (sheet.GetRow(rowIndex)?.OutlineLevel > 0)
                return true;
        }

        for (var columnIndex = 0; columnIndex <= LegacyXlsMaxColumnIndex; columnIndex++)
        {
            if (sheet.GetColumnOutlineLevel(columnIndex) > 0)
                return true;
        }

        return false;
    }

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

    private static IReadOnlyList<string> ReadImportedDataValidationFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.DataValidations
                .Select(validation => CreateDataValidationFingerprint(sheetIndex, sheet.Name, validation)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedConditionalFormatFingerprints(Workbook workbook) =>
        workbook.Sheets
            .SelectMany((sheet, sheetIndex) => sheet.ConditionalFormats
                .Select(format => CreateConditionalFormatFingerprint(sheetIndex, sheet.Name, format)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedPrintOptionsFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) =>
                GetPrintOptionsMetadataAttribute(sheet, "gridLinesSet") is { } gridLinesSet
                    ? CreatePrintOptionsFingerprint(sheetIndex, sheet.Name, gridLinesSet)
                    : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedLegacyPrintSizeFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => sheet.LegacyPrintSize is { } printSize
                ? CreateLegacyPrintSizeFingerprint(sheetIndex, sheet.Name, printSize)
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadImportedPrimaryViewMetadataFingerprints(Workbook workbook) =>
        workbook.Sheets
            .Select((sheet, sheetIndex) => CreatePrimaryViewMetadataFingerprint(
                sheetIndex,
                sheet.Name,
                GetPrimaryViewMetadataAttribute(sheet, "tabSelected"),
                GetPrimaryViewMetadataAttribute(sheet, "defaultGridColor"),
                GetPrimaryViewMetadataAttribute(sheet, "colorId"),
                GetPrimaryViewSelectionToken(sheet)))
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
                sheet.ViewMode,
                sheet.ZoomPercent,
                sheet.ShowGridlines,
                sheet.ShowHeadings,
                sheet.ShowFormulas,
                sheet.ShowZeros,
                sheet.ViewTopRow,
                sheet.ViewLeftCol,
                sheet.ActiveRow,
                sheet.ActiveCol,
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

    private static Workbook CreateSourceDefinedNameWorkbook(HSSFWorkbook hssf)
    {
        var workbook = new Workbook("DefinedNameSource");
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
            workbook.AddSheet(hssf.GetSheetName(sheetIndex));

        for (var nameIndex = 0; nameIndex < hssf.NumberOfNames; nameIndex++)
        {
            var name = hssf.GetNameAt(nameIndex);
            if (!IsImportableDefinedName(name, workbook))
                continue;

            var refersTo = NormalizeFormulaText(name.RefersToFormula ?? "").Trim();
            if (TryParseNamedRangeRefersTo(workbook, refersTo, out var range))
            {
                workbook.DefineNamedRange(
                    name.NameName,
                    range,
                    new NamedRangeMetadata(GetSourceDefinedNameScope(hssf, name), name.Comment ?? ""));
                continue;
            }

            workbook.NamedFormulas[name.NameName] = refersTo;
        }

        return workbook;
    }

    private static bool TryCreateSourcePrintOptionsFingerprint(
        int sheetIndex,
        string sheetName,
        ISheet sheet,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? fingerprint)
    {
        fingerprint = null;
        if (sheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(GridsetRecord.sid) is not GridsetRecord gridset)
        {
            return false;
        }

        fingerprint = CreatePrintOptionsFingerprint(sheetIndex, sheetName, gridset.Gridset ? "1" : "0");
        return true;
    }

    private static IReadOnlyList<string> ReadSourceLegacyPrintSizeFingerprints(HSSFWorkbook hssf)
    {
        var fingerprints = new List<string>();
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sheet = hssf.GetSheetAt(sheetIndex);
            if (sheet is not HSSFSheet hssfSheet ||
                hssfSheet.Sheet.FindFirstRecordBySid(PrintSizeRecord.sid) is not PrintSizeRecord printSize ||
                PositiveOrNull(printSize.PrintSize) is not { } value)
            {
                continue;
            }

            fingerprints.Add(CreateLegacyPrintSizeFingerprint(sheetIndex, sheet.SheetName, value));
        }

        return fingerprints.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool TryCreateSourcePrimaryViewMetadataFingerprint(
        int sheetIndex,
        string sheetName,
        ISheet sheet,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? fingerprint)
    {
        fingerprint = null;
        if (TryGetWindowTwoRecord(sheet) is not { } window)
        {
            return false;
        }

        var selection = GetSourceSelectionToken(sheet);
        if (!window.IsSelected && window.DefaultHeader && window.HeaderColor == 64 && selection is null)
            return false;

        fingerprint = CreatePrimaryViewMetadataFingerprint(
            sheetIndex,
            sheetName,
            window.IsSelected ? "1" : null,
            window.DefaultHeader ? null : "0",
            window.HeaderColor == 64 ? null : window.HeaderColor.ToString(CultureInfo.InvariantCulture),
            selection);
        return fingerprint is not null;
    }

    private static string? GetSourceSelectionToken(ISheet sheet)
    {
        if (TryGetSelectionRecord(sheet) is not { } selection)
            return null;

        var activeCell = ToSourceA1(selection.ActiveCellRow, selection.ActiveCellCol);
        var sqref = CreateSqrefToken(selection.CellReferences);
        if (string.IsNullOrWhiteSpace(sqref))
            sqref = activeCell;
        var activeCellId = selection.ActiveCellRef > 0
            ? selection.ActiveCellRef.ToString(CultureInfo.InvariantCulture)
            : "null";
        if (selection.ActiveCellRef == 0 &&
            string.Equals(activeCell, sqref, StringComparison.Ordinal))
        {
            return null;
        }

        return $"{activeCell},{sqref},{activeCellId}";
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

        var activeCell = sheet.ActiveCell;
        var hasActiveCell = activeCell.Row > 0 || activeCell.Column > 0;
        return CreateViewStateFingerprint(
            sheetIndex,
            sheetName,
            GetSourceViewMode(sheet),
            GetSourceZoomPercent(sheet),
            sheet.DisplayGridlines,
            sheet.DisplayRowColHeadings,
            sheet.DisplayFormulas,
            sheet.DisplayZeros,
            sheet.TopRow > 0 ? ToSourceModelIndex(sheet.TopRow) : null,
            sheet.LeftCol > 0 ? ToSourceModelIndex(sheet.LeftCol) : null,
            hasActiveCell ? ToSourceModelIndex(activeCell.Row) : null,
            hasActiveCell ? ToSourceModelIndex(activeCell.Column) : null,
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

    private static string? CreateSourceDataValidationFingerprint(
        int sheetIndex,
        string sheetName,
        IDataValidation validation)
    {
        var regions = validation.Regions?.CellRangeAddresses;
        if (regions is null || regions.Length == 0)
            return null;

        var constraint = validation.ValidationConstraint;
        var type = MapSourceDataValidationType(constraint.GetValidationType());
        var formula1 = type == DvType.List && constraint.ExplicitListValues is { Length: > 0 } explicitValues
            ? string.Join(",", explicitValues)
            : constraint.Formula1;

        return CreateDataValidationFingerprint(
            sheetIndex,
            sheetName,
            regions.Select(CreateRangeToken),
            type,
            MapSourceDataValidationOperator(constraint.Operator),
            formula1,
            constraint.Formula2,
            validation.EmptyCellAllowed,
            !validation.SuppressDropDownArrow,
            MapSourceDataValidationAlertStyle(validation.ErrorStyle),
            validation.ShowPromptBox,
            validation.ShowErrorBox,
            validation.PromptBoxTitle,
            validation.PromptBoxText,
            validation.ErrorBoxTitle,
            validation.ErrorBoxText);
    }

    private static IReadOnlyList<string> ReadSourceConditionalFormatFingerprints(
        HSSFWorkbook hssf,
        HSSFSheet sheet,
        int sheetIndex)
    {
        var sourceFormats = sheet.SheetConditionalFormatting;
        var fingerprints = new List<string>();
        for (var formatIndex = 0; formatIndex < sourceFormats.NumConditionalFormattings; formatIndex++)
        {
            var sourceFormat = sourceFormats.GetConditionalFormattingAt(formatIndex);
            var ranges = sourceFormat.GetFormattingRanges();
            if (ranges.Length == 0)
                continue;

            for (var ruleIndex = 0; ruleIndex < sourceFormat.NumberOfRules; ruleIndex++)
            {
                var sourceRule = sourceFormat.GetRule(ruleIndex);
                foreach (var range in ranges)
                {
                    if (CreateSourceConditionalFormatFingerprint(hssf, sheetIndex, sheet.SheetName, sourceRule, range) is { } fingerprint)
                        fingerprints.Add(fingerprint);
                }
            }
        }

        return fingerprints;
    }

    private static string? CreateSourceConditionalFormatFingerprint(
        HSSFWorkbook hssf,
        int sheetIndex,
        string sheetName,
        IConditionalFormattingRule rule,
        CellRangeAddressBase range)
    {
        if (rule.ConditionType == ConditionType.CellValueIs)
        {
            return CreateConditionalFormatFingerprint(
                sheetIndex,
                sheetName,
                CreateRangeToken(range),
                CfRuleType.CellValue,
                MapSourceConditionalFormatOperator(rule.ComparisonOperation),
                NormalizeFormulaText(rule.Formula1 ?? ""),
                NormalizeFormulaText(rule.Formula2 ?? ""),
                null,
                Math.Max(1, rule.Priority),
                rule.StopIfTrue,
                CreateSourceConditionalFormatStyleFingerprint(hssf, rule));
        }

        if (rule.ConditionType == ConditionType.Formula)
        {
            return CreateConditionalFormatFingerprint(
                sheetIndex,
                sheetName,
                CreateRangeToken(range),
                CfRuleType.Formula,
                CfOperator.Equal,
                null,
                null,
                NormalizeFormulaText(rule.Formula1 ?? ""),
                Math.Max(1, rule.Priority),
                rule.StopIfTrue,
                CreateSourceConditionalFormatStyleFingerprint(hssf, rule));
        }

        return null;
    }

    private static string CreateCellFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string formula,
        string value) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|F={formula}|V={value}";

    private static string CreateSheetVisibilityFingerprint(int sheetIndex, string sheetName, string visibility) =>
        $"{sheetIndex}:{sheetName}|Visibility={visibility}";

    private static string CreateSheetKindFingerprint(int sheetIndex, string sheetName, SheetKind kind) =>
        $"{sheetIndex}:{sheetName}|Kind={kind}";

    private static string CreateWorkbookViewFingerprint(
        bool? showSheetTabs,
        int? sheetTabRatio,
        int? firstVisibleSheetIndex,
        int? activeSheetIndex) =>
        string.Join("|", [
            "WorkbookView",
            $"Tabs={FormatNullableBool(showSheetTabs)}",
            $"Ratio={FormatNullableInt(sheetTabRatio)}",
            $"First={FormatNullableInt(firstVisibleSheetIndex)}",
            $"Active={FormatNullableInt(activeSheetIndex)}"
        ]);

    private static string CreateWorkbookPropertiesFingerprint(
        string? backupFile,
        string? showObjects,
        string? saveExternalLinkValues,
        string? refreshAllConnections) =>
        string.Join("|", [
            "WorkbookProperties",
            $"Backup={backupFile ?? "null"}",
            $"ShowObjects={showObjects ?? "null"}",
            $"SaveExternalLinkValues={saveExternalLinkValues ?? "null"}",
            $"RefreshAllConnections={refreshAllConnections ?? "null"}"
        ]);

    private static string CreateWorkbookCountryFingerprint(int? defaultCountryId, int? currentCountryId) =>
        string.Join("|", [
            "WorkbookCountry",
            $"Default={FormatNullableInt(defaultCountryId)}",
            $"Current={FormatNullableInt(currentCountryId)}"
        ]);

    private static string CreateWorkbookLegacyMenuFingerprint(int? addMenuCount, int? deleteMenuCount) =>
        string.Join("|", [
            "WorkbookLegacyMenus",
            $"Add={FormatNullableInt(addMenuCount)}",
            $"Delete={FormatNullableInt(deleteMenuCount)}"
        ]);

    private static string CreateWorkbookLegacyWorkbookFingerprint(
        IReadOnlyList<int> sheetTabIds,
        bool? useNaturalLanguageFormulas) =>
        string.Join("|", [
            "WorkbookLegacyWorkbook",
            $"TabIds={string.Join(",", sheetTabIds)}",
            $"UseNaturalLanguageFormulas={FormatNullableBool(useNaturalLanguageFormulas)}"
        ]);

    private static string CreateWorkbookFunctionGroupFingerprint(string builtInGroupCount) =>
        string.Join("|", [
            "WorkbookFunctionGroups",
            $"BuiltInGroupCount={builtInGroupCount}"
        ]);

    private static bool HasAnyWorkbookProperties(params string?[] values) =>
        values.Any(value => value is not null);

    private static string FormatWorkbookPropertyBool(bool value) =>
        value ? "1" : "0";

    private static string MapSourceShowObjects(short hideObjects) =>
        hideObjects switch
        {
            HideObjRecord.HIDE_ALL => "none",
            HideObjRecord.SHOW_PLACEHOLDERS => "placeholders",
            _ => "all"
        };

    private static string CreateWorkbookProtectionFingerprint(
        bool protectedStructureOrWindows,
        string? passwordHash,
        bool lockWindows) =>
        string.Join("|", [
            "WorkbookProtection",
            $"Protected={protectedStructureOrWindows}",
            $"Password={passwordHash ?? ""}",
            $"LockWindows={lockWindows}"
        ]);

    private static string CreateWorkbookFileSharingFingerprint(
        bool? readOnlyRecommended,
        string? userName,
        string? reservationPassword) =>
        string.Join("|", [
            "WorkbookFileSharing",
            $"ReadOnly={FormatNullableBool(readOnlyRecommended)}",
            $"User={EscapeToken(userName ?? "")}",
            $"Password={reservationPassword ?? ""}"
        ]);

    private static string CreateWorkbookCalculationFingerprint(
        WorkbookCalculationMode mode,
        bool fullCalculationOnLoad,
        bool iterativeCalculation,
        int? maxCalculationIterations,
        double? maxCalculationChange) =>
        string.Join("|", [
            "WorkbookCalculation",
            $"Mode={mode}",
            $"Full={fullCalculationOnLoad}",
            $"Iterate={iterativeCalculation}",
            $"Count={FormatNullableInt(maxCalculationIterations)}",
            $"Delta={(maxCalculationChange is { } value ? FormatDouble(value) : "null")}"
        ]);

    private static string CreateSheetCalculationFingerprint(
        int sheetIndex,
        string sheetName,
        bool fullCalculationOnLoad) =>
        $"{sheetIndex}:{sheetName}|SheetCalculation|Full={fullCalculationOnLoad}";

    private static string CreateSheetCodeNameFingerprint(int sheetIndex, string sheetName, string codeName) =>
        $"{sheetIndex}:{sheetName}|CodeName={codeName}";

    private static string CreatePrintOptionsFingerprint(int sheetIndex, string sheetName, string gridLinesSet) =>
        $"{sheetIndex}:{sheetName}|PrintOptions|GridLinesSet={gridLinesSet}";

    private static string CreateLegacyPrintSizeFingerprint(int sheetIndex, string sheetName, int printSize) =>
        $"{sheetIndex}:{sheetName}|LegacyPrintSize={FormatNullableInt(printSize)}";

    private static string? CreatePrimaryViewMetadataFingerprint(
        int sheetIndex,
        string sheetName,
        string? tabSelected,
        string? defaultGridColor,
        string? colorId,
        string? selection)
    {
        if (tabSelected is null && defaultGridColor is null && colorId is null && selection is null)
            return null;

        return string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            "PrimaryView",
            $"TabSelected={tabSelected ?? "null"}",
            $"DefaultGridColor={defaultGridColor ?? "null"}",
            $"ColorId={colorId ?? "null"}",
            $"Selection={selection ?? "null"}"
        ]);
    }

    private static string? ReadHssfWorkbookCodeName(HSSFWorkbook sourceWorkbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(UnknownRecord.CODENAME_1BA) is not UnknownRecord codeNameRecord ||
            UnknownRecordRawDataField?.GetValue(codeNameRecord) is not byte[] rawData)
        {
            return null;
        }

        return DecodeBiffCodeName(rawData);
    }

    private static string? ReadHssfSheetCodeName(ISheet sourceSheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(UnknownRecord.CODENAME_1BA) is not UnknownRecord codeNameRecord ||
            UnknownRecordRawDataField?.GetValue(codeNameRecord) is not byte[] rawData)
        {
            return null;
        }

        return DecodeBiffCodeName(rawData);
    }

    private static string? DecodeBiffCodeName(byte[] rawData)
    {
        if (rawData.Length < 3)
            return null;

        var characterCount = rawData[0] | (rawData[1] << 8);
        var optionFlags = rawData[2];
        var isWide = (optionFlags & 0x01) != 0;
        var byteCount = isWide ? characterCount * 2 : characterCount;
        if (byteCount <= 0 || rawData.Length < 3 + byteCount)
            return null;

        var codeName = isWide
            ? Encoding.Unicode.GetString(rawData, 3, byteCount)
            : Encoding.Latin1.GetString(rawData, 3, byteCount);
        return string.IsNullOrWhiteSpace(codeName) ? null : codeName;
    }

    private static string NormalizeSourceSheetVisibility(SheetVisibility visibility) =>
        visibility switch
        {
            SheetVisibility.Hidden => "Hidden",
            SheetVisibility.VeryHidden => "VeryHidden",
            _ => "Visible"
        };

    private static string NormalizeExcelDataReaderVisibleState(string? visibleState) =>
        string.Equals(visibleState, "veryHidden", StringComparison.OrdinalIgnoreCase)
            ? "VeryHidden"
            : string.Equals(visibleState, "hidden", StringComparison.OrdinalIgnoreCase)
                ? "Hidden"
                : "Visible";

    private static string CreateMergeFingerprint(int sheetIndex, string sheetName, GridRange range) =>
        $"{sheetIndex}:{sheetName}|Merge|{range.Start.ToA1()}:{range.End.ToA1()}";

    private static string CreateMergeFingerprint(int sheetIndex, string sheetName, CellRangeAddressBase range) =>
        $"{sheetIndex}:{sheetName}|Merge|{CreateRangeToken(range)}";

    private static string CreateMergeFingerprint(int sheetIndex, string sheetName, ExcelDataReader.CellRange range) =>
        $"{sheetIndex}:{sheetName}|Merge|{new ModelCellAddress(default, ToSourceModelIndex(range.FromRow), ToSourceModelIndex(range.FromColumn)).ToA1()}:" +
        $"{new ModelCellAddress(default, ToSourceModelIndex(range.ToRow), ToSourceModelIndex(range.ToColumn)).ToA1()}";

    private static string CreateDimensionFingerprint(int sheetIndex, string sheetName, string kind, uint index, string value) =>
        $"{sheetIndex}:{sheetName}|Dimension|{kind}{index}={value}";

    private static string CreateDefaultDimensionFingerprint(
        int sheetIndex,
        string sheetName,
        double defaultColumnWidth,
        double defaultRowHeight) =>
        $"{sheetIndex}:{sheetName}|Dimension|DefaultColWidth={FormatDouble(defaultColumnWidth)}|DefaultRowHeight={FormatDouble(defaultRowHeight)}";

    private static bool TryCreateExcelDataReaderStyleFingerprint(
        IExcelDataReader reader,
        int sheetIndex,
        string sheetName,
        int column,
        out string fingerprint)
    {
        var sourceStyle = reader.GetCellStyle(column);
        var sourceNumberFormat = reader.GetNumberFormatString(column);
        var numberFormat = string.IsNullOrWhiteSpace(sourceNumberFormat)
            ? ModelCellStyle.Default.NumberFormat
            : sourceNumberFormat;
        var horizontalAlignment = MapExcelDataReaderHorizontalAlignment(sourceStyle.HorizontalAlignment);
        var verticalAlignment = MapExcelDataReaderVerticalAlignment(sourceStyle.VerticalAlignment);

        if (string.Equals(numberFormat, ModelCellStyle.Default.NumberFormat, StringComparison.Ordinal) &&
            horizontalAlignment == ModelCellStyle.Default.HorizontalAlignment &&
            verticalAlignment == ModelCellStyle.Default.VerticalAlignment &&
            sourceStyle.IndentLevel == ModelCellStyle.Default.IndentLevel &&
            sourceStyle.Locked == ModelCellStyle.Default.Locked &&
            sourceStyle.Hidden == ModelCellStyle.Default.Hidden)
        {
            fingerprint = "";
            return false;
        }

        fingerprint = CreateFallbackStyleFingerprint(
            sheetIndex,
            sheetName,
            (uint)reader.Depth + 1,
            (uint)column + 1,
            numberFormat,
            horizontalAlignment,
            verticalAlignment,
            sourceStyle.IndentLevel,
            sourceStyle.Locked,
            sourceStyle.Hidden);
        return true;
    }

    private static string CreateFallbackStyleFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string numberFormat,
        ModelHorizontalAlignment horizontalAlignment,
        ModelVerticalAlignment verticalAlignment,
        int indentLevel,
        bool locked,
        bool hidden) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|Style|Fmt={EscapeToken(numberFormat)}|H={horizontalAlignment}|V={verticalAlignment}|Indent={indentLevel}|Locked={locked}|Hidden={hidden}";

    private static string CreateSourceRichStyleFingerprint(
        HSSFWorkbook hssf,
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        ICellStyle sourceStyle)
    {
        var style = new ModelCellStyle
        {
            NumberFormat = sourceStyle.GetDataFormatString(),
            HorizontalAlignment = MapSourceHorizontalAlignment(sourceStyle.Alignment),
            VerticalAlignment = MapSourceVerticalAlignment(sourceStyle.VerticalAlignment),
            WrapText = sourceStyle.WrapText,
            ShrinkToFit = sourceStyle.ShrinkToFit,
            IndentLevel = sourceStyle.Indention,
            TextRotation = MapSourceTextRotation(sourceStyle.Rotation),
            Locked = sourceStyle.IsLocked,
            Hidden = sourceStyle.IsHidden,
            FillPatternStyle = MapSourceFillPattern(sourceStyle.FillPattern),
            BorderTop = new CellBorder(MapSourceBorderStyle(sourceStyle.BorderTop), GetSourceIndexedColor(hssf, sourceStyle.TopBorderColor)),
            BorderRight = new CellBorder(MapSourceBorderStyle(sourceStyle.BorderRight), GetSourceIndexedColor(hssf, sourceStyle.RightBorderColor)),
            BorderBottom = new CellBorder(MapSourceBorderStyle(sourceStyle.BorderBottom), GetSourceIndexedColor(hssf, sourceStyle.BottomBorderColor)),
            BorderLeft = new CellBorder(MapSourceBorderStyle(sourceStyle.BorderLeft), GetSourceIndexedColor(hssf, sourceStyle.LeftBorderColor))
        };

        if (sourceStyle.FillForegroundColor != 0)
            style.FillColor = GetSourceIndexedColor(hssf, sourceStyle.FillForegroundColor);

        var font = hssf.GetFontAt(sourceStyle.FontIndex);
        if (font is not null)
        {
            style.FontName = string.IsNullOrWhiteSpace(font.FontName) ? style.FontName : font.FontName;
            if (font.FontHeightInPoints > 0)
                style.FontSize = font.FontHeightInPoints;
            style.Bold = font.IsBold;
            style.Italic = font.IsItalic;
            style.Strikethrough = font.IsStrikeout;
            style.Underline = font.Underline != FontUnderlineType.None;
            style.FontColor = GetSourceIndexedColor(hssf, font.Color);
        }

        return CreateRichStyleFingerprint(sheetIndex, sheetName, row, column, style);
    }

    private static string CreateRichStyleFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        ModelCellStyle style) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}",
            $"Style=Rich",
            $"Fmt={EscapeToken(style.NumberFormat)}",
            $"Align={style.HorizontalAlignment},{style.VerticalAlignment}",
            $"Text={style.WrapText},{style.ShrinkToFit},{style.IndentLevel},{style.TextRotation}",
            $"Protection={style.Locked},{style.Hidden}",
            $"Font={EscapeToken(style.FontName)},{FormatDouble(style.FontSize)},{style.Bold},{style.Italic},{style.Strikethrough},{style.Underline},{FormatColor(style.FontColor)}",
            $"Fill={FormatColor(style.FillColor)},{style.FillPatternStyle},{FormatColor(style.FillPatternColor)}",
            $"Borders=T:{FormatBorder(style.BorderTop)},R:{FormatBorder(style.BorderRight)},B:{FormatBorder(style.BorderBottom)},L:{FormatBorder(style.BorderLeft)}"
        ]);

    private static bool TryCreateExcelDataReaderHeaderFooterFingerprint(
        IExcelDataReader reader,
        int sheetIndex,
        string sheetName,
        out string fingerprint)
    {
        if (reader.HeaderFooter is not { } headerFooter)
        {
            fingerprint = "";
            return false;
        }

        return TryCreateHeaderFooterFingerprint(
            sheetIndex,
            sheetName,
            ParseHeaderFooterRawText(headerFooter.OddHeader),
            ParseHeaderFooterRawText(headerFooter.OddFooter),
            out fingerprint);
    }

    private static bool TryCreateHeaderFooterFingerprint(
        int sheetIndex,
        string sheetName,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        out string fingerprint)
    {
        if (header == new WorksheetHeaderFooter("", "", "") &&
            footer == new WorksheetHeaderFooter("", "", ""))
        {
            fingerprint = "";
            return false;
        }

        fingerprint = $"{sheetIndex}:{sheetName}|Header={FormatHeaderFooter(header)}|Footer={FormatHeaderFooter(footer)}";
        return true;
    }

    private static string CreateAddressedFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string value) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|{value}";

    private static string CreatePictureFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string? contentType,
        byte[]? imageBytes) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|Picture|Type={NormalizePictureContentType(contentType)}|Bytes={imageBytes?.Length ?? 0}|Hash={HashPictureBytes(imageBytes)}";

    private static string CreateTextBoxFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        string? text,
        CellColor? fillColor)
    {
        var normalizedText = text ?? "";
        return $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|TextBox|Len={normalizedText.Length}|Hash={HashText(normalizedText)}|Fill={FormatColor(fillColor)}";
    }

    private static string CreateDrawingShapeFingerprint(
        int sheetIndex,
        string sheetName,
        uint row,
        uint column,
        DrawingShapeKind kind,
        bool hasFill,
        CellColor? fillColor,
        CellColor? outlineColor) =>
        $"{sheetIndex}:{sheetName}!{new ModelCellAddress(default, row, column).ToA1()}|Shape|Kind={kind}|Fill={hasFill},{FormatColor(fillColor)}|Outline={FormatColor(outlineColor)}";

    private static string CreateFormControlFingerprint(
        int sheetIndex,
        string sheetName,
        GridRange anchor,
        FormControlKind kind,
        string? name,
        string? listFillRange,
        int? selectedIndex) =>
        $"{sheetIndex}:{sheetName}!{CreateRangeToken(anchor)}|FormControl|Kind={kind}|Name={EscapeToken(name ?? "")}|List={EscapeToken(listFillRange ?? "")}|Selected={selectedIndex?.ToString(CultureInfo.InvariantCulture) ?? ""}";

    private static string NormalizePictureContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;

    private static string HashPictureBytes(byte[]? imageBytes) =>
        imageBytes is { Length: > 0 }
            ? Convert.ToHexString(SHA256.HashData(imageBytes))[..16]
            : "";

    private static string HashText(string text) =>
        text.Length > 0
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16]
            : "";

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

    private static string CreateOutlineSettingFingerprint(
        int sheetIndex,
        string sheetName,
        bool? summaryBelow,
        bool? summaryRight,
        bool? showOutlineSymbols) =>
        $"{sheetIndex}:{sheetName}|OutlineSettings|Below={FormatNullableBool(summaryBelow)}|Right={FormatNullableBool(summaryRight)}|Symbols={FormatNullableBool(showOutlineSymbols)}";

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

    private static string CreateDefinedNameFingerprint(
        string name,
        string kind,
        string target,
        string scope,
        string comment) =>
        $"DefinedName|Name={EscapeToken(name)}|Kind={kind}|Target={EscapeToken(target)}|Scope={EscapeToken(scope)}|Comment={EscapeToken(comment)}";

    private static string CreateSheetProtectionFingerprint(
        int sheetIndex,
        string sheetName,
        string? passwordHash,
        bool objectProtected,
        bool scenarioProtected) =>
        $"{sheetIndex}:{sheetName}|Protection|Password={passwordHash ?? ""}|Objects={objectProtected}|Scenarios={scenarioProtected}";

    private static string CreateDataValidationFingerprint(int sheetIndex, string sheetName, DataValidation validation) =>
        CreateDataValidationFingerprint(
            sheetIndex,
            sheetName,
            [CreateRangeToken(validation.AppliesTo), .. validation.AdditionalRanges.Select(CreateRangeToken)],
            validation.Type,
            validation.Operator,
            validation.Formula1,
            validation.Formula2,
            validation.AllowBlank,
            validation.ShowDropdown,
            validation.AlertStyle,
            validation.ShowInputMessage,
            validation.ShowErrorMessage,
            validation.PromptTitle,
            validation.PromptMessage,
            validation.ErrorTitle,
            validation.ErrorMessage);

    private static string CreateDataValidationFingerprint(
        int sheetIndex,
        string sheetName,
        IEnumerable<string> ranges,
        DvType type,
        DvOperator op,
        string? formula1,
        string? formula2,
        bool allowBlank,
        bool showDropdown,
        DvAlertStyle alertStyle,
        bool showInputMessage,
        bool showErrorMessage,
        string? promptTitle,
        string? promptMessage,
        string? errorTitle,
        string? errorMessage) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            $"Validation={string.Join(",", ranges.Select(EscapeToken))}",
            $"Type={type}",
            $"Operator={op}",
            $"Formula={EscapeToken(formula1 ?? "")},{EscapeToken(formula2 ?? "")}",
            $"Flags={allowBlank},{showDropdown},{alertStyle},{showInputMessage},{showErrorMessage}",
            $"Prompt={EscapeToken(promptTitle ?? "")},{EscapeToken(promptMessage ?? "")}",
            $"Error={EscapeToken(errorTitle ?? "")},{EscapeToken(errorMessage ?? "")}"
        ]);

    private static string CreateConditionalFormatFingerprint(int sheetIndex, string sheetName, ConditionalFormat format) =>
        CreateConditionalFormatFingerprint(
            sheetIndex,
            sheetName,
            CreateRangeToken(format.AppliesTo),
            format.RuleType,
            format.Operator,
            format.Value1,
            format.Value2,
            format.FormulaText,
            Math.Max(1, format.Priority),
            format.StopIfTrue,
            CreateStyleFingerprint(format.FormatIfTrue));

    private static string CreateConditionalFormatFingerprint(
        int sheetIndex,
        string sheetName,
        string range,
        CfRuleType type,
        CfOperator op,
        string? value1,
        string? value2,
        string? formula,
        int priority,
        bool stopIfTrue,
        string style) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            $"ConditionalFormat={EscapeToken(range)}",
            $"Type={type}",
            $"Operator={op}",
            $"Values={EscapeToken(value1 ?? "")},{EscapeToken(value2 ?? "")}",
            $"Formula={EscapeToken(formula ?? "")}",
            $"Priority={priority}",
            $"Stop={stopIfTrue}",
            style
        ]);

    private static string CreateStyleFingerprint(ModelCellStyle? style)
    {
        if (style is null)
            return "Style=null";

        return string.Join(";", [
            $"Font={style.Bold},{style.Italic},{style.Underline},{FormatColor(style.FontColor)}",
            $"Fill={FormatColor(style.FillColor)},{style.FillPatternStyle},{FormatColor(style.FillPatternColor)}",
            $"Borders=T:{FormatBorder(style.BorderTop)},R:{FormatBorder(style.BorderRight)},B:{FormatBorder(style.BorderBottom)},L:{FormatBorder(style.BorderLeft)}"
        ]);
    }

    private static string CreateSourceConditionalFormatStyleFingerprint(
        HSSFWorkbook hssf,
        IConditionalFormattingRule rule)
    {
        var hasStyle = false;
        var style = new ModelCellStyle();
        if (rule.FontFormatting is { } font)
        {
            hasStyle = true;
            style.Bold = font.IsBold;
            style.Italic = font.IsItalic;
            style.Underline = font.UnderlineType != FontUnderlineType.None;
            if (font.FontColorIndex != 0)
                style.FontColor = GetSourceIndexedColor(hssf, font.FontColorIndex);
        }

        if (rule.PatternFormatting is { } pattern)
        {
            hasStyle = true;
            style.FillPatternStyle = MapSourceFillPattern(pattern.FillPattern);
            if (pattern.FillForegroundColor != 0)
                style.FillColor = GetSourceIndexedColor(hssf, pattern.FillForegroundColor);
            if (pattern.FillBackgroundColor != 0)
                style.FillPatternColor = GetSourceIndexedColor(hssf, pattern.FillBackgroundColor);
        }

        if (rule.BorderFormatting is { } border)
        {
            hasStyle = true;
            style.BorderTop = new CellBorder(MapSourceBorderStyle(border.BorderTop), GetSourceIndexedColor(hssf, border.TopBorderColor));
            style.BorderRight = new CellBorder(MapSourceBorderStyle(border.BorderRight), GetSourceIndexedColor(hssf, border.RightBorderColor));
            style.BorderBottom = new CellBorder(MapSourceBorderStyle(border.BorderBottom), GetSourceIndexedColor(hssf, border.BottomBorderColor));
            style.BorderLeft = new CellBorder(MapSourceBorderStyle(border.BorderLeft), GetSourceIndexedColor(hssf, border.LeftBorderColor));
        }

        return hasStyle ? CreateStyleFingerprint(style) : "Style=null";
    }

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
        WorksheetViewMode viewMode,
        int zoomPercent,
        bool showGridlines,
        bool showHeadings,
        bool showFormulas,
        bool showZeros,
        uint? viewTopRow,
        uint? viewLeftCol,
        uint? activeRow,
        uint? activeColumn,
        uint? splitRow,
        uint? splitColumn,
        CellColor? tabColor) =>
        string.Join("|", [
            $"{sheetIndex}:{sheetName}",
            $"View={viewMode},{zoomPercent}",
            $"Display={showGridlines},{showHeadings},{showFormulas},{showZeros}",
            $"TopLeft={FormatNullableUInt(viewTopRow)},{FormatNullableUInt(viewLeftCol)}",
            $"Active={FormatNullableUInt(activeRow)},{FormatNullableUInt(activeColumn)}",
            $"Split={FormatNullableUInt(splitRow)},{FormatNullableUInt(splitColumn)}",
            $"TabColor={FormatColor(tabColor)}"
        ]);

    private static string FormatDouble(double value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static double PointsToPixels(double points) =>
        Math.Round(points * (96.0 / 72.0), MidpointRounding.AwayFromZero);

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

    private static WorksheetViewMode GetSourceViewMode(ISheet sheet) =>
        TryGetWindowTwoRecord(sheet) is { SavedInPageBreakPreview: true }
            ? WorksheetViewMode.PageBreakPreview
            : WorksheetViewMode.Normal;

    private static int GetSourceZoomPercent(ISheet sheet)
    {
        if (TryGetWindowTwoRecord(sheet) is { } window &&
            GetValidWindowZoom(window) is { } windowZoom)
        {
            return windowZoom;
        }

        return GetValidScaleZoom(sheet) ?? 100;
    }

    private static int? GetValidWindowZoom(WindowTwoRecord window)
    {
        var zoom = window.SavedInPageBreakPreview && window.PageBreakZoom > 0
            ? window.PageBreakZoom
            : window.NormalZoom;
        return zoom is >= 10 and <= 400 ? zoom : null;
    }

    private static int? GetValidScaleZoom(ISheet sheet)
    {
        if (sheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(SCLRecord.sid) is not SCLRecord scale ||
            scale.Denominator <= 0)
        {
            return null;
        }

        var zoom = (int)Math.Round(scale.Numerator * 100d / scale.Denominator, MidpointRounding.AwayFromZero);
        return zoom is >= 10 and <= 400 ? zoom : null;
    }

    private static WindowTwoRecord? TryGetWindowTwoRecord(ISheet sheet) =>
        sheet is HSSFSheet hssfSheet
            ? hssfSheet.Sheet.FindFirstRecordBySid(WindowTwoRecord.sid) as WindowTwoRecord
            : null;

    private static SelectionRecord? TryGetSelectionRecord(ISheet sheet) =>
        sheet is HSSFSheet hssfSheet
            ? hssfSheet.Sheet.FindFirstRecordBySid(SelectionRecord.sid) as SelectionRecord
            : null;

    private static bool SourceHasVbaProjectPackage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var poifs = new POIFSFileSystem(POIFSFileSystem.CreateNonClosingInputStream(stream));
            return DirectoryContainsVbaProject(poifs.Root);
        }
        catch
        {
            return false;
        }
    }

    private static bool DirectoryContainsVbaProject(DirectoryEntry directory)
    {
        var entries = directory.Entries;
        while (entries.MoveNext())
        {
            var entry = entries.Current;
            if (string.Equals(entry.Name, "_VBA_PROJECT_CUR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, "VBA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry is DirectoryEntry child && DirectoryContainsVbaProject(child))
                return true;
        }

        return false;
    }

    private static string FormatBorder(CellBorder border) =>
        $"{border.Style},{FormatColor(border.Color)}";

    private static string EscapeToken(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\p", StringComparison.Ordinal)
            .Replace(",", "\\c", StringComparison.Ordinal);

    private static string FormatBreaks(IEnumerable<uint> breaks) =>
        string.Join(",", breaks.Order());

    private static string CreateRangeToken(GridRange range) =>
        $"{range.Start.ToA1()}:{range.End.ToA1()}";

    private static string CreateRangeToken(CellRangeAddressBase range) =>
        $"{new ModelCellAddress(default, ToSourceModelIndex(range.FirstRow), ToSourceModelIndex(range.FirstColumn)).ToA1()}:" +
        $"{new ModelCellAddress(default, ToSourceModelIndex(range.LastRow), ToSourceModelIndex(range.LastColumn)).ToA1()}";

    private static string CreateSqrefToken(IEnumerable<CellRangeAddressBase> ranges) =>
        string.Join(" ", ranges.Select(CreateSqrefRangeToken));

    private static string CreateSqrefRangeToken(CellRangeAddressBase range)
    {
        var first = ToSourceA1(range.FirstRow, range.FirstColumn);
        var last = ToSourceA1(range.LastRow, range.LastColumn);
        return string.Equals(first, last, StringComparison.Ordinal) ? first : $"{first}:{last}";
    }

    private static string ToSourceA1(int rowIndex, int columnIndex) =>
        new ModelCellAddress(default, ToSourceModelIndex(rowIndex), ToSourceModelIndex(columnIndex)).ToA1();

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

    private static DvType MapSourceDataValidationType(int validationType) =>
        validationType switch
        {
            ValidationType.INTEGER => DvType.WholeNumber,
            ValidationType.DECIMAL => DvType.Decimal,
            ValidationType.LIST => DvType.List,
            ValidationType.DATE => DvType.Date,
            ValidationType.TIME => DvType.Time,
            ValidationType.TEXT_LENGTH => DvType.TextLength,
            ValidationType.FORMULA => DvType.Custom,
            _ => DvType.Any
        };

    private static DvOperator MapSourceDataValidationOperator(int operatorType) =>
        operatorType switch
        {
            OperatorType.NOT_BETWEEN => DvOperator.NotBetween,
            OperatorType.EQUAL => DvOperator.Equal,
            OperatorType.NOT_EQUAL => DvOperator.NotEqual,
            OperatorType.GREATER_THAN => DvOperator.GreaterThan,
            OperatorType.LESS_THAN => DvOperator.LessThan,
            OperatorType.GREATER_OR_EQUAL => DvOperator.GreaterThanOrEqual,
            OperatorType.LESS_OR_EQUAL => DvOperator.LessThanOrEqual,
            _ => DvOperator.Between
        };

    private static DvAlertStyle MapSourceDataValidationAlertStyle(int errorStyle) =>
        errorStyle switch
        {
            ERRORSTYLE.WARNING => DvAlertStyle.Warning,
            ERRORSTYLE.INFO => DvAlertStyle.Information,
            _ => DvAlertStyle.Stop
        };

    private static CfOperator MapSourceConditionalFormatOperator(ComparisonOperator op) =>
        op switch
        {
            ComparisonOperator.NotBetween => CfOperator.NotBetween,
            ComparisonOperator.Equal => CfOperator.Equal,
            ComparisonOperator.NotEqual => CfOperator.NotEqual,
            ComparisonOperator.GreaterThan => CfOperator.GreaterThan,
            ComparisonOperator.LessThan => CfOperator.LessThan,
            ComparisonOperator.GreaterThanOrEqual => CfOperator.GreaterThanOrEqual,
            ComparisonOperator.LessThanOrEqual => CfOperator.LessThanOrEqual,
            _ => CfOperator.Between
        };

    private static ModelBorderStyle MapSourceBorderStyle(NPOIBorderStyle borderStyle) =>
        borderStyle switch
        {
            NPOIBorderStyle.Thin => ModelBorderStyle.Thin,
            NPOIBorderStyle.Medium => ModelBorderStyle.Medium,
            NPOIBorderStyle.Thick => ModelBorderStyle.Thick,
            NPOIBorderStyle.Dashed => ModelBorderStyle.Dashed,
            NPOIBorderStyle.Dotted => ModelBorderStyle.Dotted,
            NPOIBorderStyle.Double => ModelBorderStyle.Double,
            _ => ModelBorderStyle.None
        };

    private static ModelHorizontalAlignment MapSourceHorizontalAlignment(NPOIHorizontalAlignment alignment) =>
        alignment switch
        {
            NPOIHorizontalAlignment.Left => ModelHorizontalAlignment.Left,
            NPOIHorizontalAlignment.Center => ModelHorizontalAlignment.Center,
            NPOIHorizontalAlignment.Right => ModelHorizontalAlignment.Right,
            NPOIHorizontalAlignment.Justify => ModelHorizontalAlignment.Justify,
            NPOIHorizontalAlignment.Distributed => ModelHorizontalAlignment.Distributed,
            _ => ModelHorizontalAlignment.General
        };

    private static ModelVerticalAlignment MapSourceVerticalAlignment(NPOIVerticalAlignment alignment) =>
        alignment switch
        {
            NPOIVerticalAlignment.Top => ModelVerticalAlignment.Top,
            NPOIVerticalAlignment.Center => ModelVerticalAlignment.Center,
            NPOIVerticalAlignment.Justify => ModelVerticalAlignment.Justify,
            NPOIVerticalAlignment.Distributed => ModelVerticalAlignment.Distributed,
            _ => ModelVerticalAlignment.Bottom
        };

    private static int MapSourceTextRotation(short rotation) =>
        rotation switch
        {
            255 => 255,
            > 90 => 90 - rotation,
            _ => rotation
        };

    private static ModelHorizontalAlignment MapExcelDataReaderHorizontalAlignment(ExcelDataReader.HorizontalAlignment alignment) =>
        alignment switch
        {
            ExcelDataReader.HorizontalAlignment.Left => ModelHorizontalAlignment.Left,
            ExcelDataReader.HorizontalAlignment.Center or ExcelDataReader.HorizontalAlignment.Centered or ExcelDataReader.HorizontalAlignment.CenteredAcrossSelection => ModelHorizontalAlignment.Center,
            ExcelDataReader.HorizontalAlignment.Right => ModelHorizontalAlignment.Right,
            ExcelDataReader.HorizontalAlignment.Justified => ModelHorizontalAlignment.Justify,
            ExcelDataReader.HorizontalAlignment.Distributed => ModelHorizontalAlignment.Distributed,
            _ => ModelHorizontalAlignment.General
        };

    private static ModelVerticalAlignment MapExcelDataReaderVerticalAlignment(ExcelDataReader.VerticalAlignment alignment) =>
        alignment switch
        {
            ExcelDataReader.VerticalAlignment.Top => ModelVerticalAlignment.Top,
            ExcelDataReader.VerticalAlignment.Center => ModelVerticalAlignment.Center,
            ExcelDataReader.VerticalAlignment.Justify => ModelVerticalAlignment.Justify,
            ExcelDataReader.VerticalAlignment.Distributed => ModelVerticalAlignment.Distributed,
            _ => ModelVerticalAlignment.Bottom
        };

    private static CellFillPatternStyle MapSourceFillPattern(FillPattern fillPattern) =>
        fillPattern switch
        {
            FillPattern.SolidForeground => CellFillPatternStyle.Solid,
            FillPattern.FineDots => CellFillPatternStyle.Gray125,
            FillPattern.AltBars => CellFillPatternStyle.DarkHorizontal,
            FillPattern.SparseDots => CellFillPatternStyle.Gray0625,
            FillPattern.ThickHorizontalBands => CellFillPatternStyle.DarkHorizontal,
            FillPattern.ThickVerticalBands => CellFillPatternStyle.DarkVertical,
            FillPattern.ThickBackwardDiagonals => CellFillPatternStyle.DarkUp,
            FillPattern.ThickForwardDiagonals => CellFillPatternStyle.DarkDown,
            FillPattern.BigSpots => CellFillPatternStyle.LightGray,
            FillPattern.Bricks => CellFillPatternStyle.LightTrellis,
            FillPattern.ThinHorizontalBands => CellFillPatternStyle.LightHorizontal,
            FillPattern.ThinVerticalBands => CellFillPatternStyle.LightVertical,
            FillPattern.ThinBackwardDiagonals => CellFillPatternStyle.LightUp,
            FillPattern.ThinForwardDiagonals => CellFillPatternStyle.LightDown,
            FillPattern.Squares => CellFillPatternStyle.LightGrid,
            FillPattern.Diamonds => CellFillPatternStyle.LightTrellis,
            _ => CellFillPatternStyle.None
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

    private static bool TryGetSourceHssfRgbColor(int value, out CellColor color)
    {
        color = default;
        if (value < 0 || value > 0xFFFFFF)
            return false;

        color = new CellColor(
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF));
        return true;
    }

    private static DrawingShapeKind? MapSourceHssfShapeKind(int shapeType) =>
        shapeType switch
        {
            HSSFSimpleShape.OBJECT_TYPE_RECTANGLE => DrawingShapeKind.Rectangle,
            HSSFSimpleShape.OBJECT_TYPE_OVAL => DrawingShapeKind.Ellipse,
            HSSFSimpleShape.OBJECT_TYPE_LINE => DrawingShapeKind.Line,
            _ => null
        };

    private static FormControlKind? MapSourceHssfFormControlKind(int shapeType) =>
        shapeType switch
        {
            HSSFSimpleShape.OBJECT_TYPE_COMBO_BOX => FormControlKind.DropDown,
            _ => null
        };

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static CellColor GetSourceIndexedColor(HSSFWorkbook hssf, short colorIndex)
    {
        var color = hssf.GetCustomPalette().GetColor(colorIndex);
        var triplet = color?.GetTriplet();
        return triplet is { Length: >= 3 }
            ? new CellColor(triplet[0], triplet[1], triplet[2])
            : CellColor.Black;
    }

    private static string? GetProtectionMetadataAttribute(Sheet sheet, string name)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(sheet.ProtectionMetadata?.Get("sheetProtection"));
        return attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static string? GetPrintOptionsMetadataAttribute(Sheet sheet, string name)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(sheet.PrintOptionsMetadata?.Get("printOptions"));
        return attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static string? GetPrimaryViewMetadataAttribute(Sheet sheet, string name)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(sheet.PrimaryViewMetadata?.Get("sheetView"));
        return attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static string? GetPrimaryViewSelectionToken(Sheet sheet)
    {
        var (_, children) = XmlNativeBagSerializer.Deserialize(sheet.PrimaryViewMetadata?.Get("sheetView"));
        foreach (var child in children)
        {
            if (string.IsNullOrWhiteSpace(child))
                continue;

            try
            {
                var element = XElement.Parse(child);
                if (!string.Equals(element.Name.LocalName, "selection", StringComparison.Ordinal))
                    continue;

                var activeCell = element.Attribute("activeCell")?.Value;
                var sqref = element.Attribute("sqref")?.Value;
                if (string.IsNullOrWhiteSpace(activeCell) || string.IsNullOrWhiteSpace(sqref))
                    continue;

                return $"{activeCell},{sqref},{element.Attribute("activeCellId")?.Value ?? "null"}";
            }
            catch
            {
                // Ignore malformed native child XML in test summaries.
            }
        }

        return null;
    }

    private static string? GetWorkbookProtectionMetadataAttribute(Workbook workbook, string name)
    {
        var (attributes, _) = XmlNativeBagSerializer.Deserialize(workbook.ProtectionMetadata?.Get("workbookProtection"));
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

    private static ScalarValue MapExcelDataReaderValue(object? value) =>
        value switch
        {
            null => BlankValue.Instance,
            double number => new NumberValue(number),
            float number => new NumberValue(number),
            long number => new NumberValue(number),
            int number => new NumberValue(number),
            short number => new NumberValue(number),
            byte number => new NumberValue(number),
            sbyte number => new NumberValue(number),
            uint number => new NumberValue(number),
            ushort number => new NumberValue(number),
            ulong number => new NumberValue(number),
            decimal number => new NumberValue((double)number),
            bool boolean => new BoolValue(boolean),
            DateTime date => DateTimeValue.FromDateTime(date),
            TimeSpan time => new DateTimeValue(time.TotalDays),
            string text when text.Length == 0 => BlankValue.Instance,
            string text => new TextValue(text),
            _ => new TextValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
        };

    private static ScalarValue MapExcelDataReaderCellValue(IExcelDataReader reader, int column) =>
        reader.GetCellError(column) is { } error
            ? MapExcelDataReaderErrorValue(error)
            : MapExcelDataReaderValue(reader.GetValue(column));

    private static ErrorValue MapExcelDataReaderErrorValue(ExcelDataReader.CellError error) =>
        error switch
        {
            ExcelDataReader.CellError.NULL => ErrorValue.Null,
            ExcelDataReader.CellError.DIV0 => ErrorValue.DivByZero,
            ExcelDataReader.CellError.VALUE => ErrorValue.Value,
            ExcelDataReader.CellError.REF => ErrorValue.Ref,
            ExcelDataReader.CellError.NAME => ErrorValue.Name,
            ExcelDataReader.CellError.NUM => ErrorValue.Num,
            ExcelDataReader.CellError.NA => ErrorValue.NA,
            ExcelDataReader.CellError.GETTING_DATA => new ErrorValue("#GETTING_DATA"),
            _ => new ErrorValue(error.ToString())
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

    private static string GetSourceDefinedNameScope(HSSFWorkbook hssf, IName definedName)
    {
        var sheetIndex = definedName.SheetIndex;
        return sheetIndex >= 0 && sheetIndex < hssf.NumberOfSheets
            ? hssf.GetSheetName(sheetIndex)
            : NamedRangeMetadata.WorkbookScope.Scope;
    }

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
        int Pictures,
        int TextBoxes,
        int DrawingShapes,
        int FormControls,
        int FreezePanes,
        int RowOutlineLevels,
        int ColOutlineLevels,
        int PrintAreas,
        int PrintTitleRows,
        int PrintTitleColumns,
        int AutoFilters,
        int ProtectedSheets,
        int DataValidations,
        int ConditionalFormats,
        int PageSetupSheets,
        int PageBreaks,
        int? ActiveSheetIndex,
        bool Uses1904DateSystem,
        bool RichMetadata = true,
        bool HasVbaProjectPackage = false,
        IReadOnlyList<string>? SheetNames = null,
        IReadOnlyList<string>? SheetKindFingerprints = null,
        IReadOnlyList<string>? SheetVisibilityFingerprints = null,
        string? WorkbookCodeName = null,
        IReadOnlyList<string>? SheetCodeNameFingerprints = null,
        IReadOnlyList<string>? WorkbookCountryFingerprints = null,
        IReadOnlyList<string>? WorkbookLegacyMenuFingerprints = null,
        IReadOnlyList<string>? WorkbookLegacyWorkbookFingerprints = null,
        IReadOnlyList<string>? WorkbookFunctionGroupFingerprints = null,
        IReadOnlyList<string>? WorkbookPropertiesFingerprints = null,
        IReadOnlyList<string>? WorkbookViewFingerprints = null,
        IReadOnlyList<string>? WorkbookProtectionFingerprints = null,
        IReadOnlyList<string>? WorkbookFileSharingFingerprints = null,
        IReadOnlyList<string>? WorkbookCalculationFingerprints = null,
        IReadOnlyList<string>? SheetCalculationFingerprints = null,
        IReadOnlyList<string>? CellFingerprints = null,
        IReadOnlyList<string>? MergeFingerprints = null,
        IReadOnlyList<string>? DimensionFingerprints = null,
        IReadOnlyList<string>? DefaultDimensionFingerprints = null,
        IReadOnlyList<string>? StyleFingerprints = null,
        IReadOnlyList<string>? HeaderFooterFingerprints = null,
        IReadOnlyList<string>? DefinedNameFingerprints = null,
        IReadOnlyList<string>? HyperlinkFingerprints = null,
        IReadOnlyList<string>? CommentFingerprints = null,
        IReadOnlyList<string>? PictureFingerprints = null,
        IReadOnlyList<string>? TextBoxFingerprints = null,
        IReadOnlyList<string>? DrawingShapeFingerprints = null,
        IReadOnlyList<string>? FormControlFingerprints = null,
        IReadOnlyList<string>? PaneFingerprints = null,
        IReadOnlyList<string>? RowOutlineFingerprints = null,
        IReadOnlyList<string>? ColOutlineFingerprints = null,
        IReadOnlyList<string>? OutlineSettingFingerprints = null,
        IReadOnlyList<string>? PrintLayoutFingerprints = null,
        IReadOnlyList<string>? PrintOptionsFingerprints = null,
        IReadOnlyList<string>? SheetLegacyPrintSizeFingerprints = null,
        IReadOnlyList<string>? PrimaryViewMetadataFingerprints = null,
        IReadOnlyList<string>? PageSetupFingerprints = null,
        IReadOnlyList<string>? ViewStateFingerprints = null,
        IReadOnlyList<string>? AutoFilterFingerprints = null,
        IReadOnlyList<string>? SheetProtectionFingerprints = null,
        IReadOnlyList<string>? DataValidationFingerprints = null,
        IReadOnlyList<string>? ConditionalFormatFingerprints = null);

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
