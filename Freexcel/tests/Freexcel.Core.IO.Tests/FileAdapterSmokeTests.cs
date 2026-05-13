using System.Text;
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
        ls2.GetCell(1, 1)!.FormulaText.Should().Be("A1+1");
    }

    // ── XLSX ──────────────────────────────────────────────────────────────────

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
