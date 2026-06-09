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
    private const string ConditionalFormattingExtensionUri = "{FREEX-CF-CONTAINER-EXT}";
    private const string ConditionalFormatRuleExtensionUri = "{FREEX-CF-RULE-EXT}";
    private const string ConditionalFormatColorScalePayloadExtensionUri = "{FREEX-CF-COLORSCALE-PAYLOAD-EXT}";
    private const string ConditionalFormatDataBarPayloadExtensionUri = "{FREEX-CF-DATABAR-PAYLOAD-EXT}";
    private const string ConditionalFormatIconSetPayloadExtensionUri = "{FREEX-CF-ICONSET-PAYLOAD-EXT}";

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
                CreateInvalidExtensionListXml(ConditionalFormattingExtensionUri, "FreeXConditionalFormattingExtension", "customCfExtLstFlag", "customCfExtFlag", "nativeCfExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-CF-CONTAINER-EXTLST}"),
                "<nativeContainerChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ],
            NativeAttributes = new Dictionary<string, string> { ["customAttr"] = "removed" },
            NativeChildXmls =
            [
                CreateInvalidExtensionListXml(ConditionalFormatRuleExtensionUri, "FreeXConditionalFormatRuleExtension", "customCfRuleExtLstFlag", "customCfRuleExtFlag", "nativeCfRuleExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-CF-RULE-EXTLST}"),
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
        AssertExtensionListSanitized(
            conditionalFormatting,
            worksheetNs,
            ConditionalFormattingExtensionUri,
            "FreeXConditionalFormattingExtension",
            "customCfExtLstFlag",
            "customCfExtFlag",
            "nativeCfExtLstChild");
        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.Attribute("customAttr").Should().BeNull();
        rule.Element(worksheetNs + "nativeRuleChild").Should().BeNull();
        AssertExtensionListSanitized(
            rule,
            worksheetNs,
            ConditionalFormatRuleExtensionUri,
            "FreeXConditionalFormatRuleExtension",
            "customCfRuleExtLstFlag",
            "customCfRuleExtFlag",
            "nativeCfRuleExtLstChild");
        rule.Element(worksheetNs + "colorScale").Should().NotBeNull();
    }

    [Fact]
    public void ConditionalFormatPayloadExtensionLists_RemovesInvalidNativeMetadataForSchemaValidity()
    {
        var workbook = new Workbook("ConditionalFormatPayloadExtensionListInvalidSchema");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            NativePayloadChildXmls =
            [
                CreateInvalidExtensionListXml(ConditionalFormatColorScalePayloadExtensionUri, "FreeXColorScalePayloadExtension", "customColorScalePayloadExtLstFlag", "customColorScalePayloadExtFlag", "nativeColorScalePayloadExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-CF-COLORSCALE-PAYLOAD-EXTLST}")
            ],
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
            NativePayloadChildXmls =
            [
                CreateInvalidExtensionListXml(ConditionalFormatDataBarPayloadExtensionUri, "FreeXDataBarPayloadExtension", "customDataBarPayloadExtLstFlag", "customDataBarPayloadExtFlag", "nativeDataBarPayloadExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-CF-DATABAR-PAYLOAD-EXTLST}")
            ],
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 3, 5, 3),
            Priority = 3,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetThresholds =
            {
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67"),
            },
            NativePayloadChildXmls =
            [
                CreateInvalidExtensionListXml(ConditionalFormatIconSetPayloadExtensionUri, "FreeXIconSetPayloadExtension", "customIconSetPayloadExtLstFlag", "customIconSetPayloadExtFlag", "nativeIconSetPayloadExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-CF-ICONSET-PAYLOAD-EXTLST}")
            ],
        });

        using var stream = Save(workbook);

        SchemaErrors(stream).Should().BeEmpty();
        AssertConditionalFormatPayloadExtensionListsRemoved(stream);
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
        AssertStandardConditionalFormatsModel(workbook.GetSheetAt(0));

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertStandardConditionalFormatsModel(reloaded.GetSheetAt(0));
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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).GetCell(8, 8)!.Value.Should().Be(new NumberValue(42));
        AssertStandardConditionalFormatsModel(reloaded.GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidConditionalFormatPayloadExtensionListsForSchemaValidity()
    {
        using var source = Save(CreateStandardConditionalFormatsSourceWorkbook());
        SetConditionalFormatPayloadExtensionListsInvalidNativeMetadata(source);
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
        AssertConditionalFormatPayloadExtensionListsRemoved(saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).GetCell(8, 8)!.Value.Should().Be(new NumberValue(42));
        AssertStandardConditionalFormatsModel(reloaded.GetSheetAt(0));
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
        var dxf = LoadPackageXml(archive, "xl/styles.xml")
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
        var dxf = LoadPackageXml(archive, "xl/styles.xml")
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
        AssertX14DataBarConditionalFormatModel(workbook.GetSheetAt(0));

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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        AssertX14DataBarConditionalFormatModel(reloaded.GetSheetAt(0));
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

    private static void AssertStandardConditionalFormatsModel(Sheet sheet)
    {
        var formats = sheet.ConditionalFormats.ToArray();
        formats.Should().HaveCount(6);

        var colorScale = ConditionalFormatForRange(formats, "A2:A5");
        colorScale.RuleType.Should().Be(CfRuleType.ColorScale);
        colorScale.UseThreeColorScale.Should().BeTrue();
        colorScale.MinThresholdType.Should().Be(CfThresholdType.Min);
        colorScale.MidThresholdType.Should().Be(CfThresholdType.Percentile);
        colorScale.MidThresholdValue.Should().Be("50");
        colorScale.MaxThresholdType.Should().Be(CfThresholdType.Max);

        var dataBar = ConditionalFormatForRange(formats, "B2:B5");
        dataBar.RuleType.Should().Be(CfRuleType.DataBar);
        dataBar.DataBarShowValue.Should().BeFalse();
        dataBar.DataBarMinLength.Should().Be(5);
        dataBar.DataBarMaxLength.Should().Be(95);
        dataBar.DataBarColor.Should().Be(new RgbColor(91, 155, 213));

        var iconSet = ConditionalFormatForRange(formats, "C2:C5");
        iconSet.RuleType.Should().Be(CfRuleType.IconSet);
        iconSet.IconSetStyle.Should().Be("4Arrows");
        iconSet.IconSetShowValue.Should().BeFalse();
        iconSet.IconSetReverse.Should().BeTrue();
        iconSet.IconSetThresholds.Should().Equal(
            new CfThresholdModel(CfThresholdType.Percent, "0"),
            new CfThresholdModel(CfThresholdType.Percent, "25"),
            new CfThresholdModel(CfThresholdType.Percent, "50"),
            new CfThresholdModel(CfThresholdType.Percent, "75"));

        var cellValue = ConditionalFormatForRange(formats, "D2:D5");
        cellValue.RuleType.Should().Be(CfRuleType.CellValue);
        cellValue.Operator.Should().Be(CfOperator.Between);
        cellValue.Value1.Should().Be("10");
        cellValue.Value2.Should().Be("50");
        cellValue.StopIfTrue.Should().BeTrue();
        cellValue.FormatIfTrue.Should().NotBeNull();
        cellValue.FormatIfTrue!.Bold.Should().BeTrue();
        cellValue.FormatIfTrue!.FillColor.Should().Be(new CellColor(255, 242, 204));

        var formula = ConditionalFormatForRange(formats, "E2:E5");
        formula.RuleType.Should().Be(CfRuleType.Formula);
        formula.FormulaText.Should().Be("E2>20");
        formula.FormatIfTrue.Should().NotBeNull();
        formula.FormatIfTrue!.Italic.Should().BeTrue();
        formula.FormatIfTrue!.FontColor.Should().Be(new CellColor(192, 0, 0));

        var top10 = ConditionalFormatForRange(formats, "F2:F5");
        top10.RuleType.Should().Be(CfRuleType.Top10);
        top10.TopBottomRank.Should().Be(2);
        top10.FormatIfTrue.Should().NotBeNull();
        top10.FormatIfTrue!.FillColor.Should().Be(new CellColor(198, 239, 206));
    }

    private static ConditionalFormat ConditionalFormatForRange(
        IReadOnlyCollection<ConditionalFormat> formats,
        string range) =>
        formats.Should().ContainSingle(format => format.AppliesTo.ToString() == range).Subject;

    private static void AssertX14DataBarConditionalFormatModel(Sheet sheet)
    {
        var format = sheet.ConditionalFormats.Should().ContainSingle().Subject;
        format.RuleType.Should().Be(CfRuleType.DataBar);
        format.AppliesTo.ToString().Should().Be("A2:A5");
        format.Priority.Should().Be(1);
        format.DataBarShowValue.Should().BeTrue();
        format.DataBarGradient.Should().BeFalse();
        format.DataBarBorder.Should().BeTrue();
        format.DataBarAxisPosition.Should().Be("middle");
        format.DataBarAxisColor.Should().Be(new RgbColor(0, 0, 0));
        format.DataBarNegativeFillColor.Should().Be(new RgbColor(255, 0, 0));
        format.DataBarNegativeBorderColor.Should().Be(new RgbColor(255, 0, 0));
    }

    private static void SetConditionalFormatInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var conditionalFormatting = worksheetXml.Root!.Elements(worksheetNs + "conditionalFormatting").First();
        conditionalFormatting.SetAttributeValue("customBlockAttr", "removed");
        conditionalFormatting.Elements(worksheetNs + "extLst").Remove();
        conditionalFormatting.Add(
            CreateInvalidExtensionList(worksheetNs, ConditionalFormattingExtensionUri, "FreeXConditionalFormattingExtension", "customCfExtLstFlag", "customCfExtFlag", "nativeCfExtLstChild"),
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-CF-CONTAINER-EXTLST}"))),
            new XElement(worksheetNs + "nativeContainerChild"));

        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.SetAttributeValue("customAttr", "removed");
        rule.Elements(worksheetNs + "extLst").Remove();
        rule.Add(
            CreateInvalidExtensionList(worksheetNs, ConditionalFormatRuleExtensionUri, "FreeXConditionalFormatRuleExtension", "customCfRuleExtLstFlag", "customCfRuleExtFlag", "nativeCfRuleExtLstChild"),
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-CF-RULE-EXTLST}"))),
            new XElement(worksheetNs + "nativeRuleChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void SetConditionalFormatPayloadExtensionListsInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        AddInvalidPayloadExtensionList(
            worksheetXml.Root!.Descendants(worksheetNs + "colorScale").First(),
            worksheetNs,
            ConditionalFormatColorScalePayloadExtensionUri,
            "FreeXColorScalePayloadExtension",
            "customColorScalePayloadExtLstFlag",
            "customColorScalePayloadExtFlag",
            "nativeColorScalePayloadExtLstChild",
            "{FREEX-DUPLICATE-CF-COLORSCALE-PAYLOAD-EXTLST}");
        AddInvalidPayloadExtensionList(
            worksheetXml.Root.Descendants(worksheetNs + "dataBar").First(),
            worksheetNs,
            ConditionalFormatDataBarPayloadExtensionUri,
            "FreeXDataBarPayloadExtension",
            "customDataBarPayloadExtLstFlag",
            "customDataBarPayloadExtFlag",
            "nativeDataBarPayloadExtLstChild",
            "{FREEX-DUPLICATE-CF-DATABAR-PAYLOAD-EXTLST}");
        AddInvalidPayloadExtensionList(
            worksheetXml.Root.Descendants(worksheetNs + "iconSet").First(),
            worksheetNs,
            ConditionalFormatIconSetPayloadExtensionUri,
            "FreeXIconSetPayloadExtension",
            "customIconSetPayloadExtLstFlag",
            "customIconSetPayloadExtFlag",
            "nativeIconSetPayloadExtLstChild",
            "{FREEX-DUPLICATE-CF-ICONSET-PAYLOAD-EXTLST}");

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AddInvalidPayloadExtensionList(
        XElement payload,
        XNamespace worksheetNs,
        string uri,
        string payloadName,
        string listAttributeName,
        string extensionAttributeName,
        string unexpectedChildName,
        string duplicateUri)
    {
        payload.Elements(worksheetNs + "extLst").Remove();
        payload.Add(
            CreateInvalidExtensionList(worksheetNs, uri, payloadName, listAttributeName, extensionAttributeName, unexpectedChildName),
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", duplicateUri))));
    }

    private static void AssertConditionalFormatInvalidNativeMetadataSanitized(XElement conditionalFormatting)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        conditionalFormatting.Attribute("customBlockAttr").Should().BeNull();
        conditionalFormatting.Element(worksheetNs + "nativeContainerChild").Should().BeNull();
        AssertExtensionListSanitized(
            conditionalFormatting,
            worksheetNs,
            ConditionalFormattingExtensionUri,
            "FreeXConditionalFormattingExtension",
            "customCfExtLstFlag",
            "customCfExtFlag",
            "nativeCfExtLstChild");
        var rule = conditionalFormatting.Element(worksheetNs + "cfRule")!;
        rule.Attribute("customAttr").Should().BeNull();
        rule.Element(worksheetNs + "nativeRuleChild").Should().BeNull();
        AssertExtensionListSanitized(
            rule,
            worksheetNs,
            ConditionalFormatRuleExtensionUri,
            "FreeXConditionalFormatRuleExtension",
            "customCfRuleExtLstFlag",
            "customCfRuleExtFlag",
            "nativeCfRuleExtLstChild");
    }

    private static void AssertConditionalFormatPayloadExtensionListsRemoved(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var conditionalFormattings = ReadWorksheetChildElements(stream, "conditionalFormatting");
        AssertConditionalFormatPayloadExtensionListRemoved(conditionalFormattings, worksheetNs, "colorScale");
        AssertConditionalFormatPayloadExtensionListRemoved(conditionalFormattings, worksheetNs, "dataBar");
        AssertConditionalFormatPayloadExtensionListRemoved(conditionalFormattings, worksheetNs, "iconSet");
    }

    private static void AssertConditionalFormatPayloadExtensionListRemoved(
        IReadOnlyList<XElement> conditionalFormattings,
        XNamespace worksheetNs,
        string payloadLocalName)
    {
        var payload = conditionalFormattings
            .SelectMany(element => element.Descendants(worksheetNs + payloadLocalName))
            .Should()
            .ContainSingle()
            .Subject;
        payload.Elements(worksheetNs + "extLst").Should().BeEmpty();
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
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        return worksheetXml.Root!
            .Elements(worksheetNs + localName)
            .Select(element => new XElement(element))
            .ToList();
    }

}
