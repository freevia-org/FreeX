using System;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The registration "contract": flattening the catalog's formats yields one capability tuple per supported
/// extension. Every new format adds one asserted row here — this is the assertion that keeps adding a format
/// a data change. Mirrors the sibling FreeX app's adapter-registration test.
/// </summary>
public class DocumentFileAdapterRegistrationTests
{
    private static System.Collections.Generic.IReadOnlyList<FileFormatDescriptor> AllFormats() =>
        DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(a => a.Formats).ToList();

    [Theory]
    [InlineData(".docx", true, true, false)]
    [InlineData(".docm", true, true, false)]
    [InlineData(".dotx", true, true, true)]
    [InlineData(".dotm", true, true, true)]
    [InlineData(".xml", true, true, false)]
    [InlineData(".txt", true, true, false)]
    [InlineData(".text", true, true, false)]
    [InlineData(".log", true, true, false)]
    public void Catalog_RegistersFormatWithExpectedCapabilities(string extension, bool canOpen, bool canSave, bool opensAsTemplate)
    {
        var format = AllFormats()
            .Should().ContainSingle(f => string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase))
            .Which;

        format.CanOpen.Should().Be(canOpen);
        format.CanSave.Should().Be(canSave);
        format.OpensAsTemplate.Should().Be(opensAsTemplate);
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
