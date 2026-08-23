using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxAutoFilterXmlCodecTests
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void WriteColorFilter_PreservesRawPrecedenceDefaultsNamespaceAndNativeAttributes()
    {
        XNamespace strictNs = "http://purl.oclc.org/ooxml/spreadsheetml/main";
        XNamespace nativeNs = "urn:freex:test";
        var model = new WorksheetAutoFilterColorFilterModel(
            DifferentialFormatId: 7,
            CellColor: true,
            DifferentialFormatIdRaw: "raw-dxf",
            CellColorRaw: "raw-cell",
            NativeAttributes: new Dictionary<string, string>
            {
                ["dxfId"] = "native-must-not-win",
                [(nativeNs + "flag").ToString()] = "keep",
            });

        var element = XlsxAutoFilterXmlCodec.WriteColorFilter(model, strictNs, allocatedDxfId: 11);

        element.Name.Should().Be(strictNs + "colorFilter");
        element.Attribute("dxfId")!.Value.Should().Be("raw-dxf");
        element.Attribute("cellColor")!.Value.Should().Be("raw-cell");
        element.Attribute(nativeNs + "flag")!.Value.Should().Be("keep");
        element.Attributes("dxfId").Should().ContainSingle();
    }

    [Theory]
    [InlineData(null, true, null, null)]
    [InlineData(null, false, null, "0")]
    [InlineData(4, true, "4", null)]
    public void WriteColorFilter_UsesModeledValuesAndBooleanOmission(
        int? dxfId,
        bool cellColor,
        string? expectedDxfId,
        string? expectedCellColor)
    {
        var element = XlsxAutoFilterXmlCodec.WriteColorFilter(
            new WorksheetAutoFilterColorFilterModel(dxfId, cellColor),
            SpreadsheetNs);

        element.Attribute("dxfId")?.Value.Should().Be(expectedDxfId);
        element.Attribute("cellColor")?.Value.Should().Be(expectedCellColor);
    }

    [Fact]
    public void WriteColorFilter_UsesAllocatedDxfOnlyWhenModelHasNoId()
    {
        var allocated = XlsxAutoFilterXmlCodec.WriteColorFilter(
            new WorksheetAutoFilterColorFilterModel(),
            SpreadsheetNs,
            allocatedDxfId: 13);
        var modeled = XlsxAutoFilterXmlCodec.WriteColorFilter(
            new WorksheetAutoFilterColorFilterModel(DifferentialFormatId: 5),
            SpreadsheetNs,
            allocatedDxfId: 13);

        allocated.Attribute("dxfId")!.Value.Should().Be("13");
        modeled.Attribute("dxfId")!.Value.Should().Be("5");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadColorFilter_PreservesLexicalValuesAndResolvesSelectedDxfColor(bool cellColor)
    {
        XNamespace nativeNs = "urn:freex:test";
        var fill = new CellColor(10, 20, 30);
        var font = new CellColor(40, 50, 60);
        var styles = new[]
        {
            new CellStyle(),
            new CellStyle { FillColor = fill, FontColor = font },
        };
        var element = new XElement(
            SpreadsheetNs + "colorFilter",
            new XAttribute("dxfId", "01"),
            new XAttribute("cellColor", cellColor ? "1" : "0"),
            new XAttribute(nativeNs + "flag", "keep"));

        var model = XlsxAutoFilterXmlCodec.ReadColorFilter(element, styles)!;

        model.DifferentialFormatId.Should().Be(1);
        model.DifferentialFormatIdRaw.Should().Be("01");
        model.CellColor.Should().Be(cellColor);
        model.CellColorRaw.Should().Be(cellColor ? "1" : "0");
        model.Color.Should().Be(cellColor ? fill : font);
        model.NativeAttributes.Should().Contain((nativeNs + "flag").ToString(), "keep");
    }

    [Fact]
    public void ReadColorFilter_DefaultsCellColorAndLeavesTableStyleResolutionOptional()
    {
        var element = new XElement(SpreadsheetNs + "colorFilter", new XAttribute("dxfId", "8"));

        var model = XlsxAutoFilterXmlCodec.ReadColorFilter(element)!;

        model.CellColor.Should().BeTrue();
        model.CellColorRaw.Should().BeNull();
        model.Color.Should().BeNull();
        XlsxAutoFilterXmlCodec.ReadColorFilter(null).Should().BeNull();
    }

    [Fact]
    public void WriteAndReadDateGroupItem_PreserveAllRawAndNativeAttributes()
    {
        XNamespace nativeNs = "urn:freex:test";
        var source = new WorksheetAutoFilterDateGroupItemModel(
            Year: 2024,
            Month: 2,
            Day: 3,
            Hour: 4,
            Minute: 5,
            Second: 6,
            DateTimeGrouping: "month",
            YearRaw: "02024",
            MonthRaw: "02",
            DayRaw: "03",
            HourRaw: "04",
            MinuteRaw: "05",
            SecondRaw: "06",
            NativeAttributes: new Dictionary<string, string> { [(nativeNs + "flag").ToString()] = "keep" });

        var element = XlsxAutoFilterXmlCodec.WriteDateGroupItem(source, SpreadsheetNs);
        var roundTripped = XlsxAutoFilterXmlCodec.ReadDateGroupItem(element);

        element.ToString(SaveOptions.DisableFormatting).Should().Be(
            "<dateGroupItem year=\"02024\" month=\"02\" day=\"03\" hour=\"04\" minute=\"05\" second=\"06\" dateTimeGrouping=\"month\" p1:flag=\"keep\" xmlns:p1=\"urn:freex:test\" xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");
        roundTripped.Should().BeEquivalentTo(source);
    }

    [Fact]
    public void DateGroupCodec_OmitsAbsentAndWhitespaceGroupingButRetainsInvalidRawValues()
    {
        var element = XlsxAutoFilterXmlCodec.WriteDateGroupItem(
            new WorksheetAutoFilterDateGroupItemModel(Year: 2025, MonthRaw: "not-a-number", DateTimeGrouping: "  "),
            SpreadsheetNs);

        element.Attribute("year")!.Value.Should().Be("2025");
        element.Attribute("month")!.Value.Should().Be("not-a-number");
        element.Attribute("day").Should().BeNull();
        element.Attribute("dateTimeGrouping").Should().BeNull();

        var model = XlsxAutoFilterXmlCodec.ReadDateGroupItem(element);
        model.Year.Should().Be(2025);
        model.Month.Should().BeNull();
        model.MonthRaw.Should().Be("not-a-number");
    }

    [Fact]
    public void AutoFilterCallSites_UseSharedCodec()
    {
        foreach (var file in new[]
                 {
                     "XlsxWorksheetAutoFilterXmlMapper.cs",
                     "XlsxStructuredTableWriter.cs",
                     "XlsxStructuredTableMetadataReader.cs",
                 })
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxAutoFilterXmlCodec.");
            source.Should().NotContain("private static XElement ToColorFilterXml");
            source.Should().NotContain("private static XElement ToDateGroupItemXml");
            source.Should().NotContain("private static WorksheetAutoFilterColorFilterModel? ReadColorFilter");
            source.Should().NotContain("private static WorksheetAutoFilterDateGroupItemModel ReadDateGroupItem");
        }
    }
}

