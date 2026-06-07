using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void StylesheetTableStyleMetadata_ProducesSchemaValidWorkbook()
    {
        using var saved = Save(CreateAuthoredStylesheetMetadataWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var stylesXml = ReadPackageRootElement(saved, "xl/styles.xml");
        AssertStylesheetTableStyleMetadata(stylesXml, "FreeXAuthoredTableStyle", "FreeXAuthoredPivotStyle");
        AssertStylesheetChildOrder(stylesXml);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithStylesheetTableStyleMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateExcelStylesheetMetadataSourcePackage();
        var sourceStylesXml = ReadPackageRootElement(source, "xl/styles.xml");
        var sourceDifferentialStyles = ReadStylesheetChildElement(sourceStylesXml, "dxfs");
        var sourceTableStyles = ReadStylesheetChildElement(sourceStylesXml, "tableStyles");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.StructuredTableStyles.Should().ContainSingle(style =>
            style.Name == "ExcelNativeStructuredStyle" &&
            style.Elements.Count == 2);
        workbook.PivotTableStyles.Should().ContainSingle(style =>
            style.Name == "ExcelNativePivotStyle" &&
            style.Elements.Count == 1);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        var savedStylesXml = ReadPackageRootElement(saved, "xl/styles.xml");
        AssertStylesheetTableStyleMetadata(savedStylesXml, "ExcelNativeStructuredStyle", "ExcelNativePivotStyle");
        AssertStylesheetChildOrder(savedStylesXml);
        ReadStylesheetChildElement(savedStylesXml, "dxfs")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDifferentialStyles.ToString(SaveOptions.DisableFormatting));
        ReadStylesheetChildElement(savedStylesXml, "tableStyles")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTableStyles.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidStylesheetTableStyleMetadataForSchemaValidity()
    {
        using var source = CreateExcelStylesheetMetadataSourcePackage();
        SetStylesheetTableStylesInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var savedStylesXml = ReadPackageRootElement(saved, "xl/styles.xml");
        AssertStylesheetTableStyleMetadata(savedStylesXml, "ExcelNativeStructuredStyle", "ExcelNativePivotStyle");
        AssertStylesheetTableStylesSanitized(savedStylesXml);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidStylesheetDifferentialStyleMetadataForSchemaValidity()
    {
        using var source = CreateExcelStylesheetMetadataSourcePackage();
        SetStylesheetDifferentialStylesInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var savedStylesXml = ReadPackageRootElement(saved, "xl/styles.xml");
        AssertStylesheetDifferentialStylesSanitized(savedStylesXml);
        AssertStylesheetTableStyleMetadata(savedStylesXml, "ExcelNativeStructuredStyle", "ExcelNativePivotStyle");
    }

    private static Workbook CreateAuthoredStylesheetMetadataWorkbook()
    {
        var workbook = CreateStylesheetMetadataWorkbook("FreeXAuthoredTableStyle");
        var pivotStyle = new PivotTableStyleModel
        {
            Name = "FreeXAuthoredPivotStyle",
            AppliesToPivotTables = true,
            AppliesToTables = false
        };
        pivotStyle.Elements.Add(new PivotTableStyleElementModel("wholeTable", 0));
        pivotStyle.Elements.Add(new PivotTableStyleElementModel("firstRowStripe", 1, 1));
        workbook.PivotTableStyles.Add(pivotStyle);
        workbook.StructuredTableStyles.Add(new StructuredTableStyleModel
        {
            Name = "FreeXAuthoredTableStyle",
            AppliesToTables = true,
            AppliesToPivotTables = false,
            NativeXml = """
                <tableStyle xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="FreeXAuthoredTableStyle" pivot="0" table="1" count="2" customTableStyleAttr="removed">
                  <tableStyleElement type="wholeTable" dxfId="0" customElementAttr="removed"><nativeElementChild /></tableStyleElement>
                  <tableStyleElement type="firstRowStripe" dxfId="1" size="1" />
                  <nativeTableStyleChild />
                </tableStyle>
                """
        });

        return workbook;
    }

    private static MemoryStream CreateExcelStylesheetMetadataSourcePackage()
    {
        var stream = Save(CreateStylesheetMetadataWorkbook("ExcelNativeStructuredStyle"));
        AddExcelStylesheetMetadata(stream);
        stream.Position = 0;
        return stream;
    }

    private static Workbook CreateStylesheetMetadataWorkbook(string tableStyleName)
    {
        var workbook = new Workbook("StylesheetMetadataPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(24));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = Range(sheet, 1, 1, 4, 2),
            HasAutoFilter = true,
            StyleName = tableStyleName,
            ShowRowStripes = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Sales")
            }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 4, 2),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(31, 78, 121)
            }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 4, 2),
            Priority = 2,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(226, 240, 217)
            }
        });

        return workbook;
    }

    private static void AddExcelStylesheetMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = LoadPackageXml(archive, "xl/styles.xml");
        var root = stylesXml.Root!;

        ReplaceStylesheetChildInOrder(root, new XElement(
            workbookNs + "dxfs",
            new XAttribute("count", "2"),
            new XElement(
                workbookNs + "dxf",
                new XElement(
                    workbookNs + "font",
                    new XElement(workbookNs + "b"),
                    new XElement(workbookNs + "color", new XAttribute("rgb", "FF1F4E79")))),
            new XElement(
                workbookNs + "dxf",
                new XElement(
                    workbookNs + "fill",
                    new XElement(
                        workbookNs + "patternFill",
                        new XAttribute("patternType", "solid"),
                        new XElement(workbookNs + "fgColor", new XAttribute("rgb", "FFE2F0D9")),
                        new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64")))))));

        ReplaceStylesheetChildInOrder(root, new XElement(
            workbookNs + "tableStyles",
            new XAttribute("count", "2"),
            new XAttribute("defaultTableStyle", "TableStyleMedium9"),
            new XAttribute("defaultPivotStyle", "PivotStyleMedium4"),
            new XElement(
                workbookNs + "tableStyle",
                new XAttribute("name", "ExcelNativeStructuredStyle"),
                new XAttribute("pivot", "0"),
                new XAttribute("table", "1"),
                new XAttribute("count", "2"),
                new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", "wholeTable"),
                    new XAttribute("dxfId", "0")),
                new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", "firstRowStripe"),
                    new XAttribute("dxfId", "1"),
                    new XAttribute("size", "1"))),
            new XElement(
                workbookNs + "tableStyle",
                new XAttribute("name", "ExcelNativePivotStyle"),
                new XAttribute("pivot", "1"),
                new XAttribute("table", "0"),
                new XAttribute("count", "1"),
                new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", "wholeTable"),
                    new XAttribute("dxfId", "0")))));

        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
    }

    private static void SetStylesheetTableStylesInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var stylesXml = LoadPackageXml(archive, "xl/styles.xml");
        var tableStyles = stylesXml.Root!.Element(workbookNs + "tableStyles")!;
        tableStyles.SetAttributeValue("nativeTableStylesAttr", "removed");
        tableStyles.Add(new XElement(freexNs + "tableStylesNativeChild"));

        var tableStyle = tableStyles
            .Elements(workbookNs + "tableStyle")
            .Single(element => element.Attribute("name")?.Value == "ExcelNativeStructuredStyle");
        tableStyle.SetAttributeValue("customTableStyleAttr", "removed");
        tableStyle.Add(new XElement(freexNs + "tableStyleNativeChild"));

        var tableStyleElement = tableStyle.Elements(workbookNs + "tableStyleElement").First();
        tableStyleElement.SetAttributeValue("customElementAttr", "removed");
        tableStyleElement.Add(new XElement(freexNs + "tableStyleElementNativeChild"));
        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
    }

    private static void SetStylesheetDifferentialStylesInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var stylesXml = LoadPackageXml(archive, "xl/styles.xml");
        var differentialStyles = stylesXml.Root!.Element(workbookNs + "dxfs")!;
        differentialStyles.SetAttributeValue("customDxfsAttr", "removed");
        differentialStyles.Add(new XElement(freexNs + "dxfsNativeChild"));

        var dxf = differentialStyles.Elements(workbookNs + "dxf").First();
        dxf.SetAttributeValue("customDxfAttr", "removed");
        dxf.Add(new XElement(freexNs + "dxfNativeChild"));
        dxf.Add(new XElement(
            workbookNs + "extLst",
            new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEX-DXF-VALID-EXT}"))));

        var font = dxf.Element(workbookNs + "font")!;
        font.SetAttributeValue("customFontAttr", "removed");
        font.Add(new XElement(freexNs + "fontNativeChild"));
        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
    }

    private static void AssertStylesheetTableStyleMetadata(
        XElement stylesRoot,
        string expectedTableStyleName,
        string expectedPivotStyleName)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableStyles = stylesRoot.Element(workbookNs + "tableStyles");
        tableStyles.Should().NotBeNull();
        var tableStylesElement = tableStyles!;
        tableStylesElement.Attribute("count")!.Value.Should().Be(tableStylesElement.Elements(workbookNs + "tableStyle").Count().ToString());
        AssertStylesheetTableStylesSanitized(stylesRoot);
        tableStylesElement.Elements(workbookNs + "tableStyle")
            .Where(style => style.Attribute("name")?.Value == expectedTableStyleName)
            .Should()
            .ContainSingle()
            .Which
            .Attribute("table")!
            .Value
            .Should()
            .Be("1");
        tableStylesElement.Elements(workbookNs + "tableStyle")
            .Where(style => style.Attribute("name")?.Value == expectedPivotStyleName)
            .Should()
            .ContainSingle()
            .Which
            .Attribute("pivot")!
            .Value
            .Should()
            .Be("1");
    }

    private static void AssertStylesheetTableStylesSanitized(XElement stylesRoot)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var tableStyles = stylesRoot.Element(workbookNs + "tableStyles")!;
        tableStyles.Attribute("nativeTableStylesAttr").Should().BeNull();
        tableStyles.Element(freexNs + "tableStylesNativeChild").Should().BeNull();
        foreach (var tableStyle in tableStyles.Elements(workbookNs + "tableStyle"))
        {
            tableStyle.Attribute("customTableStyleAttr").Should().BeNull();
            tableStyle.Element(freexNs + "tableStyleNativeChild").Should().BeNull();
            tableStyle.Elements(workbookNs + "tableStyleElement")
                .Should()
                .OnlyContain(element =>
                    element.Attribute("customElementAttr") == null &&
                    !element.Elements().Any());
        }
    }

    private static void AssertStylesheetDifferentialStylesSanitized(XElement stylesRoot)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var differentialStyles = stylesRoot.Element(workbookNs + "dxfs")!;
        differentialStyles.Attribute("customDxfsAttr").Should().BeNull();
        differentialStyles.Element(freexNs + "dxfsNativeChild").Should().BeNull();
        differentialStyles.Attribute("count")!.Value.Should().Be(differentialStyles.Elements(workbookNs + "dxf").Count().ToString());

        var dxf = differentialStyles.Elements(workbookNs + "dxf").First();
        dxf.Attribute("customDxfAttr").Should().BeNull();
        dxf.Element(freexNs + "dxfNativeChild").Should().BeNull();
        dxf.Element(workbookNs + "extLst").Should().NotBeNull();
        var font = dxf.Element(workbookNs + "font");
        font.Should().NotBeNull();
        font!.Attribute("customFontAttr").Should().BeNull();
        font.Element(freexNs + "fontNativeChild").Should().BeNull();
    }

    private static XElement ReadStylesheetChildElement(XElement stylesRoot, string localName)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return stylesRoot.Element(workbookNs + localName)!;
    }

    private static void AssertStylesheetChildOrder(XElement stylesRoot)
    {
        var childNames = stylesRoot.Elements().Select(element => element.Name.LocalName).ToList();
        AssertStylesheetChildPrecedes(childNames, "cellStyles", "dxfs");
        AssertStylesheetChildPrecedes(childNames, "dxfs", "tableStyles");
        AssertStylesheetChildPrecedes(childNames, "tableStyles", "colors");
        AssertStylesheetChildPrecedes(childNames, "tableStyles", "extLst");
    }

    private static void AssertStylesheetChildPrecedes(
        List<string> childNames,
        string firstName,
        string secondName)
    {
        var firstIndex = childNames.IndexOf(firstName);
        var secondIndex = childNames.IndexOf(secondName);
        if (firstIndex >= 0 && secondIndex >= 0)
            firstIndex.Should().BeLessThan(secondIndex);
    }

    private static void ReplaceStylesheetChildInOrder(XElement root, XElement child)
    {
        root.Elements(child.Name).Remove();
        var insertBefore = root.Elements()
            .FirstOrDefault(element => StylesheetChildSchemaOrder(element) > StylesheetChildSchemaOrder(child));
        if (insertBefore is null)
            root.Add(child);
        else
            insertBefore.AddBeforeSelf(child);
    }

    private static int StylesheetChildSchemaOrder(XElement element) =>
        element.Name.LocalName switch
        {
            "numFmts" => 0,
            "fonts" => 1,
            "fills" => 2,
            "borders" => 3,
            "cellStyleXfs" => 4,
            "cellXfs" => 5,
            "cellStyles" => 6,
            "dxfs" => 7,
            "tableStyles" => 8,
            "colors" => 9,
            "extLst" => 100,
            _ => 90
        };
}
