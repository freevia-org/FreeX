using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ZoomAuthoringParityTests
{
    [Fact]
    public void Avalonia_zoom_format_dialog_binds_the_portable_properties_session()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("ZoomObjectPropertiesDialogNativeRendererSession<Control>");
        source.Should().Contain("_avaloniaRenderer.Surface");
        source.Should().Contain("_avaloniaRenderer.Form.RegisterFields(");
        source.Should().Contain("foreach (var plan in _avaloniaRenderer.Session.FieldCatalog)");
        source.Should().Contain("ZoomObjectPropertiesDialogControlKind.Toggle");
        source.Should().Contain("_avaloniaRenderer.Form.Dispatch(field, value)");
        source.Should().Contain("_avaloniaRenderer.Session.TryAccept(out var validation)");
        source.Should().Contain("_avaloniaRenderer.Form.ApplyState(_avaloniaRenderer.Session.State)");
        source.Should().Contain("_avaloniaRenderer.Form.Focus(validation.Field)");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("AutomationProperties.SetName(");
        source.Should().Contain("AutomationProperties.SetAutomationId(");

        source.Should().NotContain("new ZoomObjectPropertiesDialogInput(");
        source.Should().NotContain("ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan()");
        source.Should().NotContain("ZoomObjectPropertiesDialogSession.BuildEnablement(");
        source.Should().NotContain("ZoomObjectPropertiesDialogSession.SelectExclusiveBorderMode(");
        source.Should().NotContain("ZoomObjectPropertiesPlanner.FrameBorder");
        source.Should().NotContain("ZoomObjectPropertiesPlanner.TryParse");
        source.Should().NotContain("Reflection blur (pt):");
        source.Should().NotContain("LoadSummaryTileFields");
        source.Should().NotContain("SyncFrameBorderState");
        source.Should().NotContain("private readonly CheckBox _returnToParent");
        source.Should().NotContain("Dictionary<ZoomObjectPropertiesDialogField");
        source.Should().NotContain("foreach (var fieldState in state.Fields)");
        source.Should().NotContain("_applyingState");
        source.Should().NotContain("private readonly ZoomObjectPropertiesDialogSession");
        source.Should().NotContain("private readonly ZoomObjectPropertiesDialogFormSession");
    }

    [Fact]
    public void Avalonia_zoom_commands_delegate_persistence_and_preview_ownership_to_shared_session()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var workflow = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Presentation",
            "Ribbon",
            "FreePRibbonCommandWorkflow.cs"));

        workflow.Should().Contain("ZoomObjectPropertiesPlanner.CommandId");
        source.Should().Contain("PresentationZoomAuthoringSession _zoomAuthoringSession");
        source.Should().Contain("_zoomAuthoringSession.ApplySelectedProperties(");
        source.Should().Contain("_zoomAuthoringSession.RestoreSelectedPreview(");
        source.Should().NotContain("Editor.SetSelectedZoomObjectProperties(");
        source.Should().NotContain("SummaryZoomPreviewPlanner.AttachPreviewImage(");
    }

    [Fact]
    public void Avalonia_summary_zoom_dialog_exposes_target_reordering()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "SummaryZoomDialog.cs"));

        source.Should().Contain("surface.Action(ZoomTargetDialogAction.MoveUp)");
        source.Should().Contain("surface.Action(ZoomTargetDialogAction.MoveDown)");
        source.Should().Contain("SummaryZoomDialogSession");
        source.Should().Contain("TryMoveSelected");
        source.Should().Contain("TryAccept(selectedIds)");
        source.Should().NotContain("\"Move Up\"");
        source.Should().NotContain("\"Move Down\"");
        source.Should().NotContain("SelectOrderedTargets");
    }

    [Fact]
    public void Avalonia_zoom_validation_uses_session_result_and_native_warning_surface()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("validation!.Message");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("_avaloniaRenderer.Form.Focus(validation.Field)");
        source.Should().NotContain("InvalidTransitionDurationMessage");
        source.Should().NotContain("InvalidFrameBorderShadowMessage");
        source.Should().NotContain("InvalidFrameBorderGlowMessage");
        source.Should().NotContain("InvalidFrameBorderSoftEdgeMessage");
        source.Should().NotContain("InvalidFrameBorderReflectionMessage");
        source.Should().NotContain("TryParseTransitionDuration");
        source.Should().NotContain("_validation");
    }

    [Fact]
    public void Avalonia_zoom_target_dialog_family_uses_portable_sessions_and_shared_chrome()
    {
        foreach (var file in new[]
                 {
                     "SingleTargetZoomDialog.cs",
                     "SummaryZoomCoverImageTargetDialog.cs",
                 })
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", file));
            source.Should().Contain("ZoomSingleTargetDialogSession");
            source.Should().Contain("ZoomDialogChrome.");
            source.Should().NotContain("FindSelectedIndex");
            source.Should().NotContain("record TargetOption");
            source.Should().NotContain("AvaloniaCompactDialogChromeStyle");
        }
    }

    [Fact]
    public void Avalonia_slide_and_section_zoom_commands_adopt_one_parameterized_modal_renderer()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("new SingleTargetZoomDialog(")
            .And.Contain("ZoomTargetDialogKind.Slide")
            .And.Contain("ZoomTargetDialogKind.Section")
            .And.Contain("ShowDialog<bool?>(this)")
            .And.NotContain("new SlideZoomDialog(")
            .And.NotContain("new SectionZoomDialog(");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