public sealed class XlsxCoreIoLeafCodecTests
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void RelationshipIdReader_PreservesOrderDuplicatesAndExactNamespaces()
    {
        using var package = CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                       xmlns:f="urn:foreign">
              <tablePart r:id="rId2" />
              <f:tablePart r:id="foreign" />
              <tablePart id="unqualified" />
              <tablePart r:id=" " />
              <tablePart r:id="rId2" />
              <tablePart r:id="rId1" />
            </worksheet>
            """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var ids = XlsxWorksheetRelationshipIdReader.ReadAll(
            archive.GetEntry("xl/worksheets/sheet1.xml")!,
            SpreadsheetNs + "tablePart",
            RelNs + "id");

        ids.Should().Equal("rId2", "rId2", "rId1");
    }

    [Fact]
    public void ShallowElementMaterializer_PreservesNamespacesAttributesAndReaderPosition()
    {
        const string xml = "<root xmlns=\"urn:root\" xmlns:p=\"urn:prefix\" plain=\"v\" p:value=\"x\"><child /></root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent();

        var element = XmlReaderElementMaterializer.CreateShallowElement(reader);

        element.Name.Should().Be(XName.Get("root", "urn:root"));
        element.Attribute("plain")!.Value.Should().Be("v");
        element.Attribute(XName.Get("value", "urn:prefix"))!.Value.Should().Be("x");
        element.GetDefaultNamespace().NamespaceName.Should().Be("urn:root");
        element.GetNamespaceOfPrefix("p")!.NamespaceName.Should().Be("urn:prefix");
        element.HasElements.Should().BeFalse();
        reader.NodeType.Should().Be(XmlNodeType.Element);
        reader.LocalName.Should().Be("root");
    }

    public static TheoryData<string?, CellFillPatternStyle> FillPatternCases => new()
    {
        { "solid", CellFillPatternStyle.Solid },
        { "gray0625", CellFillPatternStyle.Gray0625 },
        { "gray125", CellFillPatternStyle.Gray125 },
        { "lightGray", CellFillPatternStyle.LightGray },
        { "mediumGray", CellFillPatternStyle.MediumGray },
        { "darkGray", CellFillPatternStyle.DarkGray },
        { "lightHorizontal", CellFillPatternStyle.LightHorizontal },
        { "lightVertical", CellFillPatternStyle.LightVertical },
        { "lightDown", CellFillPatternStyle.LightDown },
        { "lightUp", CellFillPatternStyle.LightUp },
        { "lightGrid", CellFillPatternStyle.LightGrid },
        { "lightTrellis", CellFillPatternStyle.LightTrellis },
        { "darkHorizontal", CellFillPatternStyle.DarkHorizontal },
        { "darkVertical", CellFillPatternStyle.DarkVertical },
        { "darkDown", CellFillPatternStyle.DarkDown },
        { "darkUp", CellFillPatternStyle.DarkUp },
        { "darkGrid", CellFillPatternStyle.DarkGrid },
        { "darkTrellis", CellFillPatternStyle.DarkTrellis },
        { null, CellFillPatternStyle.None },
        { "Solid", CellFillPatternStyle.None },
        { "unknown", CellFillPatternStyle.None },
    };

    [Theory]
    [MemberData(nameof(FillPatternCases))]
    public void FillPatternCodec_PreservesExactCaseSensitiveTokenMap(string? token, CellFillPatternStyle expected) =>
        XlsxFillPatternCodec.FromToken(token).Should().Be(expected);

    [Theory]
    [InlineData("Print_Area", "PrintArea")]
    [InlineData("_xlnm.Print_Area", "PrintArea")]
    [InlineData(" _XLNM.PRINT_TITLES ", "PrintTitles")]
    [InlineData("print_titles", "PrintTitles")]
    public void PrintSettingNameClassifier_AcceptsLegacyAndPrefixedNames(string name, string expected)
    {
        XlsxPrintSettingNameClassifier.TryClassify(name, out var actual).Should().BeTrue();
        actual.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("_xlnm._xlnm.Print_Area")]
    [InlineData("Print_Area_extra")]
    [InlineData("_FilterDatabase")]
    public void PrintSettingNameClassifier_RejectsOtherNames(string? name) =>
        XlsxPrintSettingNameClassifier.TryClassify(name, out _).Should().BeFalse();

    [Fact]
    public void ExistingDrawingPathResolver_UsesWorksheetMarkerAndRelationshipTarget()
    {
        using var package = CreatePackage(
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <drawing r:id="rIdDrawing" />
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="riddrawing" Type="drawing" Target="../drawings/drawing7.xml" />
                </Relationships>
                """),
            ("xl/drawings/drawing7.xml", "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" />"));
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var path = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
            archive,
            "xl/worksheets/sheet1.xml",
            SpreadsheetNs,
            RelNs,
            PackageRelNs);

        path.Should().Be("xl/drawings/drawing7.xml");
    }

    [Fact]
    public void LeafCallSites_UseSharedMechanics()
    {
        foreach (var file in new[] { "XlsxPivotTableReader.cs", "XlsxStructuredTableMetadataReader.cs" })
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XlsxWorksheetRelationshipIdReader.ReadAll");

        foreach (var file in new[] { "XlsxFileAdapter.SheetXmlLayout.cs", "XlsxWorksheetHeaderNormalization.cs" })
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XmlReaderElementMaterializer.CreateShallowElement");

        foreach (var file in new[] { "XlsxDifferentialStyleReader.cs", "XlsxStructuredTableStyleMetadataReader.cs" })
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XlsxFillPatternCodec.FromToken");

        foreach (var file in new[] { "XlsxFileAdapter.SourcePackageSnapshot.cs", "XlsxWorkbookMetadataPreserver.cs" })
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XlsxPrintSettingNameClassifier.TryClassify");

        foreach (var file in new[] { "XlsxSourceDrawingGeometryRewriter.cs", "XlsxWorksheetDrawingZOrderRewriter.cs" })
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath");
            source.Should().NotContain("private static string? ResolveWorksheetDrawingPath");
        }
    }

    private static MemoryStream CreatePackage(params (string Path, string Xml)[] entries)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(xml);
            }
        }

        package.Position = 0;
        return package;
    }
}
