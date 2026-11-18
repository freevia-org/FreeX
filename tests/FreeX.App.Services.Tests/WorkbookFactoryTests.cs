using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFactoryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(300, 255)]
    public void Create_HonorsNormalizedDefaultSheetCount(
        int defaultSheetCount,
        int expectedSheetCount)
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(DefaultSheetCount: defaultSheetCount));

        workbook.SheetCount.Should().Be(expectedSheetCount);
        workbook.Sheets
            .Select(sheet => sheet.Name)
            .Should()
            .Equal(Enumerable.Range(1, expectedSheetCount).Select(index => $"Sheet{index}"));
    }

    [Theory]
    [InlineData(" Aptos ", 14, "Aptos", 14)]
    [InlineData("", 0, "Aptos Narrow", 11)]
    [InlineData("Arial", 500, "Arial", 409)]
    public void Create_HonorsNormalizedDefaultFontOptions(
        string defaultFontName,
        int defaultFontSize,
        string expectedFontName,
        int expectedFontSize)
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 2,
            DefaultFontName: defaultFontName,
            DefaultFontSize: defaultFontSize));

        var defaultStyle = workbook.GetStyle(StyleId.Default);

        workbook.SheetCount.Should().Be(2);
        defaultStyle.FontName.Should().Be(expectedFontName);
        defaultStyle.FontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void Create_WithUserName_AddsFileSharingMetadata()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            UserName: "  Analyst  "));

        workbook.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            UserName = "Analyst",
        });
    }

    [Fact]
    public void Create_WithNoExplicitFont_DefaultStylePinsOfficeDefaultFont()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            DefaultFontName: ""));

        var defaultStyle = workbook.GetStyle(StyleId.Default);
        defaultStyle.FontName.Should().Be("Aptos Narrow");
        defaultStyle.FontScheme.Should().Be(CellFontScheme.None,
            "the default cell font is distinct from the workbook theme body font");
        defaultStyle.ResolveEffectiveFontName(workbook.Theme).Should().Be("Aptos Narrow");
    }

    [Fact]
    public void CreateFromAppOptions_UsesPinnedOfficeDefaultFont()
    {
        var workbook = WorkbookFactory.CreateFromAppOptions(new AppOptions());
        var defaultStyle = workbook.GetStyle(StyleId.Default);

        defaultStyle.FontName.Should().Be("Aptos Narrow");
        defaultStyle.FontScheme.Should().Be(CellFontScheme.None);
        defaultStyle.ResolveEffectiveFontName(workbook.Theme).Should().Be("Aptos Narrow");
    }

    [Fact]
    public void Create_DefaultFontIsSerializedSeparatelyFromThemeBodyFont()
    {
        var workbook = WorkbookFactory.Create();
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        using var styleStream = package.GetEntry("xl/styles.xml")!.Open();
        var styles = XDocument.Load(styleStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var normalStyle = styles.Root!.Element(ns + "cellStyles")!.Elements(ns + "cellStyle")
            .Single(style => (string?)style.Attribute("name") == "Normal");
        var xfId = (int)normalStyle.Attribute("xfId")!;
        var defaultXf = styles.Root.Element(ns + "cellStyleXfs")!.Elements(ns + "xf").ElementAt(xfId);
        var fontId = (int)defaultXf.Attribute("fontId")!;
        var font = styles.Root.Element(ns + "fonts")!.Elements(ns + "font").ElementAt(fontId);

        font.Element(ns + "name")!.Attribute("val")!.Value.Should().Be("Aptos Narrow");
        font.Element(ns + "sz")!.Attribute("val")!.Value.Should().Be("11");
        font.Element(ns + "scheme").Should().BeNull();
        workbook.Theme.MinorFontName.Should().Be("Aptos");
    }

    [Fact]
    public void Create_WithExplicitFont_DefaultStyleHasNoneFontScheme()
    {
        var workbook = WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: 1,
            DefaultFontName: "Arial"));

        var defaultStyle = workbook.GetStyle(StyleId.Default);
        defaultStyle.FontScheme.Should().Be(CellFontScheme.None,
            "when a specific font is explicitly chosen as the default, the font scheme should be None (pinned)");
    }
}
