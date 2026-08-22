using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxInkAnnotationVisibilityTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void LoadWorkbookMetadata_WorkbookPrHidesInk_ReadsWorkbookScopedVisibility()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/workbook.xml", $"""
            <workbook xmlns="{WorkbookNs}">
              <workbookPr showInkAnnotation="0" />
              <sheets />
            </workbook>
            """));

        XlsxWorkbookMetadataReader.LoadWorkbookMetadata(package)
            .ShowInkAnnotations.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_PersistsAndCanReenableInkAnnotationVisibility()
    {
        var workbook = new Workbook("Ink annotation visibility")
        {
            ShowInkAnnotations = false
        };
        workbook.AddSheet("Sheet1");
        var adapter = new XlsxFileAdapter();

        using var hidden = new MemoryStream();
        adapter.Save(workbook, hidden);
        AssertWorkbookInkVisibility(hidden, expectedAttributeValue: "0");

        hidden.Position = 0;
        var loaded = adapter.Load(hidden);
        loaded.ShowInkAnnotations.Should().BeFalse();

        loaded.ShowInkAnnotations = true;
        using var shown = new MemoryStream();
        adapter.Save(loaded, shown);
        AssertWorkbookInkVisibility(shown, expectedAttributeValue: null);

        shown.Position = 0;
        adapter.Load(shown).ShowInkAnnotations.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_SaveLoad_PreservesInkAnnotationVisibility()
    {
        var workbook = new Workbook("Ink annotation visibility")
        {
            ShowInkAnnotations = false
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        adapter.Load(stream).ShowInkAnnotations.Should().BeFalse();
    }

    private static void AssertWorkbookInkVisibility(MemoryStream package, string? expectedAttributeValue)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = XDocument.Load(archive.GetEntry("xl/workbook.xml")!.Open());
        var workbookPr = workbookXml.Root!.Element(XName.Get("workbookPr", WorkbookNs));
        workbookPr.Should().NotBeNull();
        workbookPr!.Attribute("showInkAnnotation")?.Value.Should().Be(expectedAttributeValue);
    }
}
