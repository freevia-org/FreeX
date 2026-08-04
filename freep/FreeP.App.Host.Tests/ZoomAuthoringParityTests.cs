using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ZoomAuthoringParityTests
{
    [Fact]
    public void Wpf_zoom_format_dialog_uses_shared_transition_control_and_validation()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("Use Zoom transition");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseTransitionDuration(");
        source.Should().Contain("_transitionEnabled.IsChecked == true");
        source.Should().Contain("_transitionDuration.IsEnabled");
        source.Should().Contain("Use Zoom border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(");
        source.Should().Contain("Use gradient border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(");
        source.Should().Contain("Use pattern border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(");
        source.Should().Contain("Use no-fill border");
        source.Should().Contain("IsFrameBorderNoFillEnabled");
        source.Should().Contain("Use theme border color");
        source.Should().Contain("FrameBorderThemeColor");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameGeometry(");
        source.Should().Contain("Frame shape:");
        source.Should().Contain("_frameBorderColor.IsEnabled");
        source.Should().Contain("Apply format to all Summary Zoom tiles");
        source.Should().Contain("ApplySummaryPropertiesToAllTiles");
        source.Should().Contain("23 + (_summaryTargets.Count > 0 ? 4 : 0)",
            "the merged frame, Summary Zoom, option, and action controls need distinct grid rows");
    }

    [Fact]
    public void Wpf_zoom_commands_keep_shared_property_persistence_and_undo_route()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("onFormatZoom:       () => OpenZoomObjectPropertiesDialog()");
        source.Should().Contain("Editor.SetSelectedZoomObjectProperties(dialog.Properties)");
        source.Should().Contain("dialog.ApplySummaryPropertiesToAllTiles");
    }

    [Fact]
    public void Wpf_summary_zoom_dialog_exposes_target_reordering()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "SummaryZoomDialog.cs"));

        source.Should().Contain("Move Up");
        source.Should().Contain("Move Down");
        source.Should().Contain("SelectOrderedTargets");
    }

    [Fact]
    public void Wpf_zoom_validation_uses_the_shared_messages_and_modal_warning_surface()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderColorMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderWidthMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderDashMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderGradientMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderPatternMessage");
        source.Should().Contain("Use no-fill border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameGeometryMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidCropEdgesMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidSummaryTileLayoutMessage");
        source.Should().Contain("MessageBox.Show(this,");
        source.Should().NotContain("inline validation");
    }
}
