namespace Free.Shared.AppServices.Tests;

public sealed class DocumentSharePlannerTests
{
    [Fact]
    public void ReadinessPlanner_IsDocumentNeutralAndUsesInjectedProductNoun()
    {
        var surface = new DocumentShareSurface("System Share");
        var plan = DocumentShareReadinessPlanner.CreatePlan(null, surface, _ => false);
        var text = DocumentShareReadinessTextSpec.ForDocument("presentation");

        plan.Kind.Should().Be(DocumentShareReadinessPlanKind.SaveAsBeforeShare);
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.UnsavedDocument);
        DocumentShareReadinessPlanner.FormatStatus(plan, text)
            .Should().Be("Save As is required before System Share can send the presentation because it has not been saved yet.");
    }

    [Fact]
    public void ActionPlanner_UsesPortableFileReadinessAndContainingFolderFallback()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "share", "draft.fxp");
        var surface = new DocumentShareActionSurface(
            "Share Sheet",
            CanShowShareSheet: false,
            CanOpenContainingFolder: true,
            OpenContainingFolderLabel: "Show Folder");

        var plan = DocumentShareActionPlanner.CreatePlan(filePath, surface, _ => true);

        plan.Kind.Should().Be(DocumentShareActionPlanKind.OpenContainingFolder);
        plan.Path.Should().Be(Path.GetFullPath(filePath));
        plan.ContainingFolderPath.Should().Be(Path.GetDirectoryName(Path.GetFullPath(filePath)));
        DocumentShareActionPlanner.FormatStatus(plan, new DocumentShareActionTextSpec("presentation"))
            .Should().Be($"Share Sheet is unavailable in this build; use Show Folder for {Path.GetFullPath(filePath)}.");
    }

    [Fact]
    public void WorkbookFacade_PreservesExistingBehaviorOverDocumentAuthority()
    {
        var documentPlan = DocumentShareReadinessPlanner.CreatePlan(
            "https://example.test/book.xlsx",
            new DocumentShareSurface("Windows Share"),
            _ => true);
        var workbookPlan = WorkbookShareReadinessPlanner.CreatePlan(
            "https://example.test/book.xlsx",
            WorkbookShareSurface.WindowsShare,
            _ => true);

        documentPlan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.InvalidPath);
        workbookPlan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.InvalidPath);
        WorkbookShareReadinessPlanner.FormatStatus(workbookPlan)
            .Should().Contain("save the workbook to a local file first");
    }
}
