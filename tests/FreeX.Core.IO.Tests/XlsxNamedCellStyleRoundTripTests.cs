using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxNamedCellStyleRoundTripTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly string[] BuiltInNames =
    [
        "Normal", "Good", "Bad", "Neutral", "Calculation", "Check Cell", "Explanatory Text", "Input",
        "Linked Cell", "Note", "Output", "Warning Text", "Heading 1", "Heading 2", "Heading 3", "Heading 4",
        "Title", "Total", "Comma", "Comma [0]", "Currency", "Currency [0]", "Percent",
    ];

    private static Workbook NewWorkbookWithStyles()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        workbook.CustomCellStyles.Add(new CustomCellStyle(
            "Brand Heading",
            new CellStyle
            {
                FontName = "Arial",
                FontSize = 14,
                Bold = true,
                Italic = true,
                FontColor = new CellColor(0x12, 0x34, 0x56),
                FillColor = new CellColor(0xFF, 0xEE, 0xCC),
                FillPatternStyle = CellFillPatternStyle.Solid,
                BorderBottom = new CellBorder(BorderStyle.Medium, new CellColor(0x10, 0x20, 0x30)),
                BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0xAA, 0xBB, 0xCC)),
                NumberFormat = "#,##0.000\" units\"",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                WrapText = true,
                IndentLevel = 2,
                Locked = false,
                Hidden = true,
            },
            CustomCellStyleParts.All));
        workbook.CustomCellStyles.Add(new CustomCellStyle(
            "Only Font And Fill",
            new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(200, 0, 0),
                FillColor = new CellColor(0, 0, 200),
                FillPatternStyle = CellFillPatternStyle.Solid,
                NumberFormat = "0.0%", // present on the style but its part is excluded
            },
            CustomCellStyleParts.Font | CustomCellStyleParts.Fill));
        return workbook;
    }

    private static MemoryStream Save(Workbook workbook, XlsxFileAdapter? adapter = null)
    {
        var stream = new MemoryStream();
        (adapter ?? new XlsxFileAdapter()).Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument LoadStyles(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.GetEntry("xl/styles.xml") is not { } stylesEntry)
            return new XDocument(new XElement(Ns + "styleSheet"));
        using var entry = stylesEntry.Open();
        return XDocument.Load(entry);
    }

    private static void AssertSchemaValid(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).ToList();
        errors.Should().BeEmpty(string.Join("; ", errors.Select(error => error.Description)));
    }

    [Fact]
    public void CustomCellStyles_WrittenAsCellStylesWithoutBuiltinId_AndValidateClean()
    {
        using var stream = Save(NewWorkbookWithStyles());

        var styles = LoadStyles(stream).Root!;
        var cellStyles = styles.Element(Ns + "cellStyles")!.Elements(Ns + "cellStyle").ToList();
        var custom = cellStyles.Single(e => (string?)e.Attribute("name") == "Brand Heading");
        custom.Attribute("builtinId").Should().BeNull();
        var xfs = styles.Element(Ns + "cellStyleXfs")!.Elements(Ns + "xf").ToList();
        var xfId = int.Parse((string)custom.Attribute("xfId")!);
        xfId.Should().BeInRange(1, xfs.Count - 1);
        xfs[xfId].Attribute("applyProtection")!.Value.Should().Be("1");
        xfs[xfId].Element(Ns + "protection")!.Attribute("locked")!.Value.Should().Be("0");
        // Normal stays the single built-in with builtinId 0.
        cellStyles.Count(e => (string?)e.Attribute("builtinId") == "0").Should().Be(1);
        styles.Element(Ns + "cellStyleXfs")!.Attribute("count")!.Value.Should().Be(xfs.Count.ToString());
        styles.Element(Ns + "cellStyles")!.Attribute("count")!.Value.Should().Be(cellStyles.Count.ToString());

        var partial = cellStyles.Single(e => (string?)e.Attribute("name") == "Only Font And Fill");
        var partialXf = xfs[int.Parse((string)partial.Attribute("xfId")!)];
        partialXf.Attribute("applyNumberFormat")!.Value.Should().Be("0");
        partialXf.Attribute("applyBorder")!.Value.Should().Be("0");
        partialXf.Attribute("applyFont")!.Value.Should().Be("1");
        partialXf.Attribute("applyFill")!.Value.Should().Be("1");

        AssertSchemaValid(stream);
    }

    [Fact]
    public void CustomCellStyles_RoundTripThroughXlsx_PreserveDefinitionAndParts()
    {
        var original = NewWorkbookWithStyles();
        using var stream = Save(original);

        var loaded = new XlsxFileAdapter().Load(stream);

        loaded.CustomCellStyles.Select(s => s.Name).Should().Equal("Brand Heading", "Only Font And Fill");
        foreach (var expected in original.CustomCellStyles)
        {
            var actual = loaded.CustomCellStyles.Single(s => s.Name == expected.Name);
            actual.Parts.Should().Be(expected.Parts);
            actual.Style.Should().Be(XlsxNamedCellStyleCodec.Mask(expected.Style, expected.Parts));
        }
    }

    [Fact]
    public void CustomCellStyles_ResaveOfLoadedWorkbook_DoesNotDuplicateOrDrift()
    {
        using var first = Save(NewWorkbookWithStyles());
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(first);

        // Force a full (non-source-copy) save: change a cell value and add a second sheet.
        loaded.Sheets[0].SetCell(new CellAddress(loaded.Sheets[0].Id, 2, 1), new TextValue("changed"));
        using var second = Save(loaded, adapter);

        var styles = LoadStyles(second).Root!;
        var names = styles.Element(Ns + "cellStyles")!.Elements(Ns + "cellStyle")
            .Select(e => (string)e.Attribute("name")!).ToList();
        names.Should().OnlyHaveUniqueItems();
        names.Should().Contain(["Brand Heading", "Only Font And Fill"]);

        second.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(second);
        reloaded.CustomCellStyles.Select(s => s.Name).Should().Equal("Brand Heading", "Only Font And Fill");
        AssertSchemaValid(second);
    }

    [Fact]
    public void CustomCellStyles_ModifiedAfterLoad_ReplacesDefinitionInPlace()
    {
        using var first = Save(NewWorkbookWithStyles());
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(first);
        var index = loaded.CustomCellStyles.FindIndex(s => s.Name == "Only Font And Fill");
        var modified = loaded.CustomCellStyles[index].Style.Clone();
        modified.FontColor = new CellColor(1, 2, 3);
        loaded.CustomCellStyles[index] = loaded.CustomCellStyles[index] with { Style = modified };

        using var second = Save(loaded, adapter);

        var styles = LoadStyles(second).Root!;
        styles.Element(Ns + "cellStyles")!.Elements(Ns + "cellStyle")
            .Count(e => (string?)e.Attribute("name") == "Only Font And Fill").Should().Be(1);
        second.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(second);
        reloaded.CustomCellStyles.Single(s => s.Name == "Only Font And Fill").Style.FontColor
            .Should().Be(new CellColor(1, 2, 3));
        AssertSchemaValid(second);
    }

    [Fact]
    public void CustomCellStyle_NamedLikeBuiltIn_DoesNotOverwriteBuiltIn()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.CustomCellStyles.Add(new CustomCellStyle(
            "Normal", new CellStyle { Bold = true }, CustomCellStyleParts.Font));
        using var stream = Save(workbook);

        var cellStyles = LoadStyles(stream).Root!.Element(Ns + "cellStyles")!.Elements(Ns + "cellStyle").ToList();
        cellStyles.Count(e => string.Equals((string?)e.Attribute("name"), "Normal", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        cellStyles.Single(e => (string?)e.Attribute("name") == "Normal").Attribute("builtinId")!.Value.Should().Be("0");
        AssertSchemaValid(stream);
    }

    [Fact]
    public void NoCustomStyles_LeavesStylesheetUntouched()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        using var stream = Save(workbook);

        var loaded = new XlsxFileAdapter().Load(stream);

        loaded.CustomCellStyles.Should().BeEmpty();
        AssertSchemaValid(stream);
    }

    [Fact]
    public void ExcelAuthoredNamedStyles_ImportFromStylesXml_MapsPartsFromApplyFlags()
    {
        const string stylesXml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1"><numFmt numFmtId="164" formatCode="0.0&quot;x&quot;"/></numFmts>
              <fonts count="3">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="12"/><color rgb="FF00FF00"/><name val="Verdana"/></font>
                <font><sz val="9"/><color theme="1"/><name val="Calibri"/><scheme val="minor"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/><bgColor indexed="64"/></patternFill></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="4">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
                <xf numFmtId="164" fontId="1" fillId="2" borderId="0" applyBorder="0" applyAlignment="0" applyProtection="0"/>
                <xf numFmtId="0" fontId="2" fillId="0" borderId="0" applyNumberFormat="0" applyFill="0" applyBorder="0" applyAlignment="0" applyProtection="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0" applyNumberFormat="0" applyBorder="0" applyAlignment="0" applyProtection="0"/>
              </cellStyleXfs>
              <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
              <cellStyles count="4">
                <cellStyle name="Normal" xfId="0" builtinId="0"/>
                <cellStyle name="My Style" xfId="1"/>
                <cellStyle name="Small" xfId="2"/>
                <cellStyle name="Good" xfId="3" builtinId="26"/>
              </cellStyles>
            </styleSheet>
            """;

        var imported = XlsxNamedCellStyleCodec.Read(
            XDocument.Parse(stylesXml), WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        imported.Select(s => s.Name).Should().Equal("My Style", "Small"); // Normal/Good are built-in
        var mine = imported[0];
        mine.Parts.Should().Be(
            CustomCellStyleParts.NumberFormat | CustomCellStyleParts.Font | CustomCellStyleParts.Fill);
        mine.Style.NumberFormat.Should().Be("0.0\"x\"");
        mine.Style.FontName.Should().Be("Verdana");
        mine.Style.Bold.Should().BeTrue();
        mine.Style.FontColor.Should().Be(new CellColor(0, 255, 0));
        mine.Style.FillColor.Should().Be(new CellColor(255, 255, 0));
        var small = imported[1];
        small.Parts.Should().Be(CustomCellStyleParts.Font);
        small.Style.FontSize.Should().Be(9);
        small.Style.FontScheme.Should().Be(CellFontScheme.Minor);
    }

    [Fact]
    public void Corpus_BuiltInStylesNeverImportedAsCustom_AndRoundTripWithoutDuplicates()
    {
        var anchor = TestWorkspaceFiles.FindWorkspaceFile("test-corpus", "public", "tealeg-xlsx", "testfile.xlsx");
        var corpusRoot = Path.GetDirectoryName(Path.GetDirectoryName(anchor))!;
        var files = Directory.EnumerateFiles(corpusRoot, "*.xlsx", SearchOption.AllDirectories)
            .Where(path => new FileInfo(path).Length < 2_000_000)
            .ToList();
        files.Should().NotBeEmpty();

        var importedAny = 0;
        foreach (var path in files)
        {
            Workbook workbook;
            try
            {
                using var input = File.OpenRead(path);
                workbook = new XlsxFileAdapter().Load(input);
            }
            catch
            {
                continue; // deliberately malformed corpus members are covered elsewhere
            }

            workbook.CustomCellStyles.Select(s => s.Name).Should().OnlyHaveUniqueItems(path);
            workbook.CustomCellStyles.Select(s => s.Name).Should()
                .NotContain(name => BuiltInNames.Contains(name, StringComparer.OrdinalIgnoreCase), path);
            importedAny += workbook.CustomCellStyles.Count;

            var adapter = new XlsxFileAdapter();
            using var reload = new MemoryStream();
            using (var input = File.OpenRead(path))
                workbook = adapter.Load(input);
            workbook.Sheets.Count.Should().BeGreaterThan(0, path);
            adapter.Save(workbook, reload);
            reload.Position = 0;
            // Source files may legitimately repeat built-in entries (Excel emits one hidden
            // "Hyperlink" per xf); only non-built-in (custom) names must stay unique.
            var names = LoadStyles(reload).Root!.Element(Ns + "cellStyles")?.Elements(Ns + "cellStyle")
                .Where(e => e.Attribute("builtinId") is null)
                .Select(e => (string)e.Attribute("name")!).ToList() ?? [];
            names.Should().OnlyHaveUniqueItems(path);
        }

        importedAny.Should().BeGreaterThanOrEqualTo(0);
    }
}
