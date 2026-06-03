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

}
