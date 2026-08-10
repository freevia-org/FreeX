using Free.Shared.Shell;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePHostWorkflowSemanticOwnershipTests
{
    [Fact]
    public void OleCanvasRenderersDelegateEligibilityAndFallbackOrderingToCoordinator()
    {
        foreach (var source in new[]
        {
            ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "CanvasGestureHandler.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "AvaloniaCanvasGestureHandler.cs")
        })
        {
            source.Should().Contain("OleActivationCoordinator.TryActivate(");
            source.Should().NotContain("shape.Kind != SlideShapeKind.Ole");
            source.Should().NotContain("OleActivationService.TryActivate(shape.OleObject)");
        }
    }

    [Fact]
    public void MainWindowRenderersConsumePlanOwnedCommentAndTableText()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("plan.HeaderSummaryText");
            source.Should().Contain("plan.FilterOptionsSummaryText");
            source.Should().Contain("detail.RenderedLine");
            source.Should().NotContain("$\"{plan.CurrentSlideSummaryLabel} | {plan.DeckSummaryLabel}\"");
            source.Should().NotContain("string.Join(\" | \", plan.Filters.Select(");
            source.Should().NotContain("$\"{detail.Category}: {detail.Summary} {detail.Detail}\"");
        }
    }

    [Fact]
    public void DynamicDialogAutomationIdsUseSharedSegmentComposition()
    {
        AutomationIdToken.AppendSegment("FreeP.Dialog.Field", null)
            .Should().Be("FreeP.Dialog.Field");
        AutomationIdToken.AppendSegment("FreeP.Dialog.Field", "row-2")
            .Should().Be("FreeP.Dialog.Field.row-2");

        foreach (var source in new[]
        {
            ReadWorkspaceFile("freep", "FreeP.App.Host", "CustomShowDialog.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "CustomShowDialog.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Host", "MotionPathEditorDialog.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "MotionPathEditorDialog.cs")
        })
        {
            source.Should().Contain("AutomationIdToken.AppendSegment(");
            source.Should().NotContain("$\"{field.AutomationId}.{automationSuffix}\"");
            source.Should().NotContain("$\"{action.AutomationId}.{automationSuffix}\"");
        }
    }

    private static IEnumerable<string> MainWindowSources()
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "MainWindow.cs");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
