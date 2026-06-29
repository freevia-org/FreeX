using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileDialogSurfacePlannerTests
{
    [Fact]
    public void CreateOpenPlan_UsesOpenWorkbookChromeAndFileTypes()
    {
        var pickerPlan = WorkbookFilePickerPlanner.BuildOpenPickerPlan(Formats(static format => format.CanOpen));

        var plan = WorkbookFileDialogSurfacePlanner.CreateOpenPlan(pickerPlan);

        plan.Kind.Should().Be(FileDialogSurfaceKind.Open);
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

        plan.Kind.Should().Be(FileDialogSurfaceKind.SaveAs);
        plan.Title.Should().Be("Save Workbook");
        plan.PrimaryCommandText.Should().Be("Save");
        plan.FileName.Should().Be("Quarterly Budget.fxl");
        plan.DefaultExtension.Should().Be("fxl");
        plan.DialogAutomationId.Should().Be(WorkbookFileDialogSurfacePlanner.SaveAsDialogAutomationId);
    }

    [Fact]
    public void WorkbookPlanner_DelegatesNeutralSurfaceConceptsToSharedPlanner()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileDialogSurfacePlanner.cs"));
        var sharedSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "FileDialogSurfacePlanner.cs"));

        source.Should().Contain("FileDialogSurfacePlanner.CreateOpenPlan");
        source.Should().Contain("FileDialogSurfacePlanner.CreateSaveAsPlan");
        source.Should().NotContain("public enum WorkbookFileDialogSurfaceKind");
        source.Should().NotContain("public sealed record WorkbookFileDialogTypeRow");
        sharedSource.Should().Contain("public enum FileDialogSurfaceKind");
        sharedSource.Should().Contain("public sealed record FileDialogSurfaceTypeRow");
        sharedSource.Should().Contain("public sealed record FileDialogSurfaceAutomationIds");
    }

    private static IReadOnlyList<FileFormatDescriptor> Formats(Func<FileFormatDescriptor, bool> predicate) =>
        WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .ToList();
}
