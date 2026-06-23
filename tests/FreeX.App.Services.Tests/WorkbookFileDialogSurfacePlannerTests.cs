using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileDialogSurfacePlannerTests
{
    [Fact]
    public void CreateOpenPlan_UsesOpenWorkbookChromeAndFileTypes()
    {
        var pickerPlan = WorkbookFilePickerPlanner.BuildOpenPickerPlan(Formats(static format => format.CanOpen));

        var plan = WorkbookFileDialogSurfacePlanner.CreateOpenPlan(pickerPlan);

        plan.Kind.Should().Be(WorkbookFileDialogSurfaceKind.Open);
        plan.Title.Should().Be("Open Workbook");
        plan.PrimaryCommandText.Should().Be("Open");
        plan.DialogAutomationId.Should().Be(WorkbookFileDialogSurfacePlanner.OpenDialogAutomationId);
        plan.FileTypes.Should().Contain(row => row.DisplayName == WorkbookFilePickerPlanner.AllSupportedWorkbooksName);
    }

    [Fact]
    public void CreateSaveAsPlan_UsesSaveWorkbookChromeAndSuggestedFileName()
    {
        var pickerPlan = WorkbookFilePickerPlanner.BuildSavePickerPlan(
            Formats(static format => format.CanSave),
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "Book1",
            preferredExtension: ".fxl");

        var plan = WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(pickerPlan);

        plan.Kind.Should().Be(WorkbookFileDialogSurfaceKind.SaveAs);
        plan.Title.Should().Be("Save Workbook");
        plan.PrimaryCommandText.Should().Be("Save");
        plan.FileName.Should().Be("Quarterly Budget.fxl");
        plan.DefaultExtension.Should().Be("fxl");
        plan.DialogAutomationId.Should().Be(WorkbookFileDialogSurfacePlanner.SaveAsDialogAutomationId);
    }

    private static IReadOnlyList<FileFormatDescriptor> Formats(Func<FileFormatDescriptor, bool> predicate) =>
        WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .ToList();
}
