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
    public void MainWindowsDelegateOlePlacementAndLayoutVisualPolicyToPresentationPlans()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("OleActivationCoordinator.PlanInPlaceActivation(");
            source.Should().Contain("choice.Chrome.BorderBrushHex");
            source.Should().Contain("choice.Chrome.BackgroundBrushHex");
            source.Should().Contain("placeholder.Visual");
            source.Should().NotContain("Math.Abs(shape.RotationDeg)");
            source.Should().NotContain("BuildLayoutPlaceholderFill");
            source.Should().NotContain("BuildLayoutChoiceBrushes");
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
    public void DynamicDialogAutomationIdsComeFromSharedSurfacePlans()
    {
        SlideShowCustomShowDialogSurfaceCatalog.Surface
            .Field(SlideShowCustomShowDialogField.AvailableSlides, "slide-2")
            .AutomationId.Should().Be("FreeP.CustomShows.AvailableSlides.slide-2");
        SlideShowCustomShowDialogSurfaceCatalog.Surface
            .Action(SlideShowCustomShowDialogAction.AddSlide, "slide-2")
            .AutomationId.Should().Be("FreeP.CustomShows.AddSlide.slide-2");

        foreach (var source in new[]
        {
            ReadWorkspaceFile("freep", "FreeP.App.Host", "CustomShowDialog.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "CustomShowDialog.cs")
        })
        {
            source.Should().Contain("Surface.Field(SlideShowCustomShowDialogField.AvailableSlides, slide.SlideId)");
            source.Should().Contain("Surface.Action(actionId, automationSuffix)");
            source.Should().NotContain("AutomationIdToken.AppendSegment(");
        }

        foreach (var source in new[]
        {
            ReadWorkspaceFile("freep", "FreeP.App.Host", "MotionPathEditorDialog.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "MotionPathEditorDialog.cs")
        })
        {
            source.Should().Contain("_surface.Field(MotionPathEditorDialogField.X, plan.RowIndex)");
            source.Should().Contain("_surface.Action(MotionPathEditorDialogAction.Delete, plan.RowIndex)");
            source.Should().NotContain("AutomationIdToken.AppendSegment(");
            source.Should().NotContain("rowIndex.ToString()");
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
