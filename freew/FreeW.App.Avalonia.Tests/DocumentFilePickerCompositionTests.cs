using Avalonia.Platform.Storage;
using Free.Shared.IO;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentFilePickerCompositionTests
{
    [Fact]
    public void OpenTypesIncludeMoreThanJustDocx()
    {
        var types = BuildOpenTypes(DocumentFileAdapterCatalog.CreateDefaultAdapters());

        var patterns = AllPatterns(types);
        patterns.Should().Contain("*.docx");
        patterns.Should().Contain("*.txt", "the catalog exposes formats beyond .docx");
        patterns.Should().NotContain("*.pdf", "PDF text import has a dedicated command instead of normal Open");
        patterns.Where(pattern => pattern != "*").Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void OpenTypesLeadWithAllSupportedOpenableExtensions()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var expected = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanOpen)
            .Select(format => "*" + DocumentFileFormatResolver.NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pattern => pattern)
            .ToArray();

        var types = BuildOpenTypes(adapters);

        types[0].Name.Should().Be("All supported documents");
        types[0].Patterns!.OrderBy(pattern => pattern).Should().Equal(expected);
    }

    [Fact]
    public void OpenTypesOmitSaveOnlyFormats()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var openExtensions = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanOpen)
            .Select(format => "*" + DocumentFileFormatResolver.NormalizeExtension(format.Extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var types = BuildOpenTypes(adapters);

        foreach (var type in types.Skip(1))
            type.Patterns!.Should().OnlyContain(pattern => openExtensions.Contains(pattern));
    }

    [Fact]
    public void SaveTypesContainEachSavableFormat()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var expectedNames = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .Select(format => format.FormatName);

        var types = BuildSaveTypes(adapters);

        types.Select(type => type.Name).Should().BeEquivalentTo(expectedNames);
        AllPatterns(types).Should().Contain("*.docx");
        AllPatterns(types).Should().NotContain("*.pdf");
    }

    [Fact]
    public void PdfImportTypesComeFromTheSharedPersistenceCatalog()
    {
        var plan = new DocumentPersistenceWorkflow().BuildPdfImportPickerPlan();
        var types = AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes);

        types.Select(type => type.Name).Should().Equal("PDF Document");
        types.Should().OnlyContain(type => type.Patterns!.SequenceEqual(new[] { "*.pdf" }));
        types.Should().OnlyContain(type => type.MimeTypes!.SequenceEqual(new[] { "application/pdf" }));
    }

    [Fact]
    public void SharedAdapterPreservesDescriptorData()
    {
        var descriptor = new FileDialogPickerTypeDescriptor(
            "Exact Label",
            ["*.docx", "*.txt", "*.docx"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain"]);

        var type = AvaloniaFilePickerTypeAdapter.ToFileType(descriptor);

        type.Name.Should().Be("Exact Label");
        type.Patterns.Should().Equal("*.docx", "*.txt", "*.docx");
        type.MimeTypes.Should().Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "text/plain");
    }

    [Fact]
    public void SharedAdapterBuildsNamedTypesWithMimeTypes()
    {
        var type = AvaloniaFilePickerTypeAdapter.CreateFileType(
            "Images",
            ["*.png", "*.jpg"],
            ["image/png", "image/jpeg"]);

        type.Name.Should().Be("Images");
        type.Patterns.Should().Equal("*.png", "*.jpg");
        type.MimeTypes.Should().Equal("image/png", "image/jpeg");
    }

    [Fact]
    public void AvaloniaMainWindowComposesCanonicalPickerOwnersDirectly()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var facadePath = Path.Combine(root, "freew", "FreeW.App.Avalonia", "DocumentFilePickerTypes.cs");
        var mainWindow = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        File.Exists(facadePath).Should().BeFalse();
        mainWindow.Should().Contain("DocumentFileDialogRequestPlanner");
        mainWindow.Should().Contain("AvaloniaFilePickerTypeAdapter.ToFileTypes(");
        mainWindow.Should().NotContain("DocumentFilePickerTypes");
    }

    private static IReadOnlyList<FilePickerFileType> BuildOpenTypes(IEnumerable<IDocumentFileAdapter> adapters) =>
        AvaloniaFilePickerTypeAdapter.ToFileTypes(
            DocumentFileDialogRequestPlanner.BuildOpenPickerPlan(adapters).FileTypes);

    private static IReadOnlyList<FilePickerFileType> BuildSaveTypes(IEnumerable<IDocumentFileAdapter> adapters) =>
        AvaloniaFilePickerTypeAdapter.ToFileTypes(
            DocumentFileDialogRequestPlanner.BuildSavePickerPlan(
                adapters,
                sourceName: null,
                fallbackDisplayName: "Document",
                defaultExtensionWithDot: ".docx").FileTypes);

    private static List<string> AllPatterns(IEnumerable<FilePickerFileType> types) =>
        types.SelectMany(type => type.Patterns ?? []).ToList();
}
