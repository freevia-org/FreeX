using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookShareActionPlannerTests
{
    [Fact]
    public void CreatePlan_UsesShareSheetForSavedWorkbookWhenSurfaceCanShare()
    {
        var surface = new WorkbookShareActionSurface("macOS Share Sheet", CanShowShareSheet: true);
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = WorkbookShareActionPlanner.CreatePlan(
            " Budget.xlsx ",
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(WorkbookShareActionPlanKind.ShareSheet);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.None);
        plan.UnavailableReason.Should().Be(WorkbookShareActionUnavailableReason.None);
        plan.EffectiveSurface.Should().Be(surface);
        WorkbookShareActionPlanner.FormatStatus(plan)
            .Should()
            .Be($"Ready for macOS Share Sheet from {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_FallsBackToOpenContainingFolderWhenShareSheetIsUnavailable()
    {
        var surface = new WorkbookShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: false,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = WorkbookShareActionPlanner.CreatePlan(
            expectedPath,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(WorkbookShareActionPlanKind.OpenContainingFolder);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().Be(Path.GetDirectoryName(expectedPath));
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.None);
        plan.UnavailableReason.Should().Be(WorkbookShareActionUnavailableReason.ShareSheetUnavailable);
        WorkbookShareActionPlanner.FormatStatus(plan)
            .Should()
            .Be($"macOS Share Sheet is unavailable in this build; use Reveal in Finder for {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_DefersSavedWorkbookWhenNoNativeActionIsAvailable()
    {
        var surface = new WorkbookShareActionSurface("macOS Share Sheet", CanShowShareSheet: false);
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = WorkbookShareActionPlanner.CreatePlan(
            expectedPath,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(WorkbookShareActionPlanKind.Deferred);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.None);
        plan.UnavailableReason.Should().Be(WorkbookShareActionUnavailableReason.ShareSheetUnavailable);
        WorkbookShareActionPlanner.FormatStatus(plan)
            .Should()
            .Be("macOS Share Sheet is unavailable in this build and no open-containing-folder adapter is available.");
    }

    [Fact]
    public void CreatePlan_RequiresSaveAsForUnsavedWorkbookWhenFallbackCanUseAFile()
    {
        var surface = new WorkbookShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: false,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");

        var plan = WorkbookShareActionPlanner.CreatePlan(
            currentFilePath: null,
            surface,
            _ => throw new InvalidOperationException("unsaved workbooks must not probe the file system"));

        plan.Kind.Should().Be(WorkbookShareActionPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.UnsavedWorkbook);
        plan.CandidatePath.Should().BeNull();
        plan.UnavailableReason.Should().Be(WorkbookShareActionUnavailableReason.None);
        WorkbookShareActionPlanner.FormatStatus(plan)
            .Should()
            .Be("Save As is required before Reveal in Finder can use the workbook because it has not been saved yet.");
    }

    [Fact]
    public void CreatePlan_DefersUnsavedWorkbookWhenNoNativeActionIsAvailable()
    {
        var plan = WorkbookShareActionPlanner.CreatePlan(
            currentFilePath: null,
            WorkbookShareActionSurface.MacOsPreview,
            _ => throw new InvalidOperationException("deferred workbooks must not require a file-system probe"));

        plan.Kind.Should().Be(WorkbookShareActionPlanKind.Deferred);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.UnsavedWorkbook);
        plan.UnavailableReason.Should().Be(WorkbookShareActionUnavailableReason.ShareSheetUnavailable);
        WorkbookShareActionPlanner.FormatStatus(plan)
            .Should()
            .Be("macOS Share Sheet is unavailable in this build and no open-containing-folder adapter is available.");
    }
}
