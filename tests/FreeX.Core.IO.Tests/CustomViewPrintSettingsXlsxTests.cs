using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class CustomViewPrintSettingsXlsxTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Id = "{11111111-1111-1111-1111-111111111111}";

    [Fact]
    public void CustomView_PrintSettingsAndScopedNames_RoundTrip()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("O'Brien, Data");
        var area = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 9, 4));
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [area])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings
            {
                PageHeader = new("Left", "Center &[Page]", "Right"),
                FirstPageFooter = new("", "First footer", ""),
                EvenPageHeader = new("", "", "Even"),
                DifferentFirstPageHeaderFooter = true,
                DifferentOddEvenHeaderFooter = true,
                HeaderFooterScaleWithDocument = false,
                CenterHorizontallyOnPage = true,
                PageOrder = WorksheetPageOrder.OverThenDown,
                FirstPageNumber = 4,
                PrintQualityDpi = 300,
                PrintBlackAndWhite = true,
                PrintErrorValue = WorksheetPrintErrorValue.Dash,
                PrintComments = WorksheetPrintComments.AtEnd,
                RowPageBreaks = [5],
                ColumnPageBreaks = [3],
                PrintTitleRows = new(1, 2),
                PrintTitleColumns = new(1, 1)
            }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var wsStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var view = XDocument.Load(wsStream).Descendants(Ns + "customSheetView").Single();
            view.Elements().Select(x => x.Name.LocalName).Should().ContainInOrder("rowBreaks", "colBreaks", "printOptions", "pageSetup", "headerFooter");
            view.Element(Ns + "headerFooter")!.Element(Ns + "oddHeader")!.Value.Should().Be("&LLeft&CCenter &P&RRight");
            view.Attribute("printArea")!.Value.Should().Be("1");
            using var wbStream = archive.GetEntry("xl/workbook.xml")!.Open();
            var names = XDocument.Load(wbStream).Descendants(Ns + "definedName").ToList();
            names.Should().Contain(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintArea") &&
                x.Value == "'O''Brien, Data'!$A$2:$D$9");
            names.Should().Contain(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintTitles") &&
                x.Value == "'O''Brien, Data'!$1:$2,'O''Brien, Data'!$A:$A");
        }

        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, false))
        {
            var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).ToList();
            errors.Should().BeEmpty(string.Join("; ", errors.Select(error => error.Description)));
        }
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedState = loaded.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        loadedState.PrintSettings.Should().NotBeNull();
        var print = loadedState.PrintSettings!;
        print.PageHeader.Should().Be(new WorksheetHeaderFooter("Left", "Center &[Page]", "Right"));
        print.FirstPageFooter.Center.Should().Be("First footer");
        print.EvenPageHeader.Right.Should().Be("Even");
        print.DifferentFirstPageHeaderFooter.Should().BeTrue();
        print.DifferentOddEvenHeaderFooter.Should().BeTrue();
        print.HeaderFooterScaleWithDocument.Should().BeFalse();
        print.CenterHorizontallyOnPage.Should().BeTrue();
        print.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        print.FirstPageNumber.Should().Be(4);
        print.PrintQualityDpi.Should().Be(300);
        print.PrintBlackAndWhite.Should().BeTrue();
        print.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Dash);
        print.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);
        print.RowPageBreaks.Should().Equal(5u);
        print.ColumnPageBreaks.Should().Equal(3u);
        print.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        print.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedState.PrintAreas.Should().ContainSingle().Which.ToString().Should().Be("A2:D9");
        loaded.NamedRanges.Keys.Should().NotContain(name => name.Contains(".wvu."));
        loaded.ScopedNamedRanges.Keys.Should().NotContain(key => key.Name.Contains(".wvu."));
    }

    [Fact]
    public void CustomView_WithoutPrintAreaOrTitles_LoadsCapturedEmptyState()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings()
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loadedState = adapter.Load(stream).CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        loadedState.PrintAreas.Should().NotBeNull().And.BeEmpty();
        loadedState.PrintSettings.Should().NotBeNull();
        loadedState.PrintSettings!.PrintTitleRows.Should().BeNull();
        loadedState.PrintSettings.PrintTitleColumns.Should().BeNull();
    }

    [Fact]
    public void DeletingCustomView_RemovesItsPrintNames()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var area = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [area])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings { PrintTitleRows = new(1, 2) }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var first = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, first);
        first.Position = 0;
        var loaded = adapter.Load(first);
        loaded.CustomViews.Clear();
        using var second = new MemoryStream();
        adapter.Save(loaded, second);
        second.Position = 0;
        using var archive = new ZipArchive(second, ZipArchiveMode.Read);
        using var wbStream = archive.GetEntry("xl/workbook.xml")!.Open();
        XDocument.Load(wbStream).Descendants(Ns + "definedName")
            .Should().NotContain(x => x.Attribute("name") != null && x.Attribute("name")!.Value.Contains(".wvu."));
    }

    [Fact]
    public void EditingOneViewAndRemovingAnother_ReplacesScopedPrintNames()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2))])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings { PrintTitleRows = new(1, 2) }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("First", [state], Id));
        workbook.CustomViews.Add(new WorkbookCustomView("Second", [state], "{22222222-2222-2222-2222-222222222222}"));
        using var first = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, first);
        first.Position = 0;
        var loaded = adapter.Load(first);
        var kept = loaded.CustomViews.Single(view => view.Name == "First");
        var loadedSheet = loaded.GetSheet("Data")!;
        var updated = kept.Sheets.Single() with
        {
            PrintAreas = [new GridRange(new CellAddress(loadedSheet.Id, 3, 3), new CellAddress(loadedSheet.Id, 8, 4))],
            PrintSettings = kept.Sheets.Single().PrintSettings! with { PrintTitleRows = null }
        };
        loaded.CustomViews.Clear();
        loaded.CustomViews.Add(kept with { Sheets = [updated] });
        using var second = new MemoryStream();
        adapter.Save(loaded, second);
        second.Position = 0;
        using (var archive = new ZipArchive(second, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var wbStream = archive.GetEntry("xl/workbook.xml")!.Open();
            var names = XDocument.Load(wbStream).Descendants(Ns + "definedName")
                .Where(x => x.Attribute("name") != null && x.Attribute("name")!.Value.Contains(".wvu.")).ToList();
            names.Should().ContainSingle().Which.Value.Should().Be("Data!$C$3:$D$8");
            using var wsStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var worksheet = XDocument.Load(wsStream);
            worksheet.Descendants(Ns + "customSheetView").Should().ContainSingle();
        }
        second.Position = 0;
        var reloaded = adapter.Load(second);
        reloaded.CustomViews.Should().ContainSingle().Which.Name.Should().Be("First");
        var result = reloaded.CustomViews.Single().Sheets.Single();
        result.PrintAreas.Should().ContainSingle().Which.ToString().Should().Be("C3:D8");
        result.PrintSettings!.PrintTitleRows.Should().BeNull();
    }

    [Fact]
    public void PrintMetadata_RetainsSupportedAttributesAndDropsInvalidOnes()
    {
        static NativeXmlPreserveBag Bag(string key, string xml)
        {
            var bag = new NativeXmlPreserveBag();
            bag.Set(key, xml);
            return bag;
        }
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var breakMetadata = new WorksheetPageBreaksMetadataModel();
        breakMetadata.BreakNativeAttributes[5] = new Dictionary<string, string>
        {
            ["min"] = "2", ["max"] = "20", ["pt"] = "1", ["unsupported"] = "bad"
        };
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PageMargins: WorksheetPageMargins.Normal)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings
            {
                RowPageBreaks = [5],
                RowPageBreaksMetadata = breakMetadata,
                PrintOptionsMetadata = Bag("printOptions", "<e gridLinesSet=\"1\" unsupported=\"bad\"/>"),
                PageMarginsMetadata = Bag("pageMargins", "<e unsupported=\"bad\"/>"),
                PageSetupMetadata = Bag("pageSetup", "<e unsupported=\"bad\"/>"),
                HeaderFooterMetadata = Bag("headerFooter", "<e unsupported=\"bad\"/>")
            }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var ws = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var view = XDocument.Load(ws).Descendants(Ns + "customSheetView").Single();
            view.Element(Ns + "printOptions")!.Attribute("gridLinesSet")!.Value.Should().Be("1");
            var brk = view.Element(Ns + "rowBreaks")!.Element(Ns + "brk")!;
            brk.Attribute("min")!.Value.Should().Be("2");
            brk.Attribute("max")!.Value.Should().Be("20");
            brk.Attribute("pt")!.Value.Should().Be("1");
            foreach (var element in view.DescendantsAndSelf())
                element.Attribute("unsupported").Should().BeNull();
        }
        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, false))
            new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).Should().BeEmpty();
        stream.Position = 0;
        var print = adapter.Load(stream).CustomViews.Single().Sheets.Single().PrintSettings!;
        print.PrintOptionsMetadata!.Get("printOptions").Should().Contain("gridLinesSet=\"1\"");
        print.RowPageBreaksMetadata!.BreakNativeAttributes[5]["min"].Should().Be("2");
    }

    [Theory]
    [InlineData("$A:$B", 1u, 1u, 1048576u, 2u, WorksheetCustomViewPrintAreaKind.FullColumn)]
    [InlineData("$2:$4", 2u, 1u, 4u, 16384u, WorksheetCustomViewPrintAreaKind.FullRow)]
    [InlineData("$A$1:$B$1048576", 1u, 1u, 1048576u, 2u, WorksheetCustomViewPrintAreaKind.CellRange)]
    public void ExcelStyleWholeAxisPrintArea_LoadsAndResavesItsOriginalForm(
        string reference, uint startRow, uint startColumn, uint endRow, uint endColumn,
        WorksheetCustomViewPrintAreaKind kind)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings()
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            entry.Delete();
            var names = xml.Root!.Element(Ns + "definedNames");
            if (names is null)
            {
                names = new XElement(Ns + "definedNames");
                xml.Root!.Element(Ns + "customWorkbookViews")!.AddBeforeSelf(names);
            }
            names.Add(new XElement(Ns + "definedName",
                new XAttribute("name", "Z_11111111_1111_1111_1111_111111111111_.wvu.PrintArea"),
                new XAttribute("localSheetId", "0"), new XAttribute("hidden", "1"),
                "Data!" + reference));
            using var output = archive.CreateEntry("xl/workbook.xml").Open();
            xml.Save(output);
        }
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedView = loaded.CustomViews.Single();
        var captured = loadedView.Sheets.Single();
        var area = captured.PrintAreas.Should().ContainSingle().Which;
        area.Start.Row.Should().Be(startRow);
        area.Start.Col.Should().Be(startColumn);
        area.End.Row.Should().Be(endRow);
        area.End.Col.Should().Be(endColumn);
        captured.PrintAreaReferenceForms.Should().ContainSingle().Which.Kind.Should().Be(kind);

        // Force an authored update, then inspect the name rather than relying on source replay.
        loaded.CustomViews[0] = loadedView with { Sheets = [captured with { ZoomPercent = 125 }] };
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        using var outputArchive = new ZipArchive(saved, ZipArchiveMode.Read);
        using var workbookXml = outputArchive.GetEntry("xl/workbook.xml")!.Open();
        XDocument.Load(workbookXml).Descendants(Ns + "definedName")
            .Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintArea"))
            .Value.Should().Be("Data!" + reference);
    }

    [Fact]
    public void EditedWholeAxisArea_DoesNotReuseStaleReferenceForm()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var original = new GridRange(new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        var edited = new GridRange(new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 4));
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [edited])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings(),
            PrintAreaReferenceForms = [new WorksheetCustomViewPrintAreaForm(original, WorksheetCustomViewPrintAreaKind.FullColumn)]
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var xml = archive.GetEntry("xl/workbook.xml")!.Open();
        XDocument.Load(xml).Descendants(Ns + "definedName")
            .Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintArea"))
            .Value.Should().Be("Data!$C$1:$D$1048576");
    }

    [Fact]
    public void LoadedWorkbookSave_PreservesNativeAttributesOnlyForSurvivingViewsAndSheets()
    {
        var workbook = new Workbook("Book");
        var firstSheet = workbook.AddSheet("First");
        var secondSheet = workbook.AddSheet("Second");
        var first = new WorksheetCustomViewState(firstSheet.Name, WorksheetViewMode.Normal, 0, 0, null, null);
        var second = new WorksheetCustomViewState(secondSheet.Name, WorksheetViewMode.Normal, 0, 0, null, null);
        workbook.CustomViews.Add(new WorkbookCustomView("Keep", [first, second], Id));
        workbook.CustomViews.Add(new WorkbookCustomView("Delete", [first], "{22222222-2222-2222-2222-222222222222}"));
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            entry.Delete();
            var view = xml.Descendants(Ns + "customWorkbookView")
                .Single(x => x.Attribute("guid")!.Value == Id);
            view.SetAttributeValue("autoUpdate", "1");
            view.SetAttributeValue("windowWidth", "23456");
            view.SetAttributeValue("windowHeight", "12345");
            view.SetAttributeValue("includePrintSettings", "1");
            XNamespace customNs = "urn:freex-custom-view-test";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            view.Add(new XElement(Ns + "extLst",
                new XElement(Ns + "ext", new XAttribute("uri", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"),
                    new XElement(customNs + "payload", new XAttribute("value", "workbook"))),
                new XElement(Ns + "ext", new XAttribute("uri", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"),
                    new XElement(customNs + "unsafe", new XAttribute(relationshipNs + "id", "rIdMissing")))));
            using (var output = archive.CreateEntry("xl/workbook.xml").Open()) xml.Save(output);

            entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            entry.Delete();
            view = xml.Descendants(Ns + "customSheetView").Single(x => x.Attribute("guid")!.Value == Id);
            view.SetAttributeValue("showPageBreaks", "1");
            view.SetAttributeValue("zeroValues", "0");
            view.SetAttributeValue("colorId", "12");
            view.SetAttributeValue("unrecognized", "discard");
            view.Add(new XElement(Ns + "extLst",
                new XElement(Ns + "ext", new XAttribute("uri", "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}"),
                    new XElement(customNs + "payload", new XAttribute("value", "sheet"))),
                new XElement(Ns + "ext", new XAttribute("uri", "{DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD}"),
                    new XElement(customNs + "unsafe", new XAttribute(relationshipNs + "id", "rIdMissing")))));
            using (var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open()) xml.Save(output);
        }
        source.Position = 0;
        var loaded = adapter.Load(source);
        var keep = loaded.CustomViews.Single(view => view.Name == "Keep");
        loaded.CustomViews.Clear();
        loaded.CustomViews.Add(keep with
        {
            IncludePrintSettings = false,
            Sheets = [keep.Sheets.Single(state => state.SheetName == "First") with { ZoomPercent = 125 }]
        });
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
            var view = XDocument.Load(workbookStream).Descendants(Ns + "customWorkbookView").Should().ContainSingle().Which;
            view.Attribute("guid")!.Value.Should().Be(Id);
            view.Attribute("autoUpdate")!.Value.Should().Be("1");
            view.Attribute("windowWidth")!.Value.Should().Be("23456");
            view.Attribute("windowHeight")!.Value.Should().Be("12345");
            view.Attribute("includePrintSettings")!.Value.Should().Be("0");
            var workbookExt = view.Element(Ns + "extLst")!.Elements(Ns + "ext").Should().ContainSingle().Which;
            workbookExt.Attribute("uri")!.Value.Should().Be("{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}");
            workbookExt.Descendants().Single().Attribute("value")!.Value.Should().Be("workbook");
            using var firstStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheetView = XDocument.Load(firstStream).Descendants(Ns + "customSheetView").Should().ContainSingle().Which;
            sheetView.Attribute("scale")!.Value.Should().Be("125");
            sheetView.Attribute("showPageBreaks")!.Value.Should().Be("1");
            sheetView.Attribute("zeroValues")!.Value.Should().Be("0");
            sheetView.Attribute("colorId")!.Value.Should().Be("12");
            sheetView.Attribute("unrecognized").Should().BeNull();
            var sheetExt = sheetView.Element(Ns + "extLst")!.Elements(Ns + "ext").Should().ContainSingle().Which;
            sheetExt.Attribute("uri")!.Value.Should().Be("{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}");
            sheetExt.Descendants().Single().Attribute("value")!.Value.Should().Be("sheet");
            using var secondStream = archive.GetEntry("xl/worksheets/sheet2.xml")!.Open();
            XDocument.Load(secondStream).Descendants(Ns + "customSheetView").Should().BeEmpty();
        }
        saved.Position = 0;
        using var document = SpreadsheetDocument.Open(saved, false);
        new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookSave_TracksNativeSheetViewAttributesAcrossRenameAndReorder()
    {
        var workbook = new Workbook("Book");
        var firstSheet = workbook.AddSheet("First");
        var secondSheet = workbook.AddSheet("Second");
        var first = new WorksheetCustomViewState(firstSheet.Name, WorksheetViewMode.Normal, 0, 0, null, null);
        var second = new WorksheetCustomViewState(secondSheet.Name, WorksheetViewMode.Normal, 0, 0, null, null);
        workbook.CustomViews.Add(new WorkbookCustomView("View", [first, second], Id));
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            for (int index = 1; index <= 2; index++)
            {
                var path = $"xl/worksheets/sheet{index}.xml";
                var entry = archive.GetEntry(path)!;
                XDocument xml;
                using (var input = entry.Open()) xml = XDocument.Load(input);
                entry.Delete();
                xml.Descendants(Ns + "customSheetView").Single()
                    .SetAttributeValue("colorId", index == 1 ? "11" : "22");
                using var output = archive.CreateEntry(path).Open();
                xml.Save(output);
            }
        }
        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheet("First")!.Name = "Renamed";
        loaded.MoveSheet(0, 1);
        var view = loaded.CustomViews.Single();
        loaded.CustomViews[0] = view with
        {
            Sheets = view.Sheets.Select(state => state.SheetName == "First"
                ? state with { SheetName = "Renamed" }
                : state).ToList()
        };
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        using var archiveOut = new ZipArchive(saved, ZipArchiveMode.Read);
        using var firstStream = archiveOut.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument.Load(firstStream).Descendants(Ns + "customSheetView").Single()
            .Attribute("colorId")!.Value.Should().Be("22");
        using var secondStream = archiveOut.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        XDocument.Load(secondStream).Descendants(Ns + "customSheetView").Single()
            .Attribute("colorId")!.Value.Should().Be("11");
    }

    [Fact]
    public void OpaquePrintNames_PreserveWholeExpression_AndModeledEditsReplaceThem()
    {
        const string areaExpression = "Data!$A$1:$B$2,OFFSET(Data!$C$1,0,0,2,1)";
        const string titlesExpression = "Data!$1:$2,OFFSET(Data!$A$1,0,0,1,1)";
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings()
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Review", [state], Id));
        using var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            entry.Delete();
            var names = xml.Root!.Element(Ns + "definedNames");
            if (names is null)
            {
                names = new XElement(Ns + "definedNames");
                xml.Root!.Element(Ns + "customWorkbookViews")!.AddBeforeSelf(names);
            }
            const string prefix = "Z_11111111_1111_1111_1111_111111111111_.wvu.";
            names.Add(new XElement(Ns + "definedName", new XAttribute("name", prefix + "PrintArea"),
                new XAttribute("localSheetId", "0"), new XAttribute("hidden", "1"), areaExpression));
            names.Add(new XElement(Ns + "definedName", new XAttribute("name", prefix + "PrintTitles"),
                new XAttribute("localSheetId", "0"), new XAttribute("hidden", "1"), titlesExpression));
            using var output = archive.CreateEntry("xl/workbook.xml").Open();
            xml.Save(output);
        }
        source.Position = 0;
        var loaded = adapter.Load(source);
        var view = loaded.CustomViews.Single();
        var captured = view.Sheets.Single();
        captured.PrintAreas.Should().BeNull();
        captured.OpaquePrintAreaExpression.Should().Be(areaExpression);
        captured.OpaquePrintTitlesExpression.Should().Be(titlesExpression);
        captured.PrintSettings!.PrintTitleRows.Should().BeNull();
        var live = loaded.GetSheet("Data")!;
        live.PrintTitleRows = new WorksheetRepeatRange(5, 6);
        live.PrintTitleColumns = new WorksheetRepeatRange(2, 3);
        live.ZoomPercent = 150;
        var context = new TestCommandContext(loaded);
        var apply = new ApplyCustomViewCommand("Review");
        apply.Apply(context).Success.Should().BeTrue();
        live.PrintTitleRows.Should().Be(new WorksheetRepeatRange(5, 6));
        live.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(2, 3));
        apply.Revert(context);
        live.PrintTitleRows.Should().Be(new WorksheetRepeatRange(5, 6));
        live.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(2, 3));
        live.ZoomPercent.Should().Be(150);
        loaded.CustomViews[0] = view with { Sheets = [captured with { ZoomPercent = 125 }] };

        using var preserved = new MemoryStream();
        adapter.Save(loaded, preserved);
        preserved.Position = 0;
        using (var archive = new ZipArchive(preserved, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var stream = archive.GetEntry("xl/workbook.xml")!.Open();
            var names = XDocument.Load(stream).Descendants(Ns + "definedName").ToList();
            names.Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintArea")).Value.Should().Be(areaExpression);
            names.Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintTitles")).Value.Should().Be(titlesExpression);
        }

        var updated = loaded.CustomViews.Single();
        var loadedSheet = loaded.GetSheet("Data")!;
        var current = updated.Sheets.Single();
        loaded.CustomViews[0] = updated with
        {
            Sheets = [current with
            {
                PrintAreas = [new GridRange(new CellAddress(loadedSheet.Id, 3, 3), new CellAddress(loadedSheet.Id, 4, 4))],
                PrintSettings = current.PrintSettings! with { PrintTitleRows = new WorksheetRepeatRange(3, 4) }
            }]
        };
        using var edited = new MemoryStream();
        adapter.Save(loaded, edited);
        edited.Position = 0;
        using var editedArchive = new ZipArchive(edited, ZipArchiveMode.Read);
        using var editedStream = editedArchive.GetEntry("xl/workbook.xml")!.Open();
        var editedNames = XDocument.Load(editedStream).Descendants(Ns + "definedName").ToList();
        editedNames.Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintArea")).Value.Should().Be("Data!$C$3:$D$4");
        editedNames.Single(x => x.Attribute("name")!.Value.EndsWith("_.wvu.PrintTitles")).Value.Should().Be("Data!$3:$4");

        // An empty area is an explicit clear; an opaque titles expression is explicitly
        // cleared by clearing its field together with both modeled repeat ranges.
        var editedView = loaded.CustomViews.Single();
        var editedState = editedView.Sheets.Single();
        loaded.CustomViews[0] = editedView with
        {
            Sheets = [editedState with
            {
                PrintAreas = [],
                OpaquePrintTitlesExpression = null,
                PrintSettings = editedState.PrintSettings! with { PrintTitleRows = null, PrintTitleColumns = null }
            }]
        };
        using var cleared = new MemoryStream();
        adapter.Save(loaded, cleared);
        cleared.Position = 0;
        using (var archive = new ZipArchive(cleared, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
            XDocument.Load(workbookStream).Descendants(Ns + "definedName")
                .Should().NotContain(x => x.Attribute("name") != null && x.Attribute("name")!.Value.Contains(".wvu."));
        }
        loaded.CustomViews.Clear();
        using var deleted = new MemoryStream();
        adapter.Save(loaded, deleted);
        deleted.Position = 0;
        using var deletedArchive = new ZipArchive(deleted, ZipArchiveMode.Read);
        using var deletedStream = deletedArchive.GetEntry("xl/workbook.xml")!.Open();
        XDocument.Load(deletedStream).Descendants(Ns + "definedName")
            .Should().NotContain(x => x.Attribute("name") != null && x.Attribute("name")!.Value.Contains(".wvu."));
    }
}
