using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ZoomAuthoringParityTests
{
    [Fact]
    public void Wpf_zoom_format_dialog_binds_the_portable_properties_session()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("ZoomObjectPropertiesDialogSession");
        source.Should().Contain("_surface = _session.Surface");
        source.Should().Contain("ZoomObjectPropertiesDialogFormSession<Control>");
        source.Should().Contain("foreach (var plan in _session.FieldCatalog)");
        source.Should().Contain("ZoomObjectPropertiesDialogControlKind.Toggle");
        source.Should().Contain("_formSession.Dispatch(field, value)");
        source.Should().Contain("_session.TryAccept(out var validation)");
        source.Should().Contain("_formSession.ApplyState(_session.State)");
        source.Should().Contain("_formSession.Focus(validation.Field)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(");
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
    }

    [Fact]
    public void Wpf_zoom_commands_delegate_persistence_and_preview_ownership_to_shared_session()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("onFormatZoom:       () => OpenZoomObjectPropertiesDialog()");
        source.Should().Contain("PresentationZoomAuthoringSession _zoomAuthoringSession");
        source.Should().Contain("_zoomAuthoringSession.ApplySelectedProperties(");
        source.Should().Contain("_zoomAuthoringSession.RestoreSelectedPreview(");
        source.Should().NotContain("Editor.SetSelectedZoomObjectProperties(");
        source.Should().NotContain("SummaryZoomPreviewPlanner.AttachPreviewImage(");
    }

    [Fact]
    public void Wpf_summary_zoom_dialog_exposes_target_reordering()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
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
    public void Wpf_zoom_validation_uses_session_result_and_native_warning_surface()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("validation!.Message");
        source.Should().Contain("DialogMessageHelper.ShowWarning(");
        source.Should().Contain("_formSession.Focus(validation.Field)");
        source.Should().NotContain("InvalidTransitionDurationMessage");
        source.Should().NotContain("InvalidFrameBorderShadowMessage");
        source.Should().NotContain("InvalidFrameBorderGlowMessage");
        source.Should().NotContain("InvalidFrameBorderSoftEdgeMessage");
        source.Should().NotContain("InvalidFrameBorderReflectionMessage");
        source.Should().NotContain("TryParseTransitionDuration");
        source.Should().NotContain("inline validation");
    }

    [Fact]
    public void Wpf_zoom_target_dialog_family_uses_portable_selection_sessions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var file in new[]
                 {
                     "SlideZoomDialog.cs",
                     "SectionZoomDialog.cs",
                     "SummaryZoomCoverImageTargetDialog.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", file));
            source.Should().Contain("ZoomSingleTargetDialogSession");
            source.Should().NotContain("FindSelectedIndex");
            source.Should().NotContain("record TargetOption");
        }
    }
}
