using System.Linq;
using Avalonia.Platform.Storage;
using FreeW.App.Avalonia;
using FreeW.Core.IO;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Guards that the shell's Open/Save dialog filters are derived from the file-format catalog rather than a
/// hard-coded <c>.docx</c> entry, so adding an adapter to the catalog automatically widens the dialogs.
/// Pure data transform — no UI thread.
/// </summary>
public class DocumentFilePickerTypesTests
{
    [Fact]
    public void Open_types_include_more_than_just_docx()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();

        var types = DocumentFilePickerTypes.BuildOpenTypes(adapters);

        var patterns = AllPatterns(types);
        patterns.Should().Contain("*.docx");
        patterns.Should().Contain("*.txt", "the catalog exposes formats beyond .docx");
        patterns.Where(p => p != "*").Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Open_types_lead_with_an_all_supported_group_covering_every_openable_extension()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var expected = adapters
            .SelectMany(a => a.Formats)
            .Where(f => f.CanOpen)
            .Select(f => "*" + DocumentFileFormatResolver.NormalizeExtension(f.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray();

        var types = DocumentFilePickerTypes.BuildOpenTypes(adapters);

        types[0].Name.Should().Be("All supported documents");
        types[0].Patterns!.OrderBy(p => p).Should().Equal(expected);
    }

    [Fact]
    public void Open_types_omit_save_only_or_skip_no_formats_but_each_per_format_entry_can_open()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();

        var types = DocumentFilePickerTypes.BuildOpenTypes(adapters);

        // Every per-format entry (all but the leading "All supported" group) maps to a CanOpen descriptor.
        var openExtensions = adapters
            .SelectMany(a => a.Formats)
            .Where(f => f.CanOpen)
            .Select(f => "*" + DocumentFileFormatResolver.NormalizeExtension(f.Extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in types.Skip(1))
            t.Patterns!.Should().OnlyContain(p => openExtensions.Contains(p));
    }

    [Fact]
    public void Save_types_are_one_per_savable_format()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var expectedNames = adapters
            .SelectMany(a => a.Formats)
            .Where(f => f.CanSave)
            .Select(f => f.FormatName)
            .ToList();

        var types = DocumentFilePickerTypes.BuildSaveTypes(adapters);

        types.Select(t => t.Name).Should().BeEquivalentTo(expectedNames);
        AllPatterns(types).Should().Contain("*.docx");
    }

    private static List<string> AllPatterns(IEnumerable<FilePickerFileType> types) =>
        types.SelectMany(t => t.Patterns ?? []).ToList();
}
