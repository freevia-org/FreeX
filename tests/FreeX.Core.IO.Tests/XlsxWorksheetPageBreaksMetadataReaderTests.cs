using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageBreaksMetadataReaderTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Read_ReturnsNullWhenOnlyModeledCountAttributeExists()
    {
        var pageBreaks = new XElement(
            WorksheetNs + "rowBreaks",
            new XAttribute("count", "1"),
            new XElement(WorksheetNs + "brk", new XAttribute("id", "3")));

        XlsxWorksheetPageBreaksMetadataReader.Read(pageBreaks, maxBreakId: 100)
            .Should()
            .BeNull();
    }

    [Fact]
    public void Read_CapturesContainerAndBreakNativeAttributes()
    {
        var pageBreaks = new XElement(
            WorksheetNs + "rowBreaks",
            new XAttribute("count", "1"),
            new XAttribute("manualBreakCount", "1"),
            new XAttribute("nativeContainer", "kept"),
            new XElement(
                WorksheetNs + "brk",
                new XAttribute("id", "12"),
                new XAttribute("man", "1"),
                new XAttribute("nativeBreak", "kept")));

        var metadata = XlsxWorksheetPageBreaksMetadataReader.Read(pageBreaks, maxBreakId: 100);

        metadata.Should().NotBeNull();
        metadata!.NativeAttributes.Should().Contain("manualBreakCount", "1");
        metadata.NativeAttributes.Should().Contain("nativeContainer", "kept");
        metadata.NativeAttributes.Should().NotContainKey("count");
        metadata.BreakNativeAttributes.Should().ContainKey(12);
        metadata.BreakNativeAttributes[12].Should().Contain("man", "1");
        metadata.BreakNativeAttributes[12].Should().Contain("nativeBreak", "kept");
        metadata.BreakNativeAttributes[12].Should().NotContainKey("id");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("101")]
    [InlineData("not-a-number")]
    public void Read_SkipsUnsupportedBreakIds(string id)
    {
        var pageBreaks = new XElement(
            WorksheetNs + "rowBreaks",
            new XElement(
                WorksheetNs + "brk",
                new XAttribute("id", id),
                new XAttribute("nativeBreak", "skip")));

        XlsxWorksheetPageBreaksMetadataReader.Read(pageBreaks, maxBreakId: 100)
            .Should()
            .BeNull();
    }
}
