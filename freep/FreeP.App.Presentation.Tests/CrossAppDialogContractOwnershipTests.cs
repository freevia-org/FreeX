namespace FreeP.App.Compositor.Tests;

public sealed class CrossAppDialogContractOwnershipTests
{
    [Fact]
    public void Shared_projects_own_generic_dialog_and_localized_validation_mechanics()
    {
        var sharedShell = ReadWorkspaceFile(
            "shared", "Free.Shared.Shell", "DialogPresentationContracts.cs");
        var freePSurface = ReadWorkspaceFile(
            "freep", "FreeP.App.Presentation", "PresentationDialogSurfacePlan.cs");
        var freeWActions = ReadWorkspaceFile(
            "freew", "FreeW.App.Presentation", "Dialogs", "DialogActionButtonPlan.cs");
        var freeWFocus = ReadWorkspaceFile(
            "freew", "FreeW.App.Presentation", "Dialogs", "FreeWDialogFocusPlanner.cs");
        var freeXSelectData = ReadWorkspaceFile(
            "src", "FreeX.App.Presentation", "Charts", "Editing", "SelectDataSourcePlanner.cs");
        var sharedLocalizedText = ReadWorkspaceFile(
            "shared", "Free.Shared.Localization", "LocalizedTextDescriptor.cs");
        var freeXLocalizedText = ReadWorkspaceFile(
            "src", "FreeX.App.Presentation", "Localization", "LocalizedTextDescriptor.cs");

        sharedShell.Should().Contain("public class DialogSurfacePlan<TField, TAction>");
        sharedShell.Should().Contain("public sealed record DialogFocusPlan<TFocusTarget>");
        sharedShell.Should().Contain("Duplicate dialog surface identifier");
        freePSurface.Should().Contain(": DialogSurfacePlan<TField, TAction>");
        freePSurface.Should().NotContain("BuildIndex");
        freeWActions.Should().Contain(": DialogActionPlan");
        freeWFocus.Should().Contain("DialogFocusPlan<string>");
        freeXSelectData.Should().Contain(": DialogFieldPlan<SelectDataSourceDialogFieldId>");
        freeXSelectData.Should().Contain(": DialogSurfaceActionPlan<SelectDataSourceDialogActionId>");
        sharedShell.Should().NotContain("SelectDataSourceDialogFieldId");
        sharedLocalizedText.Should().Contain("return ResourceKey is null");
        freeXLocalizedText.Should().Contain(": Free.Shared.Localization.LocalizedTextDescriptor");
        freeXLocalizedText.Should().NotContain("return ResourceKey is null");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
    }
}
