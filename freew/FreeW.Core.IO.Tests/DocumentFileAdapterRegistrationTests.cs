using System;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The registration "contract": flattening the catalog's formats yields one capability tuple per
/// (format name, extension). Several extensions intentionally carry more than one format (e.g. <c>.docx</c>
/// Word vs Strict Open XML; <c>.xml</c> Word XML vs Word 2003 XML; <c>.htm</c>/<c>.html</c> Web Page vs
/// Web Page, Filtered) — the Save dialog disambiguates by the selected filter. Every new format adds one
/// asserted row here — this is the assertion that keeps adding a format a data change.
/// </summary>
public class DocumentFileAdapterRegistrationTests
{
    private static System.Collections.Generic.IReadOnlyList<FileFormatDescriptor> AllFormats() =>
        DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(a => a.Formats).ToList();

    [Theory]
    // name, extension, canOpen, canSave, opensAsTemplate
    [InlineData("Word Document", ".docx", true, true, false)]
    [InlineData("Word Macro-Enabled Document", ".docm", true, true, false)]
    [InlineData("Word Template", ".dotx", true, true, true)]
    [InlineData("Word Macro-Enabled Template", ".dotm", true, true, true)]
    [InlineData("Strict Open XML Document", ".docx", true, true, false)]
    [InlineData("Word XML Document", ".xml", true, true, false)]
    [InlineData("Word 2003 XML Document", ".xml", true, true, false)]
    [InlineData("Rich Text Format", ".rtf", true, true, false)]
    [InlineData("Web Page, Filtered", ".html", true, true, false)]
    [InlineData("Web Page, Filtered", ".htm", true, true, false)]
    [InlineData("Web Page", ".html", true, true, false)]
    [InlineData("Web Page", ".htm", true, true, false)]
    [InlineData("MHTML document", ".mhtml", true, true, false)]
    [InlineData("MHTML document", ".mht", true, true, false)]
    [InlineData("PDF Document", ".pdf", true, false, false)]
    [InlineData("Word 97-2003 Document", ".doc", true, false, false)]
    [InlineData("Word 97-2003 Template", ".dot", true, false, true)]
    [InlineData("OpenDocument Text", ".odt", true, true, false)]
    [InlineData("OpenDocument Text Template", ".ott", true, true, true)]
    [InlineData("Plain text", ".txt", true, true, false)]
    [InlineData("Plain text", ".text", true, true, false)]
    [InlineData("Log file", ".log", true, true, false)]
    public void Catalog_RegistersFormatWithExpectedCapabilities(
        string formatName, string extension, bool canOpen, bool canSave, bool opensAsTemplate)
    {
        var format = AllFormats()
            .Should().ContainSingle(f =>
                f.FormatName == formatName &&
                string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase))
            .Which;

        format.CanOpen.Should().Be(canOpen);
        format.CanSave.Should().Be(canSave);
        format.OpensAsTemplate.Should().Be(opensAsTemplate);
    }

    [Theory]
    // Extensions that intentionally expose multiple writable formats (disambiguated by the Save filter).
    [InlineData(".docx", 2)] // Word Document + Strict Open XML Document
    [InlineData(".xml", 2)]  // Word XML Document + Word 2003 XML Document
    [InlineData(".htm", 2)]  // Web Page + Web Page, Filtered
    [InlineData(".html", 2)] // Web Page + Web Page, Filtered
    public void Catalog_ExposesMultipleSaveFormatsForSharedExtension(string extension, int expectedSaveCount)
    {
        AllFormats()
            .Count(f => f.CanSave && string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expectedSaveCount);
    }

    [Fact]
    public void Catalog_DoesNotRegisterDroppedLegacyFormats()
    {
        var extensions = AllFormats().Select(f => f.Extension.ToLowerInvariant()).ToList();
        extensions.Should().NotContain(".wpd");
        extensions.Should().NotContain(".wps");
        extensions.Should().NotContain(".wri");
    }

    [Fact]
    public void Catalog_AdaptersAreResolvableByExtension()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        DocumentFileFormatResolver.FindOpenAdapter(adapters, "docx", out _).Should().NotBeNull();
        DocumentFileFormatResolver.FindSaveAdapter(adapters, ".txt", out _).Should().NotBeNull();
    }
}
