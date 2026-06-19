using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The open/save dialog filter strings and extension dispatch are a pure function of the registered formats.
/// Ported from the sibling FreeX app's filter-builder test.
/// </summary>
public class DocumentFileDialogFilterBuilderTests
{
    private static IReadOnlyList<IDocumentFileAdapter> Catalog() => DocumentFileAdapterCatalog.CreateDefaultAdapters();

    [Fact]
    public void OpenFilter_LeadsWithAllSupported_AndEndsWithAllFiles()
    {
        var filter = DocumentFileDialogFilterBuilder.BuildOpenFilter(Catalog());

        filter.Should().StartWith("All supported files (");
        filter.Should().EndWith("All files (*.*)|*.*");
        filter.Should().Contain("Word Document (*.docx)|*.docx");
        filter.Should().Contain("Plain text (*.txt)|*.txt");
    }

    [Fact]
    public void SaveFilter_HasNoAllFilesOrAllSupportedRow()
    {
        var filter = DocumentFileDialogFilterBuilder.BuildSaveFilter(Catalog());

        filter.Should().NotContain("All files");
        filter.Should().NotContain("All supported");
        filter.Should().Contain("Word Document (*.docx)|*.docx");
    }

    [Fact]
    public void SaveFilter_ListsEveryWritableFormatInCatalogOrder()
    {
        var filter = DocumentFileDialogFilterBuilder.BuildSaveFilter(Catalog());

        filter.Split('|').Should().Equal(
            "Word Document (*.docx)", "*.docx",
            "Word Macro-Enabled Document (*.docm)", "*.docm",
            "Word Template (*.dotx)", "*.dotx",
            "Word Macro-Enabled Template (*.dotm)", "*.dotm",
            "Word XML Document (*.xml)", "*.xml",
            "Rich Text Format (*.rtf)", "*.rtf",
            "HTML document (*.html)", "*.html",
            "HTML document (*.htm)", "*.htm",
            "MHTML document (*.mhtml)", "*.mhtml",
            "MHTML document (*.mht)", "*.mht",
            "OpenDocument Text (*.odt)", "*.odt",
            "OpenDocument Text Template (*.ott)", "*.ott",
            "Plain text (*.txt)", "*.txt",
            "Plain text (*.text)", "*.text",
            "Log file (*.log)", "*.log");
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData("txt")]
    [InlineData("*.TXT")]
    public void FindSaveFilterIndex_MatchesExtension_DotAndCaseInsensitive(string extension)
    {
        var saveFormats = Catalog().SelectMany(a => a.Formats).Where(f => f.CanSave).ToList();
        var expected = saveFormats.FindIndex(f => DocumentFileFormatResolver.NormalizeExtension(f.Extension) == ".txt") + 1;

        DocumentFileDialogFilterBuilder.FindSaveFilterIndex(Catalog(), extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".zzz")]
    [InlineData("")]
    public void FindSaveFilterIndex_DefaultsToOne_ForUnknownOrEmpty(string extension)
    {
        DocumentFileDialogFilterBuilder.FindSaveFilterIndex(Catalog(), extension).Should().Be(1);
    }

    [Theory]
    [InlineData("docx", ".docx")]
    [InlineData("*.docx", ".docx")]
    [InlineData(".docx", ".docx")]
    [InlineData("", "")]
    public void NormalizeExtension_NormalizesForms(string input, string expected)
    {
        DocumentFileFormatResolver.NormalizeExtension(input).Should().Be(expected);
    }

    [Fact]
    public void Resolver_FindsAdaptersByExtension_AndNullForUnknown()
    {
        var adapters = Catalog();

        DocumentFileFormatResolver.FindOpenAdapter(adapters, ".docx", out var openFormat).Should().NotBeNull();
        openFormat!.Extension.Should().Be(".docx");

        DocumentFileFormatResolver.FindSaveAdapter(adapters, ".log", out _).Should().NotBeNull();

        DocumentFileFormatResolver.FindOpenAdapter(adapters, ".zzz", out var unknown).Should().BeNull();
        unknown.Should().BeNull();
    }
}
