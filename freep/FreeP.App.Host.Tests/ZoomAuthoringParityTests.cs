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
        source.Should().Contain("ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan()");
        source.Should().Contain("new ZoomObjectPropertiesDialogInput(");
        source.Should().Contain("_session.TryAccept(input, out var validation)");
        source.Should().Contain("ZoomObjectPropertiesDialogSession.BuildEnablement(");
        source.Should().Contain("ZoomObjectPropertiesDialogSession.SelectExclusiveBorderMode(");
        source.Should().NotContain("ZoomObjectPropertiesPlanner.TryParse");
        source.Should().Contain("_transitionEnabled.IsChecked == true");
        source.Should().Contain("_transitionDuration.IsEnabled");
        source.Should().Contain("text.UseZoomTransitionLabel");
        source.Should().Contain("text.UseZoomBorderLabel");
        source.Should().Contain("text.UseGradientBorderLabel");
        source.Should().Contain("text.UsePatternBorderLabel");
        source.Should().Contain("text.UseNoFillBorderLabel");
        source.Should().Contain("text.UseThemeBorderColorLabel");
        source.Should().Contain("text.UseOuterBorderShadowLabel");
        source.Should().Contain("text.UseBorderGlowLabel");
        source.Should().Contain("text.UseBorderSoftEdgeLabel");
        source.Should().Contain("text.UseBorderReflectionLabel");
        source.Should().Contain("FrameBorderThemeColor");
        source.Should().Contain("FrameBorderShadowEnabled:");
        source.Should().Contain("FrameBorderGlowEnabled:");
        source.Should().Contain("FrameBorderSoftEdgeEnabled:");
        source.Should().Contain("FrameBorderReflectionEnabled:");
        source.Should().Contain("text.FrameShapeLabel");
        source.Should().Contain("_frameBorderColor.IsEnabled");
        source.Should().Contain("text.ApplyToAllSummaryTilesLabel");
        source.Should().Contain("ApplySummaryPropertiesToAllTiles");
        source.Should().NotContain("\"Use Zoom transition\"");
        source.Should().NotContain("\"Frame shape:\"");
        source.Should().NotContain("\"Apply format to all Summary Zoom tiles\"");
        source.Should().NotContain("\"Use border glow\"");
        source.Should().NotContain("\"Use border soft edge\"");
        source.Should().NotContain("\"Use border reflection\"");
        source.Should().NotContain("Width = 440");
        source.Should().NotContain("MinWidth = 180");
        source.Should().Contain("40 + (_session.HasSummaryTargets ? 4 : 0)",
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
        source.Should().Contain("SummaryZoomDialogSession");
        source.Should().Contain("TryMoveSelected");
        source.Should().Contain("TryAccept(selectedIds)");
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
        source.Should().Contain("MessageBox.Show(");
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
