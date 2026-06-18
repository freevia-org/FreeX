using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies <see cref="XlsxSlicerTimelineMetadataReader"/> parses Excel's <c>showCaption</c> attribute
/// onto <see cref="FreeX.Core.Model.SlicerModel.ShowCaption"/> (default true; "0"/"false" => no caption
/// band). File 02's "Market" slicer carries <c>showCaption="0"</c>, so the renderer must drop its header.
/// </summary>
public sealed class XlsxSlicerTimelineMetadataReaderShowCaptionTests
{
    [Fact]
    public void Load_ParsesShowCaption_FalseForZeroDefaultsTrueWhenAbsent()
    {
        using var package = BuildSlicerPackage();
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var metadata = XlsxSlicerTimelineMetadataReader.Load(archive);

        var noCaption = metadata.Slicers.Should().ContainSingle(s => s.Name == "Market").Subject;
        noCaption.ShowCaption.Should().BeFalse("Market carries showCaption=\"0\"");

        var withCaption = metadata.Slicers.Should().ContainSingle(s => s.Name == "Category").Subject;
        withCaption.ShowCaption.Should().BeTrue("Category omits showCaption, which defaults to true");
    }

    // Minimal package: two slicers in one xl/slicers/slicer1.xml part, each referencing a cache part.
    // No drawing/anchor is needed — the reader parses showCaption straight off the <slicer> element.
    private static MemoryStream BuildSlicerPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/slicers/slicer1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <slicers xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
                  <slicer name="Market" cache="Slicer_Market" caption="Market" columnCount="4" showCaption="0" style="SlicerStyleLight2"/>
                  <slicer name="Category" cache="Slicer_Category" caption="Category" columnCount="2"/>
                </slicers>
                """);

            WriteEntry(archive, "xl/slicerCaches/slicerCache1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <slicerCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" name="Slicer_Market" sourceName="Market"/>
                """);

            WriteEntry(archive, "xl/slicerCaches/slicerCache2.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <slicerCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" name="Slicer_Category" sourceName="Category"/>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
