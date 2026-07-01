using FluentAssertions;
using Free.Shared.IO;

namespace FreeX.Core.IO.Tests;

public sealed class FileDialogRequestPlannerTests
{
    [Fact]
    public void BuildOpenDialogPlan_BuildsFilterAndDefaultExtension()
    {
        var plan = FileDialogRequestPlanner.BuildOpenDialogPlan([
            new FileDialogFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileDialogFormatDescriptor(".pdf", "PDF", CanOpen: false, CanSave: false),
            new FileDialogFormatDescriptor(".csv", "CSV", CanOpen: true, CanSave: true),
        ]);

        plan.Filter.Should().StartWith("All supported files (*.xlsx;*.csv)|*.xlsx;*.csv");
        plan.Filter.Should().Contain("Excel Workbook (*.xlsx)|*.xlsx");
        plan.Filter.Should().EndWith("All files (*.*)|*.*");
        plan.DefaultExtensionWithDot.Should().Be(".xlsx");
    }

    [Fact]
    public void BuildSaveDialogPlan_BuildsFilterDefaultExtensionsAndFilterIndex()
    {
        var plan = FileDialogRequestPlanner.BuildSaveDialogPlan(
            Formats(),
            suggestedFileName: "Budget.csv",
            defaultExtensionWithDot: "csv");

        plan.Filter.Should().Be("Excel Workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv");
        plan.SuggestedFileName.Should().Be("Budget.csv");
        plan.DefaultExtensionWithDot.Should().Be(".csv");
        plan.DefaultExtensionWithoutDot.Should().Be("csv");
        plan.FilterIndex.Should().Be(2);
    }

    [Fact]
    public void BuildPerFormatOpenDialogPlan_PreservesSimpleSingleFormatFilter()
    {
        var plan = FileDialogRequestPlanner.BuildPerFormatOpenDialogPlan([
            new FileDialogFormatDescriptor("fxp", "FreeP presentations"),
        ]);

        plan.Filter.Should().Be("FreeP presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        plan.DefaultExtensionWithDot.Should().Be(".fxp");
    }

    [Fact]
    public void BuildPerFormatSaveDialogPlanFromSourceName_BuildsSuggestedName()
    {
        var plan = FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            [new FileDialogFormatDescriptor("fxp", "FreeP presentations")],
            sourceName: null,
            fallbackDisplayName: "Presentation",
            defaultExtensionWithDot: ".fxp");

        plan.Filter.Should().Be("FreeP presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        plan.SuggestedFileName.Should().Be("Presentation.fxp");
        plan.DefaultExtensionWithDot.Should().Be(".fxp");
        plan.DefaultExtensionWithoutDot.Should().Be("fxp");
        plan.FilterIndex.Should().Be(1);
    }

    [Fact]
    public void BuildSavePickerPlan_BuildsSuggestedNameAndPromotesPreferredExtension()
    {
        var plan = FileDialogRequestPlanner.BuildSavePickerPlan(
            Formats(),
            sourceName: "Budget.xlsx",
            fallbackDisplayName: "Book1",
            defaultExtensionWithDot: ".csv",
            preferredFirstExtension: ".csv");

        plan.SuggestedFileName.Should().Be("Budget.csv");
        plan.DefaultExtensionWithDot.Should().Be(".csv");
        plan.DefaultExtensionWithoutDot.Should().Be("csv");
        plan.FileTypes[0].DisplayName.Should().Be("CSV");
        plan.FileTypes[0].Patterns.Should().Equal("*.csv");
    }

    [Fact]
    public void BuildSuggestedSaveAsFileName_UsesDocumentFallbackWhenNamesAreBlank()
    {
        FileDialogRequestPlanner.BuildSuggestedSaveAsFileName("", "   ", ".docx")
            .Should().Be("Document.docx");
    }

    [Fact]
    public void FileFormatDialogDescriptorAdapter_FiltersOpenAndSaveDescriptors()
    {
        var formats = new[]
        {
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "Macro Workbook", CanOpen: true, CanSave: false),
            new FileFormatDescriptor(".pdf", "PDF", CanOpen: false, CanSave: true)
        };

        FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(formats)
            .Select(format => format.Extension)
            .Should()
            .Equal(".xlsx", ".xlsm");

        FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(formats)
            .Select(format => format.Extension)
            .Should()
            .Equal(".xlsx", ".pdf");
    }

    [Fact]
    public void SaveSelectionResolver_UsesMatchingFilterRowForDuplicateExtensions()
    {
        var standardDocx = new TestSaveAdapter(
            "standard",
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var strictDocx = new TestSaveAdapter(
            "strict",
            [new FileFormatDescriptor(".docx", "Strict Open XML Document")]);

        var resolved = FileDialogSaveSelectionResolver.ResolveAdapter(
            [standardDocx, strictDocx],
            static adapter => adapter.Formats,
            FindSaveAdapterByExtension,
            ".DOCX",
            filterIndex: 2);

        resolved.Should().BeSameAs(strictDocx);
    }

    [Fact]
    public void SaveSelectionResolver_FallsBackToTypedExtensionWhenFilterRowDiffers()
    {
        var docx = new TestSaveAdapter(
            "docx",
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var html = new TestSaveAdapter(
            "html",
            [new FileFormatDescriptor(".htm", "Web Page")]);

        var resolved = FileDialogSaveSelectionResolver.ResolveAdapter(
            [docx, html],
            static adapter => adapter.Formats,
            FindSaveAdapterByExtension,
            ".htm",
            filterIndex: 1);

        resolved.Should().BeSameAs(html);
    }

    private static FileDialogFormatDescriptor[] Formats() =>
    [
        new(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
        new(".pdf", "PDF", CanOpen: true, CanSave: false),
        new(".csv", "CSV", CanOpen: true, CanSave: true),
    ];

    private static TestSaveAdapter? FindSaveAdapterByExtension(
        IEnumerable<TestSaveAdapter> adapters,
        string extension)
    {
        var normalizedExtension = Free.Shared.IO.FileDialogFilterBuilder.NormalizeExtension(extension);
        return adapters.FirstOrDefault(adapter => adapter.Formats.Any(format =>
            format.CanSave &&
            string.Equals(
                Free.Shared.IO.FileDialogFilterBuilder.NormalizeExtension(format.Extension),
                normalizedExtension,
                StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record TestSaveAdapter(
        string Name,
        IReadOnlyList<FileFormatDescriptor> Formats);
}
