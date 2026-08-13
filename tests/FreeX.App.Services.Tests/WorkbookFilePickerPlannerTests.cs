using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFilePickerPlannerTests
{
    [Fact]
    public void BuildOpenDialogPlan_UsesWorkbookAdapterOpenFilter()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

        var plan = WorkbookFilePickerPlanner.BuildOpenDialogPlan(adapters);

        plan.Filter.Should().Contain("All supported files");
        plan.Filter.Should().Contain("*.xlsx");
        plan.Filter.Should().Contain("*.fxl");
        plan.DefaultExtensionWithDot.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BuildSaveDialogPlan_PromotesPreferredNativeFormat()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

        var plan = WorkbookFilePickerPlanner.BuildSaveDialogPlan(
            adapters,
            workbookName: "Quarterly Budget",
            preferredDefaultFormat: ".fxl");

        plan.SuggestedFileName.Should().Be("Quarterly Budget");
        plan.DefaultExtensionWithDot.Should().Be(AppOptions.FreeXWorkbookDefaultFormat);
        plan.FilterIndex.Should().Be(FindSaveFilterIndex(adapters, ".fxl"));
        plan.Filter.Should().Contain("FreeX Workbook (*.fxl)|*.fxl");
    }

    [Fact]
    public void BuildSaveDialogPlan_FallsBackWhenPreferredFormatHasNoSaveAdapter()
    {
        var adaptersWithoutNative = WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .Where(adapter => adapter is not NativeJsonAdapter)
            .ToArray();

        var plan = WorkbookFilePickerPlanner.BuildSaveDialogPlan(
            adaptersWithoutNative,
            workbookName: "Book1",
            preferredDefaultFormat: ".fxl");

        plan.DefaultExtensionWithDot.Should().Be(AppOptions.XlsxDefaultFormat);
        plan.FilterIndex.Should().Be(FindSaveFilterIndex(adaptersWithoutNative, ".xlsx"));
    }

    [Fact]
    public void BuildOpenPickerPlan_IncludesAllSupportedWorkbookDescriptor()
    {
        var openFormats = Formats(static format => format.CanOpen);

        var plan = WorkbookFilePickerPlanner.BuildOpenPickerPlan(openFormats);

        plan.FileTypes[0].DisplayName.Should().Be(WorkbookFilePickerPlanner.AllSupportedWorkbooksName);
        plan.FileTypes[0].Patterns.Should().Contain("*.xlsx");
        plan.FileTypes[0].Patterns.Should().Contain("*.fxl");
    }

    [Fact]
    public void BuildSavePickerPlan_PromotesPreferredExtensionAndBuildsSuggestedName()
    {
        var saveFormats = Formats(static format => format.CanSave);

        var plan = WorkbookFilePickerPlanner.BuildSavePickerPlan(
            saveFormats,
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "Book1",
            preferredExtension: ".fxl");

        plan.DefaultExtensionWithoutDot.Should().Be("fxl");
        plan.SuggestedFileName.Should().Be("Quarterly Budget.fxl");
        plan.FileTypes[0].DisplayName.Should().Be("FreeX Workbook");
        plan.FileTypes[0].Patterns.Should().Equal("*.fxl");
    }

    [Fact]
    public void TryResolveSaveDialogTarget_UsesChosenExtensionAdapter()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

        var resolved = WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(
            adapters,
            @"C:\Work\Budget.fxl",
            out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeOfType<NativeJsonAdapter>();
        target.Path.Should().Be(@"C:\Work\Budget.fxl");
    }

    [Fact]
    public void TryResolveSaveDialogTarget_UsesSelectedFilterWhenExtensionMatches()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
        var plainCsvFilterIndex = FindSaveFilterIndex(adapters, ".csv");
        var utf8CsvFilterIndex = plainCsvFilterIndex + 1;

        var resolved = WorkbookFilePickerPlanner.TryResolveSaveDialogTarget(
            adapters,
            @"C:\Work\Budget.csv",
            utf8CsvFilterIndex,
            out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeOfType<CsvUtf8FileAdapter>();
        target.Path.Should().Be(@"C:\Work\Budget.csv");
    }

    [Fact]
    public void BuildSuggestedSaveAsFileName_UsesFallbackAndWorkbookFallbackWhenNeeded()
    {
        WorkbookFilePickerPlanner.BuildSuggestedSaveAsFileName(
                sourceName: "",
                fallbackDisplayName: "Book1.xlsx",
                defaultExtension: ".fxl")
            .Should()
            .Be("Book1.fxl");

        WorkbookFilePickerPlanner.BuildSuggestedSaveAsFileName(
                sourceName: "   ",
                fallbackDisplayName: "   ",
                defaultExtension: "fxl")
            .Should()
            .Be("Workbook.fxl");

        WorkbookFilePickerPlanner.BuildSuggestedSaveAsFileName(
                sourceName: "",
                fallbackDisplayName: "Document",
                defaultExtension: ".fxl")
            .Should()
            .Be("Document.fxl");
    }

    private static IReadOnlyList<FileFormatDescriptor> Formats(Func<FileFormatDescriptor, bool> predicate) =>
        WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .ToList();

    private static int FindSaveFilterIndex(IEnumerable<IFileAdapter> adapters, string extension) =>
        Free.Shared.IO.FileDialogFilterBuilder.FindSaveFilterIndex(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)),
            extension);
}
