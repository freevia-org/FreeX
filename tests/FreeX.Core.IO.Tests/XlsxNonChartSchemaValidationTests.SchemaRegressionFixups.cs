using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void LoadedWorkbookSave_DropsExcelRevisionUidsAndKeepsIndexedDifferentialStylesSchemaValid()
    {
        var source = Save(CreateExcelAuthoredSchemaRegressionWorkbook());
        AddExcelRevisionUidMetadata(source);
        ReplaceDifferentialStylesWithExcelIndexedStyles(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        SchemaErrors(saved).Should().BeEmpty();

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        workbookXml.Descendants().SelectMany(element => element.Attributes()).Where(IsOfficeRevisionAttribute).Should().BeEmpty();
        worksheetXml.Descendants().SelectMany(element => element.Attributes()).Where(IsOfficeRevisionAttribute).Should().BeEmpty();

        var dxf = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "dxfs")!
            .Elements(workbookNs + "dxf")
            .First();
        dxf.Element(workbookNs + "numFmt").Should().BeNull();
        dxf.Element(workbookNs + "font")!.Element(workbookNs + "b").Should().BeNull();
    }


    [Fact]
    public void LoadedWorkbookSave_DropsMalformedSourceOnlyRelationshipParts()
    {
        var workbook = new Workbook("MalformedRelationships");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var source = Save(workbook);
        AddMalformedSourceOnlyRelationshipPart(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("customXml/item99.xml").Should().NotBeNull();
        archive.GetEntry("customXml/_rels/item99.xml.rels").Should().BeNull();
        SchemaErrors(saved).Should().BeEmpty();
    }


    [Fact]
    public void LoadedWorkbookSave_RemovesSourceNativeCustomViewsForExcelCompatibility()
    {
        var workbook = new Workbook("CustomViewSchemaRepair");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var source = Save(workbook);
        AddLowercaseNativeCustomViews(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        LoadPackageXml(archive.GetEntry("xl/workbook.xml")!)
            .Root!
            .Element(workbookNs + "customWorkbookViews")
            .Should()
            .BeNull();
        LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!)
            .Root!
            .Element(workbookNs + "customSheetViews")
            .Should()
            .BeNull();
        SchemaErrors(saved).Should().BeEmpty();
    }


    [Fact]
    public void StyleFonts_WithUnderlineStrikeAndBaselineVertAlign_ProduceSchemaValidWorkbook()
    {
        var workbook = new Workbook("RegularFontOrder");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue("styled"),
            StyleId = workbook.RegisterStyle(new CellStyle
            {
                Italic = true,
                Underline = true,
                Strikethrough = true,
                Subscript = true,
                FontSize = 9,
                FontColor = new CellColor(255, 0, 0),
                FontName = "Arial"
            })
        });

        using var stream = Save(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var styledFont = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "fonts")!
            .Elements(workbookNs + "font")
            .Last();

        styledFont.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder(
            "i",
            "strike",
            "u",
            "vertAlign",
            "sz",
            "color",
            "name",
            "family");

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty();
    }


    [Fact]
    public void StylesheetSchemaNormalizer_OrdersTopLevelElementsAndColorChildren()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = new XDocument(new XElement(
            workbookNs + "styleSheet",
            new XElement(workbookNs + "numFmts"),
            new XElement(workbookNs + "fonts"),
            new XElement(workbookNs + "fills"),
            new XElement(workbookNs + "borders"),
            new XElement(workbookNs + "cellStyleXfs"),
            new XElement(workbookNs + "cellXfs"),
            new XElement(workbookNs + "cellStyles"),
            new XElement(
                workbookNs + "colors",
                new XElement(workbookNs + "mruColors"),
                new XElement(workbookNs + "indexedColors")),
            new XElement(workbookNs + "dxfs"),
            new XElement(workbookNs + "tableStyles"),
            new XElement(workbookNs + "extLst")));

        XlsxStylesheetSchemaNormalizer.NormalizeStylesheet(stylesXml, workbookNs).Should().BeTrue();

        stylesXml.Root!.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder(
            "numFmts",
            "fonts",
            "fills",
            "borders",
            "cellStyleXfs",
            "cellXfs",
            "cellStyles",
            "dxfs",
            "tableStyles",
            "colors",
            "extLst");
        stylesXml.Root.Element(workbookNs + "colors")!
            .Elements()
            .Select(element => element.Name.LocalName)
            .Should()
            .ContainInOrder("indexedColors", "mruColors");
    }


    [Fact]
    public void WorkbookSchemaNormalizer_OrdersWorkbookPropertiesBeforeProtection()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = new XDocument(new XElement(
            workbookNs + "workbook",
            new XElement(workbookNs + "fileVersion"),
            new XElement(workbookNs + "workbookProtection"),
            new XElement(workbookNs + "workbookPr"),
            new XElement(workbookNs + "bookViews"),
            new XElement(workbookNs + "sheets"),
            new XElement(workbookNs + "definedNames"),
            new XElement(workbookNs + "calcPr"),
            new XElement(workbookNs + "revisionPtr"),
            new XElement(workbookNs + "extLst")));

        XlsxWorkbookSchemaNormalizer.NormalizeWorkbook(workbookXml, workbookNs).Should().BeTrue();

        workbookXml.Root!.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder(
            "revisionPtr",
            "fileVersion",
            "workbookPr",
            "workbookProtection",
            "bookViews",
            "sheets",
            "definedNames",
            "calcPr",
            "extLst");
    }


    [Fact]
    public void LoadedWorkbookSave_SanitizesRichSharedStringFontFamiliesForSchemaValidity()
    {
        var workbook = new Workbook("RichSharedStringFonts");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Rich font"));

        var source = Save(workbook);
        AddCssFontFamilyRichSharedString(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rFont = LoadPackageXml(archive.GetEntry("xl/sharedStrings.xml")!)
            .Root!
            .Elements(workbookNs + "si")
            .Single(element => element.Value == "Rich font")
            .Element(workbookNs + "r")!
            .Element(workbookNs + "rPr")!
            .Element(workbookNs + "rFont")!;
        rFont.Attribute("val")!.Value.Should().Be("Google Sans");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
    }

}
