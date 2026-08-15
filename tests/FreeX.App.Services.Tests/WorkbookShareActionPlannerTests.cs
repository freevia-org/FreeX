using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookShareActionPlannerTests
{
    private static string FormatStatus(DocumentShareActionPlan plan) =>
        DocumentShareActionPlanner.FormatStatus(
            plan,
            DocumentShareActionTextSpec.WorkbookEnglish);

    [Fact]
    public void CreatePlan_UsesShareSheetForSavedWorkbookWhenSurfaceCanShare()
    {
        var surface = new DocumentShareActionSurface("macOS Share Sheet", CanShowShareSheet: true);
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareActionPlanner.CreatePlan(
            " Budget.xlsx ",
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareActionPlanKind.ShareSheet);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.None);
        plan.EffectiveSurface.Should().Be(surface);
        FormatStatus(plan)
            .Should()
            .Be($"Ready for macOS Share Sheet from {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_PrefersShareSheetForSavedWorkbookWhenBothNativeActionsAreAvailable()
    {
        var surface = new DocumentShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: true,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareActionPlanner.CreatePlan(
            expectedPath,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareActionPlanKind.ShareSheet);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.None);
        plan.EffectiveSurface.Should().Be(surface);
        FormatStatus(plan)
            .Should()
            .Be($"Ready for macOS Share Sheet from {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_FallsBackToOpenContainingFolderWhenShareSheetIsUnavailable()
    {
        var surface = new DocumentShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: false,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareActionPlanner.CreatePlan(
            expectedPath,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareActionPlanKind.OpenContainingFolder);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().Be(Path.GetDirectoryName(expectedPath));
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.ShareSheetUnavailable);
        FormatStatus(plan)
            .Should()
            .Be($"macOS Share Sheet is unavailable in this build; use Reveal in Finder for {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_DefersSavedWorkbookWhenNoNativeActionIsAvailable()
    {
        var surface = new DocumentShareActionSurface("macOS Share Sheet", CanShowShareSheet: false);
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareActionPlanner.CreatePlan(
            expectedPath,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareActionPlanKind.Deferred);
        plan.Path.Should().Be(expectedPath);
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.ShareSheetUnavailable);
        FormatStatus(plan)
            .Should()
            .Be("macOS Share Sheet is unavailable in this build and no open-containing-folder adapter is available.");
    }

    [Fact]
    public void CreatePlan_RequiresSaveAsForUnsavedDocumentWhenFallbackCanUseAFile()
    {
        var surface = new DocumentShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: false,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");

        var plan = DocumentShareActionPlanner.CreatePlan(
            currentFilePath: null,
            surface,
            _ => throw new InvalidOperationException("unsaved workbooks must not probe the file system"));

        plan.Kind.Should().Be(DocumentShareActionPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.UnsavedDocument);
        plan.CandidatePath.Should().BeNull();
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.None);
        FormatStatus(plan)
            .Should()
            .Be("Save As is required before Reveal in Finder can use the workbook because it has not been saved yet.");
    }

    [Fact]
    public void CreatePlan_RequiresSaveAsForUnsavedDocumentWhenShareSheetCanUseAFile()
    {
        var surface = new DocumentShareActionSurface(
            "macOS Share Sheet",
            CanShowShareSheet: true,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Reveal in Finder");

        var plan = DocumentShareActionPlanner.CreatePlan(
            currentFilePath: null,
            surface,
            _ => throw new InvalidOperationException("unsaved workbooks must not probe the file system"));

        plan.Kind.Should().Be(DocumentShareActionPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.ContainingFolderPath.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.UnsavedDocument);
        plan.CandidatePath.Should().BeNull();
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.None);
        FormatStatus(plan)
            .Should()
            .Be("Save As is required before macOS Share Sheet can use the workbook because it has not been saved yet.");
    }

    [Fact]
    public void CreatePlan_ReportsUnsupportedCloudLinkBeforeShareSheet()
    {
        var surface = new DocumentShareActionSurface("macOS Share Sheet", CanShowShareSheet: true);

        var plan = DocumentShareActionPlanner.CreatePlan(
            "https://example.test/Budget.xlsx",
            surface,
            _ => throw new InvalidOperationException("cloud links must not probe the file system"));

        plan.Kind.Should().Be(DocumentShareActionPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.InvalidPath);
        plan.CandidatePath.Should().Be("https://example.test/Budget.xlsx");
        FormatStatus(plan)
            .Should()
            .Be("Save As is required before macOS Share Sheet can use the workbook because cloud or web links are not supported; save the workbook to a local file first.");
    }

    [Fact]
    public void CreatePlan_DefersUnsavedDocumentWhenNoNativeActionIsAvailable()
    {
        var plan = DocumentShareActionPlanner.CreatePlan(
            currentFilePath: null,
            DocumentShareActionSurface.MacOsPreview,
            _ => throw new InvalidOperationException("deferred workbooks must not require a file-system probe"));

        plan.Kind.Should().Be(DocumentShareActionPlanKind.Deferred);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.UnsavedDocument);
        plan.UnavailableReason.Should().Be(DocumentShareActionUnavailableReason.ShareSheetUnavailable);
        FormatStatus(plan)
            .Should()
            .Be("macOS Share Sheet is unavailable in this build and no open-containing-folder adapter is available.");
    }
}
