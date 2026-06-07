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
    public void ConditionalFormat_ColorScale_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("ColorScaleCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }


    [Fact]
    public void ConditionalFormat_DataBar_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("DataBarCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }


    [Fact]
    public void ConditionalFormat_DataBar_WithX14Extensions_ProducesSchemaValidWorkbook()
    {
        // DataBar border / axis / negative-fill colors require the x14 (2009) dataBar extension, which is
        // emitted as a worksheet <extLst>. extLst is the final CT_Worksheet child, so it must stay last.
        var workbook = new Workbook("DataBarX14Cf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(0, 0, 0),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarNegativeBorderColor = new RgbColor(255, 0, 0),
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }


    [Fact]
    public void BackgroundImage_WithSparklines_KeepsPictureBeforeWorksheetExtensions()
    {
        var workbook = new Workbook("BackgroundSparklineOrder");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "background.png");
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 3),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Line
        });

        var worksheetXml = WorksheetXml(workbook);
        var childNames = worksheetXml.Root!.Elements().Select(element => element.Name.LocalName).ToList();
        childNames.IndexOf("picture").Should().BeLessThan(childNames.IndexOf("extLst"));
        SchemaErrors(workbook).Should().BeEmpty();
    }


    [Fact]
    public void ConditionalFormat_IconSet_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("IconSetCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetThresholds =
            {
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67"),
            },
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }


    [Fact]
    public void ConditionalFormat_CellValue_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("CellValueCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true },
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_NativeRuleAndContainerMetadata_SanitizesInvalidXmlForSchemaValidity()
    {
        var workbook = new Workbook("ConditionalFormatNativeMetadata");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            NativeContainerAttributes = new Dictionary<string, string> { ["customBlockAttr"] = "removed" },
            NativeContainerChildXmls =
            [
                "<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEX-CF-CONTAINER-EXT}\" /></extLst>",
                "<nativeContainerChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ],
            NativeAttributes = new Dictionary<string, string> { ["customAttr"] = "removed" },
            NativeChildXmls =
            [
                "<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEX-CF-RULE-EXT}\" /></extLst>",
                "<nativeRuleChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ],
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "50",
            MinColor = new RgbColor(99, 190, 123),
            MaxColor = new RgbColor(248, 105, 107)
        });

        using var stream = Save(workbook);

        SchemaErrors(stream).Should().BeEmpty();
        var conditionalFormatting = ReadWorksheetChildElement(stream, "conditionalFormatting");
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        conditionalFormatting.Attribute("customBlockAttr").Should().BeNull();
        conditionalFormatting.Element(worksheetNs + "nativeContainerChild").Should().BeNull();
        conditionalFormatting.Element(worksheetNs + "extLst").Should().NotBeNull();
        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.Attribute("customAttr").Should().BeNull();
        rule.Element(worksheetNs + "nativeRuleChild").Should().BeNull();
        rule.Element(worksheetNs + "extLst").Should().NotBeNull();
        rule.Element(worksheetNs + "colorScale").Should().NotBeNull();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithStandardConditionalFormats_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateStandardConditionalFormatsSourceWorkbook());
        var sourceConditionalFormattings = ReadWorksheetChildElements(source, "conditionalFormatting")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToArray();
        sourceConditionalFormattings.Should().HaveCount(6);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElements(saved, "conditionalFormatting")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .Should()
            .Equal(sourceConditionalFormattings);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidConditionalFormatMetadataForSchemaValidity()
    {
        using var source = Save(CreateStandardConditionalFormatsSourceWorkbook());
        SetConditionalFormatInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var conditionalFormatting = ReadWorksheetChildElements(saved, "conditionalFormatting").First();
        AssertConditionalFormatInvalidNativeMetadataSanitized(conditionalFormatting);
    }


    [Fact]
    public void ConditionalFormat_DifferentialStyleWithFontNumberAndFill_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("DifferentialStyleOrderCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                FontColor = new CellColor(255, 255, 255),
                FontName = "Arial",
                FontSize = 12,
                FillColor = new CellColor(31, 78, 121),
                NumberFormat = "yyyy-mm-dd"
            }
        });

        using var stream = Save(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dxf = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "dxfs")!
            .Element(workbookNs + "dxf")!;

        dxf.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder("font", "numFmt", "fill");
        dxf.Element(workbookNs + "font")!
            .Elements()
            .Select(element => element.Name.LocalName)
            .Should()
            .ContainInOrder("b", "i", "strike", "u", "sz", "color", "name");

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_DifferentialStyleNativeMetadata_SanitizesInvalidXmlForSchemaValidity()
    {
        var workbook = new Workbook("DifferentialStyleNativeMetadataCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(192, 0, 0),
                NativeDifferentialAttributes = new Dictionary<string, string> { ["customAttr"] = "removed" },
                NativeDifferentialChildXmls =
                [
                    "<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEX-DXF-NATIVE}\" /></extLst>",
                    "<nativeDxfChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
                ],
                NativeDifferentialElementXmls = new Dictionary<string, string>
                {
                    ["font"] = "<font xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" customFontAttr=\"removed\"><scheme val=\"minor\" /><nativeFontChild xmlns=\"urn:freex:test\" /></font>"
                }
            }
        });

        using var stream = Save(workbook);

        SchemaErrors(stream).Should().BeEmpty();
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var dxf = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "dxfs")!
            .Elements(workbookNs + "dxf")
            .Should()
            .ContainSingle()
            .Subject;
        dxf.Attribute("customAttr").Should().BeNull();
        dxf.Element(workbookNs + "nativeDxfChild").Should().BeNull();
        dxf.Element(workbookNs + "extLst").Should().NotBeNull();
        var font = dxf.Element(workbookNs + "font");
        font.Should().NotBeNull();
        font!.Attribute("customFontAttr").Should().BeNull();
        font.Element(freexNs + "nativeFontChild").Should().BeNull();
        font.Element(workbookNs + "scheme")!.Attribute("val")!.Value.Should().Be("minor");
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithX14DataBarConditionalFormat_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateX14DataBarConditionalFormatSourceWorkbook());
        var sourceConditionalFormatting = ReadWorksheetChildElement(source, "conditionalFormatting");
        var sourceExtensionList = ReadWorksheetChildElement(source, "extLst");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "conditionalFormatting")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceConditionalFormatting.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "extLst")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExtensionList.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateX14DataBarConditionalFormatSourceWorkbook()
    {
        var workbook = new Workbook("ConditionalFormatPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(0, 0, 0),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarNegativeBorderColor = new RgbColor(255, 0, 0),
        });

        return workbook;
    }

    private static Workbook CreateStandardConditionalFormatsSourceWorkbook()
    {
        var workbook = new Workbook("ConditionalFormatStandardPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Max,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            DataBarColor = new RgbColor(91, 155, 213),
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 3, 5, 3),
            Priority = 3,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "4Arrows",
            IconSetShowValue = false,
            IconSetReverse = true,
            IconSetThresholds =
            {
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "25"),
                new CfThresholdModel(CfThresholdType.Percent, "50"),
                new CfThresholdModel(CfThresholdType.Percent, "75"),
            },
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 4, 5, 4),
            Priority = 4,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "10",
            Value2 = "50",
            StopIfTrue = true,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(255, 242, 204)
            },
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 5, 5, 5),
            Priority = 5,
            RuleType = CfRuleType.Formula,
            FormulaText = "E2>20",
            FormatIfTrue = new CellStyle
            {
                Italic = true,
                FontColor = new CellColor(192, 0, 0)
            },
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 6, 5, 6),
            Priority = 6,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(198, 239, 206)
            },
        });

        return workbook;
    }

    private static void SetConditionalFormatInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var conditionalFormatting = worksheetXml.Root!.Elements(worksheetNs + "conditionalFormatting").First();
        conditionalFormatting.SetAttributeValue("customBlockAttr", "removed");
        conditionalFormatting.Add(
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-CF-CONTAINER-EXT}"))),
            new XElement(worksheetNs + "nativeContainerChild"));

        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.SetAttributeValue("customAttr", "removed");
        rule.Add(
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-CF-RULE-EXT}"))),
            new XElement(worksheetNs + "nativeRuleChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertConditionalFormatInvalidNativeMetadataSanitized(XElement conditionalFormatting)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        conditionalFormatting.Attribute("customBlockAttr").Should().BeNull();
        conditionalFormatting.Element(worksheetNs + "nativeContainerChild").Should().BeNull();
        conditionalFormatting.Element(worksheetNs + "extLst").Should().NotBeNull();
        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.Attribute("customAttr").Should().BeNull();
        rule.Element(worksheetNs + "nativeRuleChild").Should().BeNull();
        rule.Element(worksheetNs + "extLst").Should().NotBeNull();
    }

    private static XElement ReadWorksheetChildElement(Stream stream, string localName)
    {
        return ReadWorksheetChildElements(stream, localName).Single();
    }

    private static IReadOnlyList<XElement> ReadWorksheetChildElements(Stream stream, string localName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        return worksheetXml.Root!
            .Elements(worksheetNs + localName)
            .Select(element => new XElement(element))
            .ToList();
    }

}
