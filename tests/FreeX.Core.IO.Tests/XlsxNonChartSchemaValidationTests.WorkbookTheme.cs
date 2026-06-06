using System.IO;
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
    public void WorkbookThemePackage_ProducesSchemaValidWorkbook()
    {
        using var saved = Save(CreateWorkbookThemeSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertWorkbookThemePackage(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookThemePackage_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorkbookThemeSourceWorkbook());
        var sourceTheme = ReadPackageRootElement(source, "xl/theme/theme1.xml");
        var sourceWorkbookRelationships = ReadPackageRootElement(source, "xl/_rels/workbook.xml.rels");
        var sourceContentTypes = ReadPackageRootElement(source, "[Content_Types].xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.Theme.Name.Should().Be("FreeX Schema Theme");
        workbook.Theme.MajorFontName.Should().Be("FreeX Major");
        workbook.Theme.MinorFontName.Should().Be("FreeX Minor");
        workbook.Theme.EffectsName.Should().Be("FreeX Effects");
        workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorkbookThemePackage(saved);
        ReadPackageRootElement(saved, "xl/theme/theme1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTheme.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/_rels/workbook.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "[Content_Types].xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceContentTypes.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateWorkbookThemeSourceWorkbook()
    {
        var workbook = new Workbook("WorkbookThemePatchSave")
        {
            Theme = WorkbookTheme.Office
                .WithName("FreeX Schema Theme")
                .WithFonts("FreeX Major", "FreeX Minor")
                .WithEffects("FreeX Effects")
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(12, 34, 56))
                .WithColor(WorkbookThemeColorSlot.Hyperlink, new CellColor(5, 99, 193))
        };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("theme"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        return workbook;
    }

    private static void AssertWorkbookThemePackage(Stream stream)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var theme = ReadPackageRootElement(stream, "xl/theme/theme1.xml");
        theme.Name.Should().Be(drawingNs + "theme");
        theme.Attribute("name")!.Value.Should().Be("FreeX Schema Theme");
        var themeElements = theme.Element(drawingNs + "themeElements")!;
        themeElements.Element(drawingNs + "clrScheme")!
            .Element(drawingNs + "accent1")!
            .Element(drawingNs + "srgbClr")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("0C2238");
        themeElements.Element(drawingNs + "fontScheme")!
            .Element(drawingNs + "majorFont")!
            .Element(drawingNs + "latin")!
            .Attribute("typeface")!
            .Value
            .Should()
            .Be("FreeX Major");
        themeElements.Element(drawingNs + "fontScheme")!
            .Element(drawingNs + "minorFont")!
            .Element(drawingNs + "latin")!
            .Attribute("typeface")!
            .Value
            .Should()
            .Be("FreeX Minor");
        themeElements.Element(drawingNs + "fmtScheme")!
            .Attribute("name")!
            .Value
            .Should()
            .Be("FreeX Effects");
    }
}
