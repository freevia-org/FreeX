using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class DataBarAdvancedOptionsTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrip_DataBarOptions_Survives()
    {
        var workbook = CreateWorkbookWithAdvancedDataBar();
        using var stream = new MemoryStream();

        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var loaded = new NativeJsonAdapter().Load(stream);

        var rule = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        AssertAdvancedDataBar(rule);
        rule.DataBarShowValue.Should().BeFalse();
        rule.DataBarMinLength.Should().Be(7);
        rule.DataBarMaxLength.Should().Be(88);
        rule.DataBarGradient.Should().BeFalse();
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Number);
        rule.DataBarMinThresholdValue.Should().Be("-10");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Percentile);
        rule.DataBarMaxThresholdValue.Should().Be("90");
    }

    [Fact]
    public void Save_DataBarAxisBorderAndNegativeColors_WritesDataBarPayload()
    {
        var workbook = CreateWorkbookWithAdvancedDataBar();
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(stream);
        var x14DataBar = worksheet.Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("border")?.Value.Should().Be("1");
        x14DataBar.Attribute("axisPosition")?.Value.Should().Be("middle");
        x14DataBar.Element(X14Ns + "axisColor")?.Attribute("rgb")?.Value.Should().Be("FF010203");
        x14DataBar.Element(X14Ns + "negativeFillColor")?.Attribute("rgb")?.Value.Should().Be("FF040506");
        x14DataBar.Element(X14Ns + "negativeBorderColor")?.Attribute("rgb")?.Value.Should().Be("FF070809");
    }

    [Fact]
    public void Save_DataBarAxisOptionsWithGradientFill_WritesX14Payload()
    {
        var workbook = CreateWorkbookWithAdvancedDataBar();
        workbook.GetSheetAt(0).ConditionalFormats.Single().DataBarGradient = true;
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(stream);
        var x14DataBar = worksheet.Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("gradient")?.Value.Should().Be("1");
        x14DataBar.Attribute("border")?.Value.Should().Be("1");
        x14DataBar.Attribute("axisPosition")?.Value.Should().Be("middle");
    }

    [Fact]
    public void Save_DataBarExplicitThresholds_WritesX14CfvoFormulaValues()
    {
        var workbook = CreateWorkbookWithAdvancedDataBar();
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        var thresholds = x14DataBar.Elements(X14Ns + "cfvo").ToArray();
        thresholds.Should().HaveCount(2);
        thresholds[0].Attribute("type")?.Value.Should().Be("num");
        thresholds[0].Element(XmNs + "f")?.Value.Should().Be("-10");
        thresholds[0].Attribute("val").Should().BeNull("x14 cfvo values are stored as xm:f children");
        thresholds[1].Attribute("type")?.Value.Should().Be("percentile");
        thresholds[1].Element(XmNs + "f")?.Value.Should().Be("90");
        thresholds[1].Attribute("val").Should().BeNull("x14 cfvo values are stored as xm:f children");
    }

    [Fact]
    public void Load_DataBarAxisBorderAndNegativeColors_MapsFirstClassProperties()
    {
        using var stream = CreateXlsxWithAdvancedDataBarXml();

        var workbook = new XlsxFileAdapter().Load(stream);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        AssertAdvancedDataBar(rule);
    }

    [Fact]
    public void Load_MainDataBarAxisBorderAndNegativeColors_MapsFirstClassProperties()
    {
        using var stream = CreateXlsxWithMainAdvancedDataBarXml();

        var workbook = new XlsxFileAdapter().Load(stream);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        AssertAdvancedDataBar(rule);
        rule.NativePayloadAttributes.Should().BeNull();
        rule.NativePayloadChildXmls.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_DataBarAdvancedOptions_DoesNotDuplicateNativePayload()
    {
        using var source = CreateXlsxWithAdvancedDataBarXml();

        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(saved);
        var x14DataBar = worksheet.Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attributes("border").Should().ContainSingle();
        x14DataBar.Attributes("axisPosition").Should().ContainSingle();
        x14DataBar.Elements(X14Ns + "axisColor").Should().ContainSingle();
        x14DataBar.Elements(X14Ns + "negativeFillColor").Should().ContainSingle();
        x14DataBar.Elements(X14Ns + "negativeBorderColor").Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedX14DataBarWithExistingIdAndEditedModeledFields_WritesUpdatedX14Payload()
    {
        using var source = CreateXlsxWithAdvancedDataBarXml();
        var workbook = new XlsxFileAdapter().Load(source);
        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarAxisPosition = "none";
        rule.DataBarNegativeFillColor = new RgbColor(9, 10, 11);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(saved);
        var x14Rule = worksheet.Descendants(X14Ns + "cfRule")
            .Single(ruleXml => ruleXml.Attribute("id")?.Value == "{11111111-2222-3333-4444-555555555555}");
        var maybeX14DataBar = x14Rule.Element(X14Ns + "dataBar");
        maybeX14DataBar.Should().NotBeNull();
        var x14DataBar = maybeX14DataBar!;
        x14DataBar.Attribute("axisPosition")?.Value.Should().Be("none");
        x14DataBar.Element(X14Ns + "negativeFillColor")?.Attribute("rgb")?.Value.Should().Be("FF090A0B");
    }

    [Fact]
    public void Save_LoadedX14DataBarWithExistingIdAndClearedModeledFields_DoesNotReplayStalePayload()
    {
        using var source = CreateXlsxWithAdvancedDataBarXml();
        var workbook = new XlsxFileAdapter().Load(source);
        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarGradient = true;
        rule.DataBarBorder = false;
        rule.DataBarAxisPosition = null;
        rule.DataBarAxisColor = null;
        rule.DataBarNegativeFillColor = null;
        rule.DataBarNegativeBorderColor = null;
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("gradient")?.Value.Should().Be("1");
        x14DataBar.Attribute("border").Should().BeNull();
        x14DataBar.Attribute("axisPosition").Should().BeNull();
        x14DataBar.Element(X14Ns + "axisColor").Should().BeNull();
        x14DataBar.Element(X14Ns + "negativeFillColor").Should().BeNull();
        x14DataBar.Element(X14Ns + "negativeBorderColor").Should().BeNull();
    }

    [Fact]
    public void LoadSave_NativeOnlyMainDataBarChildXml_IsPreservedWhenNotModeled()
    {
        using var source = CreateXlsxWithMainNativeOnlyDataBarXml();

        var workbook = new XlsxFileAdapter().Load(source);
        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarAxisColor.Should().BeNull();
        rule.NativePayloadChildXmls.Should().ContainSingle(xml => xml.Contains("axisColor", StringComparison.Ordinal) &&
            xml.Contains("theme=\"1\"", StringComparison.Ordinal));
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var axisColor = XlsxPackageTestHelper.ReadWorksheetXml(saved).Descendants(MainNs + "axisColor").Should().ContainSingle().Subject;
        axisColor.Attribute("theme")?.Value.Should().Be("1");
        axisColor.Attribute("rgb").Should().BeNull();
    }

    [Fact]
    public void LoadSave_NativeOnlyX14DataBarChildXml_IsPreservedWhenGeneratedPayloadIsUpdated()
    {
        using var source = CreateXlsxWithX14NativeOnlyColorDataBarXml();

        var workbook = new XlsxFileAdapter().Load(source);
        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarBorder.Should().BeTrue();
        rule.DataBarAxisColor.Should().BeNull();
        rule.NativePayloadChildXmls.Should().Contain(xml => xml.Contains("axisColor", StringComparison.Ordinal) &&
            xml.Contains("theme=\"1\"", StringComparison.Ordinal));
        rule.NativePayloadChildXmls.Should().Contain(xml => xml.Contains("fillColor", StringComparison.Ordinal) &&
            xml.Contains("theme=\"2\"", StringComparison.Ordinal));
        rule.NativePayloadChildXmls.Should().Contain(xml => xml.Contains("borderColor", StringComparison.Ordinal) &&
            xml.Contains("theme=\"3\"", StringComparison.Ordinal));
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("border")?.Value.Should().Be("1");
        var maybeAxisColor = x14DataBar.Element(X14Ns + "axisColor");
        maybeAxisColor.Should().NotBeNull();
        var axisColor = maybeAxisColor!;
        axisColor.Attribute("theme")?.Value.Should().Be("1");
        axisColor.Attribute("rgb").Should().BeNull();
        x14DataBar.Element(X14Ns + "fillColor")?.Attribute("theme")?.Value.Should().Be("2");
        x14DataBar.Element(X14Ns + "borderColor")?.Attribute("theme")?.Value.Should().Be("3");
    }

    [Fact]
    public void Save_DataBarWithNativeOnlyX14ChildrenAndNoExistingId_WritesFreshX14Payload()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarGradient = true,
            NativePayloadChildXmls =
            [
                """<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />""",
                """<fillColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="2" />"""
            ]
        });
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("gradient")?.Value.Should().Be("1");
        x14DataBar.Element(X14Ns + "axisColor")?.Attribute("theme")?.Value.Should().Be("1");
        x14DataBar.Element(X14Ns + "fillColor")?.Attribute("theme")?.Value.Should().Be("2");
        XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "id").Should().ContainSingle();
    }

    [Fact]
    public void Save_X14DataBarWithNativeFillAndModeledAxisColor_WritesFillColorBeforeAxisColor()
    {
        // CT_DataBar requires the child order cfvo, cfvo, fillColor?, borderColor?, negativeFillColor?,
        // negativeBorderColor?, axisColor?. A preserved native fillColor must be inserted BEFORE the
        // already-modeled axisColor, not appended after it, or Excel flags the file for repair.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarAxisColor = new RgbColor(1, 2, 3),
            NativePayloadChildXmls =
            [
                """<fillColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" rgb="FF112233" />"""
            ]
        });
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        var childNames = x14DataBar.Elements().Select(e => e.Name.LocalName).ToArray();
        var fillIndex = Array.IndexOf(childNames, "fillColor");
        var axisIndex = Array.IndexOf(childNames, "axisColor");
        fillIndex.Should().BeGreaterThan(-1, because: "the native fillColor child must be preserved");
        axisIndex.Should().BeGreaterThan(-1, because: "the modeled axisColor must be written");
        fillIndex.Should().BeLessThan(axisIndex, because: "CT_DataBar requires fillColor before axisColor");
    }

    [Fact]
    public void Save_NativeOnlyDataBarAdvancedPayload_IsPreserved()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            NativePayloadAttributes = new Dictionary<string, string>
            {
                ["border"] = "1",
                ["axisPosition"] = "middle"
            },
            NativePayloadChildXmls =
            [
                """<axisColor xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" rgb="FF010203" />""",
                """<negativeFillColor xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" rgb="FF040506" />""",
                """<negativeBorderColor xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" rgb="FF070809" />"""
            ]
        });
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var dataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(MainNs + "dataBar").Should().ContainSingle().Subject;
        dataBar.Attribute("border")?.Value.Should().Be("1");
        dataBar.Attribute("axisPosition")?.Value.Should().Be("middle");
        dataBar.Element(MainNs + "axisColor")?.Attribute("rgb")?.Value.Should().Be("FF010203");
        dataBar.Element(MainNs + "negativeFillColor")?.Attribute("rgb")?.Value.Should().Be("FF040506");
        dataBar.Element(MainNs + "negativeBorderColor")?.Attribute("rgb")?.Value.Should().Be("FF070809");
    }

    private static Workbook CreateWorkbookWithAdvancedDataBar()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(10, 20, 30),
            DataBarShowValue = false,
            DataBarMinLength = 7,
            DataBarMaxLength = 88,
            DataBarGradient = false,
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "-10",
            DataBarMaxThresholdType = CfThresholdType.Percentile,
            DataBarMaxThresholdValue = "90",
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(1, 2, 3),
            DataBarNegativeFillColor = new RgbColor(4, 5, 6),
            DataBarNegativeBorderColor = new RgbColor(7, 8, 9)
        });
        return workbook;
    }

    private static void AssertAdvancedDataBar(ConditionalFormat rule)
    {
        rule.RuleType.Should().Be(CfRuleType.DataBar);
        rule.DataBarBorder.Should().BeTrue();
        rule.DataBarAxisPosition.Should().Be("middle");
        rule.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
        rule.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
        rule.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));
    }

    private static MemoryStream CreateXlsxWithAdvancedDataBarXml()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            var root = xml.Root!;
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XAttribute("showValue", "0"),
                            new XAttribute("minLength", "7"),
                            new XAttribute("maxLength", "88"),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "num"),
                                new XAttribute("val", "-10")),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "percentile"),
                                new XAttribute("val", "90")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E"))),
                        new XElement(MainNs + "extLst",
                            new XElement(MainNs + "ext",
                                new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                                new XElement(X14Ns + "id", "{11111111-2222-3333-4444-555555555555}"))))));
            root.Add(
                new XElement(MainNs + "extLst",
                    new XElement(MainNs + "ext",
                        new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
                        new XAttribute("uri", "{78C0D931-6437-407d-A8EE-F0AAD7539E65}"),
                        new XElement(X14Ns + "conditionalFormattings",
                            new XElement(X14Ns + "conditionalFormatting",
                                new XAttribute("sqref", "A1:A5"),
                                new XElement(X14Ns + "cfRule",
                                    new XAttribute("type", "dataBar"),
                                    new XAttribute("id", "{11111111-2222-3333-4444-555555555555}"),
                                    new XElement(X14Ns + "dataBar",
                                        new XAttribute("minLength", "7"),
                                        new XAttribute("maxLength", "88"),
                                        new XAttribute("gradient", "0"),
                                        new XAttribute("border", "1"),
                                        new XAttribute("axisPosition", "middle"),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMin")),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMax")),
                                        new XElement(X14Ns + "axisColor", new XAttribute("rgb", "FF010203")),
                                        new XElement(X14Ns + "negativeFillColor", new XAttribute("rgb", "FF040506")),
                                        new XElement(X14Ns + "negativeBorderColor", new XAttribute("rgb", "FF070809")))))))));
        });

        return package;
    }

    private static MemoryStream CreateXlsxWithMainAdvancedDataBarXml()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XAttribute("showValue", "0"),
                            new XAttribute("minLength", "7"),
                            new XAttribute("maxLength", "88"),
                            new XAttribute("border", "1"),
                            new XAttribute("axisPosition", "middle"),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "num"),
                                new XAttribute("val", "-10")),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "percentile"),
                                new XAttribute("val", "90")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E")),
                            new XElement(MainNs + "axisColor", new XAttribute("rgb", "FF010203")),
                            new XElement(MainNs + "negativeFillColor", new XAttribute("rgb", "FF040506")),
                            new XElement(MainNs + "negativeBorderColor", new XAttribute("rgb", "FF070809"))))));
        });

        return package;
    }

    private static MemoryStream CreateXlsxWithMainNativeOnlyDataBarXml()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XAttribute("showValue", "1"),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E")),
                            new XElement(MainNs + "axisColor", new XAttribute("theme", "1"))))));
        });

        return package;
    }

    private static MemoryStream CreateXlsxWithX14NativeOnlyColorDataBarXml()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E"))),
                        new XElement(MainNs + "extLst",
                            new XElement(MainNs + "ext",
                                new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                                new XElement(X14Ns + "id", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"))))));
            xml.Root!.Add(
                new XElement(MainNs + "extLst",
                    new XElement(MainNs + "ext",
                        new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
                        new XAttribute("uri", "{78C0D931-6437-407d-A8EE-F0AAD7539E65}"),
                        new XElement(X14Ns + "conditionalFormattings",
                            new XElement(X14Ns + "conditionalFormatting",
                                new XAttribute("sqref", "A1:A5"),
                                new XElement(X14Ns + "cfRule",
                                    new XAttribute("type", "dataBar"),
                                    new XAttribute("id", "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"),
                                    new XElement(X14Ns + "dataBar",
                                        new XAttribute("minLength", "12"),
                                        new XAttribute("maxLength", "90"),
                                        new XAttribute("gradient", "1"),
                                        new XAttribute("border", "1"),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMin")),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMax")),
                                        new XElement(X14Ns + "axisColor", new XAttribute("theme", "1")),
                                        new XElement(X14Ns + "fillColor", new XAttribute("theme", "2")),
                                        new XElement(X14Ns + "borderColor", new XAttribute("theme", "3")))))))));
        });

        return package;
    }

    // ── DataBar fill theme-color round-trip tests ────────────────────────────

    [Fact]
    public void Load_DataBarThemeColor_ResolvesWorkbookThemeColorAndPreservesSource()
    {
        // A <color theme="4" tint="0.4"/> on a dataBar should resolve to the tinted Accent1
        // AND record the DataBarColorSource so the writer can round-trip theme/tint attributes.
        using var source = CreateXlsxWithThemeColorDataBar();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.DataBar);
        // The resolved color should be the tinted Accent1.
        var expectedTinted = RgbColor.FromCellColor(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4));
        rule.DataBarColor.Should().Be(expectedTinted, because: "theme=4 tint=0.4 resolves to tinted Accent1");
        // Source must carry both theme index and tint.
        rule.DataBarColorSource.Should().Be(new CfColorStopSource(4, 0.4), because: "tint must be recorded for round-trip");
    }

    [Fact]
    public void RoundTrip_DataBarThemeColor_WritesThemeIndexAndTintAttributes()
    {
        // When a dataBar fill color was expressed as a theme reference in the source file,
        // the writer must round-trip the original theme/tint attributes — not flatten to sRGB.
        using var source = CreateXlsxWithThemeColorDataBar();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var dataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "dataBar")
            .Should()
            .ContainSingle()
            .Subject;
        var color = dataBar.Element(MainNs + "color");
        color.Should().NotBeNull(because: "dataBar must emit a <color> element");
        color!.Attribute("theme")?.Value.Should().Be("4", because: "Accent1 is OOXML theme index 4");
        color.Attribute("tint").Should().NotBeNull(because: "tint must be written");
        double.Parse(color.Attribute("tint")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(0.4, 0.0001, because: "tint round-trips");
        color.Attribute("rgb").Should().BeNull(because: "theme color must not be duplicated as rgb");
    }

    [Fact]
    public void Load_DataBarThemeColorNoTint_ResolvesCorrectlyAndPreservesSource()
    {
        // A plain <color theme="4"/> (no tint) dataBar should resolve Accent1 with no tint
        // and record source with Tint=0 for round-trip.
        using var source = CreateXlsxWithThemeColorDataBarNoTint();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarColor.Should().Be(RgbColor.FromCellColor(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1)));
        rule.DataBarColorSource.Should().Be(new CfColorStopSource(4, 0), because: "Accent1 theme index=4, no tint");
    }

    [Fact]
    public void RoundTrip_DataBarThemeColorNoTint_WritesThemeIndexWithoutTintAttribute()
    {
        using var source = CreateXlsxWithThemeColorDataBarNoTint();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var color = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "dataBar")
            .Should().ContainSingle().Subject
            .Element(MainNs + "color");
        color.Should().NotBeNull();
        color!.Attribute("theme")?.Value.Should().Be("4");
        color.Attribute("tint").Should().BeNull(because: "no tint was specified");
        color.Attribute("rgb").Should().BeNull(because: "theme color must not emit rgb");
    }

    [Fact]
    public void RoundTrip_DataBarSRgbColor_StaysRgb()
    {
        // A plain sRGB dataBar color must not be affected by the theme fix — it stays rgb=.
        using var source = CreateXlsxWithMainAdvancedDataBarXml();
        var workbook = new XlsxFileAdapter().Load(source);
        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarColorSource.Should().BeNull(because: "rgb source has no theme reference");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        var color = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "dataBar")
            .Should().ContainSingle().Subject
            .Element(MainNs + "color");
        color.Should().NotBeNull();
        color!.Attribute("rgb").Should().NotBeNull(because: "sRGB color must still emit rgb=");
        color.Attribute("theme").Should().BeNull(because: "sRGB color must not emit theme=");
    }

    [Fact]
    public void Clone_DataBarThemeColorSource_IsCopied()
    {
        // ConditionalFormat.Clone() must preserve DataBarColorSource so paste / copy operations
        // keep the theme reference alive.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarColor = RgbColor.FromCellColor(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4)),
            DataBarColorSource = new CfColorStopSource(4, 0.4)
        };

        var clone = source.Clone(Guid.NewGuid());

        clone.DataBarColorSource.Should().Be(new CfColorStopSource(4, 0.4));
        clone.DataBarColor.Should().Be(source.DataBarColor);
    }

    private static MemoryStream CreateXlsxWithThemeColorDataBar()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color",
                                new XAttribute("theme", "4"),
                                new XAttribute("tint", "0.4"))))));
        });
        return package;
    }

    private static MemoryStream CreateXlsxWithThemeColorDataBarNoTint()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("theme", "4"))))));
        });
        return package;
    }

    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
}
