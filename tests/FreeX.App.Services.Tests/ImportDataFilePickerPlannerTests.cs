using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class ImportDataFilePickerPlannerTests
{
    [Fact]
    public void BuildAdapterOpenDialogPlan_UsesGetDataAdapterScopeAndNativeGuards()
    {
        var plan = ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(
            WorkbookFileAdapterCatalog.CreateDefaultAdapters());

        plan.CheckFileExists.Should().BeTrue();
        plan.Multiselect.Should().BeFalse();
        plan.Filter.Should().Contain("*.csv");
        plan.Filter.Should().Contain("*.txt");
        plan.Filter.Should().Contain("*.tsv");
        plan.Filter.Should().Contain("*.tab");
        plan.Filter.Should().Contain("*.xml");
        plan.Filter.Should().NotContain("*.xlsx");
        ImportExtensions(plan.Adapters).Should().Contain([".csv", ".txt", ".tsv", ".tab", ".xml"]);
        ImportExtensions(plan.Adapters).Should().NotContain(".xlsx");
    }

    [Fact]
    public void BuildAdapterOpenDialogPlan_NoMatchingAdapters_ReturnsEmptyFilter()
    {
        var plan = ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX")]);

        plan.Adapters.Should().BeEmpty();
        plan.Filter.Should().BeEmpty();
        plan.CheckFileExists.Should().BeTrue();
        plan.Multiselect.Should().BeFalse();
    }

    [Fact]
    public void BuildTextOpenPickerPlan_UsesTextOnlyGetDataPatterns()
    {
        var plan = ImportDataFilePickerPlanner.BuildTextOpenPickerPlan("Localized text files");

        plan.FileTypes.Should().ContainSingle();
        var fileType = plan.FileTypes[0];
        fileType.DisplayName.Should().Be("Localized text files");
        fileType.Patterns.Should().Equal(ImportDataFilePickerPlanner.TextImportPatterns);
        fileType.Patterns.Should().Equal("*.csv", "*.tsv", "*.tab", "*.txt");
        fileType.Patterns.Should().NotContain("*.xml");
    }

    [Fact]
    public void SelectAdapterImportAdapters_UsesFormatAliasesNotPrimaryExtensionOnly()
    {
        var adapter = new TestFileAdapter(
            extension: ".bin",
            formatName: "Aliased text",
            formats:
            [
                new FileFormatDescriptor(".bin", "Binary", CanOpen: false, CanSave: false),
                new FileFormatDescriptor(".tsv", "TSV alias", CanOpen: true, CanSave: false)
            ]);

        ImportDataFilePickerPlanner.SelectAdapterImportAdapters([adapter])
            .Should()
            .ContainSingle()
            .Which.Should().BeSameAs(adapter);
    }

    private static IReadOnlySet<string> ImportExtensions(IEnumerable<IFileAdapter> adapters) =>
        adapters
            .SelectMany(adapter => adapter.Formats)
            .Select(format => FileFormatResolver.NormalizeExtension(format.Extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
