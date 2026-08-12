using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
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

    [Fact]
    public void NativeJsonAdapter_Load_StripsLeadingEqualsFromFormulaText()
    {
        const string json = """
        {
          "Name": "FormulaPrefixLoad",
          "Sheets": [
            {
              "Name": "Sheet1",
              "Cells": [
                { "Address": "A1", "Formula": "=B1+1" }
              ]
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = new NativeJsonAdapter().Load(stream);

        loaded.GetSheetAt(0).GetCell(1, 1)!.FormulaText.Should().Be("B1+1");
    }

    [Fact]
    public void NativeJsonAdapter_Save_StripsLeadingEqualsFromFormulaText()
    {
        var workbook = new Workbook("FormulaPrefixSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("=B1+1"));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var document = JsonDocument.Parse(stream);
        document.RootElement
            .GetProperty("Sheets")[0]
            .GetProperty("Cells")[0]
            .GetProperty("Formula")
            .GetString()
            .Should().Be("B1+1");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_FormulaCachedValue()
    {
        var workbook = new Workbook("FormulaCachedValue");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "B1+1",
            Value = new NumberValue(42)
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var cell = loaded.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("B1+1");
        cell.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_FormulaArrayMode()
    {
        var workbook = new Workbook("FormulaArrayMode");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "B1:B2",
            ArrayMode = FormulaArrayMode.Implicit
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var document = JsonDocument.Parse(stream))
        {
            document.RootElement
                .GetProperty("Sheets")[0]
                .GetProperty("Cells")[0]
                .GetProperty("FormulaArrayMode")
                .GetString()
                .Should().Be(nameof(FormulaArrayMode.Implicit));
        }

        stream.Position = 0;
        var cell = adapter.Load(stream).GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("B1:B2");
        cell.ArrayMode.Should().Be(FormulaArrayMode.Implicit);
    }

    [Fact]
    public void NativeJsonAdapter_SaveThenResolveOpenAdapterAndReload()
    {
        var workbook = new Workbook("ResolvableNative");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Saved"));

        using var stream = new MemoryStream();
        var saveAdapter = new NativeJsonAdapter();
        saveAdapter.Save(workbook, stream);
        stream.Position = 0;

        var openAdapter = FileFormatResolver.FindOpenAdapter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new NativeJsonAdapter()],
            ".fxl",
            out var format);

        openAdapter.Should().BeOfType<NativeJsonAdapter>();
        format!.Extension.Should().Be(".fxl");
        var loaded = openAdapter!.Load(stream);
        loaded.GetSheetAt(0).GetCell(1, 1).Should().NotBeNull();
    }
}
