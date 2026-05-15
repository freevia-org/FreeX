using System.Text;
using System.IO.Compression;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public class FileAdapterSmokeTests
{
    // ── Native JSON ───────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrip()
    {
        var workbook = new Workbook("Test");
        var s1 = workbook.AddSheet("Alpha");
        var s2 = workbook.AddSheet("Beta");
        s2.IsHidden = true;
        s2.TabColor = new CellColor(0, 176, 80);

        var a1 = new CellAddress(s1.Id, 1, 1);
        var a2 = new CellAddress(s1.Id, 2, 3);
        s1.SetCell(a1, new TextValue("foo"));
        s1.SetCell(a2, new TextValue("hello"));

        var b1 = new CellAddress(s2.Id, 1, 1);
        s2.SetFormula(b1, "A1+1");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.SheetCount.Should().Be(2);
        loaded.GetSheetAt(0).Name.Should().Be("Alpha");
        loaded.GetSheetAt(1).Name.Should().Be("Beta");

        // NativeJsonAdapter stores values via record.ToString() so cells survive as non-blank.
        var ls1 = loaded.GetSheetAt(0);
        ls1.GetValue(1, 1).Should().NotBeOfType<BlankValue>();
        ls1.GetValue(2, 3).Should().NotBeOfType<BlankValue>();

        var ls2 = loaded.GetSheetAt(1);
        ls2.IsHidden.Should().BeTrue();
        ls2.TabColor.Should().Be(new CellColor(0, 176, 80));
        ls2.GetCell(1, 1)!.FormulaText.Should().Be("A1+1");
    }

    // ── XLSX ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FileSavePlanner_TryResolveExistingPath_UsesAdapterForCurrentPath()
    {
        var adapter = new XlsxFileAdapter();

        var resolved = FileSavePlanner.TryResolveExistingPath(
            @"C:\work\book.xlsx",
            [adapter],
            out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Path.Should().Be(@"C:\work\book.xlsx");
        target.Adapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public void FileSavePlanner_TryResolveExistingPath_ReturnsFalseForUnknownExtension()
    {
        var resolved = FileSavePlanner.TryResolveExistingPath(
            @"C:\work\book.unsupported",
            [new XlsxFileAdapter()],
            out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartLayout()
    {
        var workbook = new Workbook("ChartLayoutTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            Title = "Revenue",
            XAxisTitle = "Amount",
            YAxisTitle = "Quarter",
            LegendPosition = ChartLegendPosition.Bottom,
            ShowLegend = false,
            Left = 12,
            Top = 34,
            Width = 500,
            Height = 240
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Bar);
        chart.DataRange.Start.ToA1().Should().Be("A1");
        chart.DataRange.End.ToA1().Should().Be("B2");
        chart.Title.Should().Be("Revenue");
        chart.XAxisTitle.Should().Be("Amount");
        chart.YAxisTitle.Should().Be("Quarter");
        chart.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        chart.ShowLegend.Should().BeFalse();
        chart.Left.Should().Be(12);
        chart.Top.Should().Be(34);
        chart.Width.Should().Be(500);
        chart.Height.Should().Be(240);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetViewMode()
    {
        var workbook = new Workbook("ViewModeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_SplitPanes()
    {
        var workbook = new Workbook("SplitPaneTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 8;
        sheet.SplitColumn = 4;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).SplitRow.Should().Be(8);
        loaded.GetSheetAt(0).SplitColumn.Should().Be(4);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_CustomViews()
    {
        var workbook = new Workbook("CustomViewTest");
        workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.PageLayout, 1, 0, null, 3)]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var view = loaded.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Review");
        var state = view.Sheets.Should().ContainSingle().Subject;
        state.SheetName.Should().Be("Sheet1");
        state.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        state.FrozenRows.Should().Be(1);
        state.SplitColumn.Should().Be(3);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PageLayoutSettings()
    {
        var workbook = new Workbook("PageLayoutTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 8, 4));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1);
        sheet.HeaderMargin = 0.35;
        sheet.FooterMargin = 0.45;
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Page &[Page]", "Right footer");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First header left", "First header center", "First header right");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 5;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Blank;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 2);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(5);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintArea.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 2, 2),
            new CellAddress(loadedSheet.Id, 8, 4)));
        loadedSheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        loadedSheet.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        loadedSheet.PageMargins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1));
        loadedSheet.HeaderMargin.Should().Be(0.35);
        loadedSheet.FooterMargin.Should().Be(0.45);
        loadedSheet.PrintGridlines.Should().BeTrue();
        loadedSheet.PrintHeadings.Should().BeTrue();
        loadedSheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        loadedSheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        loadedSheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Page &[Page]", "Right footer"));
        loadedSheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First header left", "First header center", "First header right"));
        loadedSheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        loadedSheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right"));
        loadedSheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        loadedSheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        loadedSheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        loadedSheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        loadedSheet.HeaderFooterAlignWithMargins.Should().BeFalse();
        loadedSheet.CenterHorizontallyOnPage.Should().BeTrue();
        loadedSheet.CenterVerticallyOnPage.Should().BeTrue();
        loadedSheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        loadedSheet.FirstPageNumber.Should().Be(5);
        loadedSheet.PrintBlackAndWhite.Should().BeTrue();
        loadedSheet.PrintDraftQuality.Should().BeTrue();
        loadedSheet.PrintQualityDpi.Should().Be(600);
        loadedSheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Blank);
        loadedSheet.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);
        loadedSheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 2));
        loadedSheet.RowPageBreaks.Should().Contain(20u);
        loadedSheet.ColumnPageBreaks.Should().Contain(5u);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Pictures()
    {
        var workbook = new Workbook("PictureTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 4),
            SourceRowCount = 2,
            SourceColumnCount = 2,
            Width = 160,
            Height = 40,
            Cells =
            {
                new PictureCellSnapshot(0, 0, "A"),
                new PictureCellSnapshot(1, 1, "D")
            }
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Row.Should().Be(3);
        picture.Anchor.Col.Should().Be(4);
        picture.SourceRowCount.Should().Be(2);
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "D");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ImagePicture()
    {
        var workbook = new Workbook("ImagePictureTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = [9, 8, 7],
            ContentType = "image/png",
            Width = 90,
            Height = 60,
            RotationDegrees = 30
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ImageBytes.Should().Equal(9, 8, 7);
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(90);
        picture.Height.Should().Be(60);
        picture.RotationDegrees.Should().Be(30);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_TextBoxesAndDrawingShapes()
    {
        var workbook = new Workbook("DrawingObjectTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Review note",
            Width = 220,
            Height = 120,
            RotationDegrees = 20,
            FillColor = new CellColor(240, 250, 255),
            OutlineColor = new CellColor(70, 80, 90)
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            RotationDegrees = 45,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50)
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var textBox = loaded.GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        textBox.Anchor.Row.Should().Be(2);
        textBox.Anchor.Col.Should().Be(2);
        textBox.Text.Should().Be("Review note");
        textBox.Width.Should().Be(220);
        textBox.Height.Should().Be(120);
        textBox.RotationDegrees.Should().Be(20);
        textBox.FillColor.Should().Be(new CellColor(240, 250, 255));
        textBox.OutlineColor.Should().Be(new CellColor(70, 80, 90));
        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Anchor.Row.Should().Be(4);
        shape.Anchor.Col.Should().Be(3);
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.Width.Should().Be(140);
        shape.Height.Should().Be(90);
        shape.RotationDegrees.Should().Be(45);
        shape.FillColor.Should().Be(new CellColor(200, 210, 220));
        shape.OutlineColor.Should().Be(new CellColor(30, 40, 50));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ValuesAndFormulas()
    {
        var workbook = new Workbook("XlsxTest");
        var sheet = workbook.AddSheet("Data");

        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 1, 2);
        var addr3 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(addr1, new NumberValue(3.14));
        sheet.SetCell(addr2, new TextValue("world"));
        var formulaCell = Cell.FromFormula("A1*2");
        formulaCell.Value = new NumberValue(6.28);
        sheet.SetCell(addr3, formulaCell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.SheetCount.Should().Be(1);
        var ls = loaded.GetSheetAt(0);
        ((NumberValue)ls.GetValue(1, 1)).Value.Should().BeApproximately(3.14, 1e-9);
        ((TextValue)ls.GetValue(1, 2)).Value.Should().Be("world");
        var formulaAddr = new CellAddress(ls.Id, 2, 1);
        ls.GetCell(2, 1)!.FormulaText.Should().NotBeNullOrEmpty();
        // ClosedXML preserves cached formula values on round-trip
        var reloadedFormulaCell = loaded.GetSheet(sheet.Name)!.GetCell(formulaAddr);
        reloadedFormulaCell!.Value.Should().NotBeOfType<BlankValue>();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_NamedRange_OnSheetWithApostrophe()
    {
        var workbook = new Workbook("NamedRangeTest");
        var sheet = workbook.AddSheet("Bob's Sheet");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        workbook.DefineNamedRange("SalesData", range);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.TryGetNamedRange("SalesData", out var loadedRange).Should().BeTrue();
        var loadedSheet = loaded.GetSheet("Bob's Sheet");
        loadedSheet.Should().NotBeNull();
        loadedRange.Start.Sheet.Should().Be(loadedSheet!.Id);
        loadedRange.Start.ToA1().Should().Be("A1");
        loadedRange.End.ToA1().Should().Be("A2");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Styles()
    {
        var workbook = new Workbook("StyleTest");
        var sheet = workbook.AddSheet("S1");

        var style = new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(200, 0, 0),
        };
        var styleId = workbook.RegisterStyle(style);

        var addr = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("styled"));
        cell.StyleId = styleId;
        sheet.SetCell(addr, cell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(1, 1);
        loadedCell.Should().NotBeNull();
        var loadedStyle = loaded.GetStyle(loadedCell!.StyleId);
        loadedStyle.Bold.Should().BeTrue();
        loadedStyle.FontColor.R.Should().Be(200);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnWidths()
    {
        var workbook = new Workbook("WidthTest");
        var sheet = workbook.AddSheet("S1");
        sheet.ColumnWidths[2] = 25.0;

        // Put cells in both col 1 and col 2 so ColumnsUsed() sees col 2 and reads back its width.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("y"));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ColumnWidths.Should().ContainKey(2u);
        loaded.GetSheetAt(0).ColumnWidths[2u].Should().BeApproximately(25.0, 1.0);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenRowsAndColumns()
    {
        var workbook = new Workbook("HiddenLayoutTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("hidden markers"));
        sheet.HiddenRows.Add(3);
        sheet.HiddenCols.Add(4);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.HiddenRows.Should().Contain(3u);
        loadedSheet.HiddenCols.Should().Contain(4u);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_SplitPanes_PreservesSplitInsteadOfFreeze()
    {
        var workbook = new Workbook("SplitPaneXlsxTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SplitRow = 8;
        sheet.SplitColumn = 4;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SplitRow.Should().Be(8);
        loadedSheet.SplitColumn.Should().Be(4);
        loadedSheet.FrozenRows.Should().Be(0);
        loadedSheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_FreezePanes_PreservesFreezeInsteadOfSplit()
    {
        var workbook = new Workbook("FreezePaneXlsxTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.FrozenRows.Should().Be(2);
        loadedSheet.FrozenCols.Should().Be(1);
        loadedSheet.SplitRow.Should().BeNull();
        loadedSheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenSheetAndTabColor()
    {
        var workbook = new Workbook("HiddenSheetTest");
        var visible = workbook.AddSheet("Visible");
        visible.SetCell(new CellAddress(visible.Id, 1, 1), new TextValue("visible"));
        var hidden = workbook.AddSheet("Hidden");
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden"));
        hidden.IsHidden = true;
        hidden.TabColor = new CellColor(255, 192, 0);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedHidden = loaded.GetSheet("Hidden");
        loadedHidden.Should().NotBeNull();
        loadedHidden!.IsHidden.Should().BeTrue();
        loadedHidden.TabColor.Should().Be(new CellColor(255, 192, 0));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenEmptyRowsAndColumns()
    {
        var workbook = new Workbook("HiddenEmptyLayoutTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));
        sheet.HiddenRows.Add(10);
        sheet.HiddenCols.Add(7);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.HiddenRows.Should().Contain(10u);
        loadedSheet.HiddenCols.Should().Contain(7u);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintArea()
    {
        var workbook = new Workbook("PrintAreaTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("outside"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("inside"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintArea.Should().NotBeNull();
        loadedSheet.PrintArea!.Value.Start.Row.Should().Be(2);
        loadedSheet.PrintArea.Value.Start.Col.Should().Be(2);
        loadedSheet.PrintArea.Value.End.Row.Should().Be(4);
        loadedSheet.PrintArea.Value.End.Col.Should().Be(3);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PageOrientation()
    {
        var workbook = new Workbook("PageOrientationTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PaperSize()
    {
        var workbook = new Workbook("PaperSizeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PaperSize = WorksheetPaperSize.Legal;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PaperSize.Should().Be(WorksheetPaperSize.Legal);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PageMargins()
    {
        var workbook = new Workbook("PageMarginsTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageMargins = WorksheetPageMargins.Wide;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PageMargins.Left.Should().BeApproximately(1.25, 0.001);
        loaded.GetSheetAt(0).PageMargins.Right.Should().BeApproximately(1.25, 0.001);
        loaded.GetSheetAt(0).PageMargins.Top.Should().BeApproximately(1.0, 0.001);
        loaded.GetSheetAt(0).PageMargins.Bottom.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintGridlinesAndHeadings()
    {
        var workbook = new Workbook("PrintOptionsTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PrintGridlines.Should().BeTrue();
        loaded.GetSheetAt(0).PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintTitlesPageBreaksAndScaleToFit()
    {
        var workbook = new Workbook("PageSetupTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.HeaderMargin = 0.35;
        sheet.FooterMargin = 0.45;
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Page &[Page] of &[Pages]", "Right footer");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First header left", "First header center", "First header right");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 5;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        loadedSheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedSheet.HeaderMargin.Should().Be(0.35);
        loadedSheet.FooterMargin.Should().Be(0.45);
        loadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        loadedSheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Page &[Page] of &[Pages]", "Right footer"));
        loadedSheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First header left", "First header center", "First header right"));
        loadedSheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        loadedSheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right"));
        loadedSheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        loadedSheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        loadedSheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        loadedSheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        loadedSheet.HeaderFooterAlignWithMargins.Should().BeFalse();
        loadedSheet.CenterHorizontallyOnPage.Should().BeTrue();
        loadedSheet.CenterVerticallyOnPage.Should().BeTrue();
        loadedSheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        loadedSheet.FirstPageNumber.Should().Be(5);
        loadedSheet.PrintBlackAndWhite.Should().BeTrue();
        loadedSheet.PrintDraftQuality.Should().BeTrue();
        loadedSheet.PrintQualityDpi.Should().Be(600);
        loadedSheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Dash);
        loadedSheet.PrintComments.Should().Be(WorksheetPrintComments.AsDisplayed);
        loadedSheet.RowPageBreaks.Should().Contain(20u);
        loadedSheet.ColumnPageBreaks.Should().Contain(4u);
        loadedSheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 1));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_CalculationMode()
    {
        var workbook = new Workbook("CalculationModeTest");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Hyperlinks()
    {
        var workbook = new Workbook("HyperlinkTest");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com/docs";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);
        loadedSheet.Hyperlinks[loadedAddr].Should().Be("https://example.com/docs");
        loadedSheet.GetValue(loadedAddr).Should().Be(new TextValue("Example"));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_SheetProtection()
    {
        var workbook = new Workbook("ProtectionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPassword.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_UnlockedCellStyle()
    {
        var workbook = new Workbook("UnlockedStyleTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle { Locked = false });
        var cell = Cell.FromValue(new TextValue("editable"));
        cell.StyleId = styleId;
        sheet.SetCell(address, cell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(1, 1);
        loadedCell.Should().NotBeNull();
        loaded.GetStyle(loadedCell!.StyleId).Locked.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_CellComments()
    {
        var workbook = new Workbook("CommentTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("review"));
        sheet.Comments[address] = "Check this input";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 2, 3);
        loadedSheet.Comments.Should().ContainKey(loadedAddress);
        loadedSheet.Comments[loadedAddress].Should().Be("Check this input");
    }

    [Theory]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#REF!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#N/A")]
    [InlineData("#NUM!")]
    public void XlsxAdapter_RoundTrip_ErrorValues_PreservesErrorCode(string errorCode)
    {
        var workbook = new Workbook("ErrorRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue(errorCode));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).GetValue(1, 1).Should().Be(new ErrorValue(errorCode));
    }

    // ── XLSX — conditional formatting round-trip ──────────────────────────────

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_CellValueRule_Survives()
    {
        var workbook = new Workbook("CfTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            RuleType    = CfRuleType.CellValue,
            Operator    = CfOperator.GreaterThan,
            Value1      = "5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };
        sheet.ConditionalFormats.Add(cf);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.ConditionalFormats.Should().NotBeEmpty("CF rule must survive XLSX round-trip");
        var rule = loadedSheet.ConditionalFormats[0];
        rule.RuleType.Should().Be(CfRuleType.CellValue);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_DataValidation_ListRule_Survives()
    {
        var workbook = new Workbook("DvTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            Type     = DvType.List,
            Formula1 = "Apple,Banana,Cherry"
        };
        sheet.DataValidations.Add(dv);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DataValidations.Should().NotBeEmpty("DV rule must survive XLSX round-trip");
        var rule = loadedSheet.DataValidations[0];
        rule.Type.Should().Be(DvType.List);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_MergedRegions_Survive()
    {
        var workbook = new Workbook("MergeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged"));
        sheet.MergedRegions.Add(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)));

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var loaded = new XlsxFileAdapter().Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.MergedRegions.Should().HaveCount(1);
        loadedSheet.MergedRegions[0].Start.Row.Should().Be(1);
        loadedSheet.MergedRegions[0].End.Row.Should().Be(2);
        loadedSheet.MergedRegions[0].End.Col.Should().Be(3);
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    [Fact]
    public void XlsxAdapter_SaveFromModel_DoesNotPreserveUnknownPackageParts()
    {
        var workbook = new Workbook("UnknownPartTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnknownPackagePart(source, "customXml/item1.xml", "<freexcel-test />");

        source.Position = 0;
        var loaded = adapter.Load(source);
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("customXml/item1.xml").Should().BeNull(
            "v1 XLSX save writes from the Freexcel model and does not preserve unsupported OOXML package parts");
    }

    private static void AddUnknownPackagePart(MemoryStream packageStream, string entryName, string content)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(entryName)?.Delete();
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        packageStream.Position = 0;
    }

    [Fact]
    public void CsvAdapter_RoundTrip()
    {
        var workbook = new Workbook("CsvTest");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("world"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(9));

        var ms = new MemoryStream();
        var adapter = new CsvFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var ls = loaded.GetSheetAt(0);
        ((NumberValue)ls.GetValue(1, 1)).Value.Should().Be(1);
        ((NumberValue)ls.GetValue(1, 2)).Value.Should().Be(2);
        ((TextValue)ls.GetValue(1, 3)).Value.Should().Be("hello");
        ((NumberValue)ls.GetValue(3, 3)).Value.Should().Be(9);
    }

    [Fact]
    public void CsvAdapter_Load_AutoDetectsNumbers()
    {
        const string csv = "1,2,hello\r\n";
        var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var adapter = new CsvFileAdapter();
        var loaded = adapter.Load(ms);

        var sheet = loaded.GetSheetAt(0);
        sheet.GetValue(1, 1).Should().BeOfType<NumberValue>().Which.Value.Should().Be(1);
        sheet.GetValue(1, 2).Should().BeOfType<NumberValue>().Which.Value.Should().Be(2);
        sheet.GetValue(1, 3).Should().BeOfType<TextValue>().Which.Value.Should().Be("hello");
    }
}
