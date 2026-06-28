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

    private static FileDialogFormatDescriptor[] Formats() =>
    [
        new(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
        new(".pdf", "PDF", CanOpen: true, CanSave: false),
        new(".csv", "CSV", CanOpen: true, CanSave: true),
    ];
}
