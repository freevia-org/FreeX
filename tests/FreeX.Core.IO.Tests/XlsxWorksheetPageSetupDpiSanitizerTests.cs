using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that <see cref="XlsxWorksheetPageSetupDpiSanitizer"/> removes the schema-invalid
/// <c>horizontalDpi="0"</c>/<c>verticalDpi="0"</c> that Excel emits on worksheets referencing a
/// printerSettings part, while leaving valid DPI values and unrelated attributes intact.
/// </summary>
public sealed class XlsxWorksheetPageSetupDpiSanitizerTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream BuildPackage(string pageSetupXml)
    {
        var worksheet =
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{WorksheetNs}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheetData/>{pageSetupXml}</worksheet>""";

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(worksheet);
        }

        stream.Position = 0;
        return stream;
    }

    private static XElement? ReadPageSetup(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd()).Root!.Element(WorksheetNs + "pageSetup");
    }

    [Fact]
    public void Sanitize_RemovesZeroDpiAttributes_AndReportsChange()
    {
        using var package = BuildPackage(
            """<pageSetup orientation="portrait" horizontalDpi="0" verticalDpi="0" r:id="rId1"/>""");

        var changed = XlsxWorksheetPageSetupDpiSanitizer.Sanitize(package);

        changed.Should().BeTrue();
        var pageSetup = ReadPageSetup(package)!;
        pageSetup.Attribute("horizontalDpi").Should().BeNull();
        pageSetup.Attribute("verticalDpi").Should().BeNull();
        // Unrelated attributes are preserved.
        pageSetup.Attribute("orientation")!.Value.Should().Be("portrait");
        pageSetup.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))!
            .Value.Should().Be("rId1");
    }

    [Fact]
    public void Sanitize_LeavesValidPositiveDpiUntouched_AndReportsNoChange()
    {
        using var package = BuildPackage(
            """<pageSetup orientation="portrait" horizontalDpi="600" verticalDpi="600"/>""");

        var changed = XlsxWorksheetPageSetupDpiSanitizer.Sanitize(package);

        changed.Should().BeFalse();
        var pageSetup = ReadPageSetup(package)!;
        pageSetup.Attribute("horizontalDpi")!.Value.Should().Be("600");
        pageSetup.Attribute("verticalDpi")!.Value.Should().Be("600");
    }

    [Fact]
    public void Sanitize_RemovesOnlyTheInvalidDpiAttribute()
    {
        using var package = BuildPackage(
            """<pageSetup horizontalDpi="0" verticalDpi="300"/>""");

        var changed = XlsxWorksheetPageSetupDpiSanitizer.Sanitize(package);

        changed.Should().BeTrue();
        var pageSetup = ReadPageSetup(package)!;
        pageSetup.Attribute("horizontalDpi").Should().BeNull();
        pageSetup.Attribute("verticalDpi")!.Value.Should().Be("300");
    }

    [Fact]
    public void Sanitize_NoPageSetup_ReportsNoChange()
    {
        using var package = BuildPackage(string.Empty);

        XlsxWorksheetPageSetupDpiSanitizer.Sanitize(package).Should().BeFalse();
    }
}
