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

        source.Should().Contain("Use Zoom transition");
        source.Should().Contain("ZoomObjectPropertiesDialogSession");
        source.Should().Contain("new ZoomObjectPropertiesDialogInput(");
        source.Should().Contain("_session.TryAccept(input, out var validation)");
        source.Should().Contain("ZoomObjectPropertiesDialogSession.BuildEnablement(");
        source.Should().Contain("ZoomObjectPropertiesDialogSession.SelectExclusiveBorderMode(");
        source.Should().NotContain("ZoomObjectPropertiesPlanner.TryParse");
        source.Should().Contain("_transitionEnabled.IsChecked == true");
        source.Should().Contain("_transitionDuration.IsEnabled");
        source.Should().Contain("Use Zoom border");
        source.Should().Contain("Use gradient border");
        source.Should().Contain("Use pattern border");
        source.Should().Contain("Use no-fill border");
        source.Should().Contain("Use theme border color");
        source.Should().Contain("FrameBorderThemeColor");
        source.Should().Contain("Frame shape:");
        source.Should().Contain("_frameBorderColor.IsEnabled");
        source.Should().Contain("Apply format to all Summary Zoom tiles");
        source.Should().Contain("ApplySummaryPropertiesToAllTiles");
    }

    [Fact]
    public void Avalonia_zoom_commands_keep_shared_property_persistence_and_undo_route()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("ZoomObjectPropertiesPlanner.CommandId");
        source.Should().Contain("Editor.SetSelectedZoomObjectProperties(dialog.Properties)");
        source.Should().Contain("dialog.ApplySummaryPropertiesToAllTiles");
    }

    [Fact]
    public void Avalonia_summary_zoom_dialog_exposes_target_reordering()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "SummaryZoomDialog.cs"));

        source.Should().Contain("Move Up");
        source.Should().Contain("Move Down");
        source.Should().Contain("SummaryZoomDialogSession");
        source.Should().Contain("TryMoveSelected");
        source.Should().Contain("TryAccept(selectedIds)");
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
        source.Should().NotContain("InvalidTransitionDurationMessage");
        source.Should().NotContain("TryParseTransitionDuration");
        source.Should().NotContain("_validation");
    }

    [Fact]
    public void Avalonia_zoom_target_dialog_family_uses_portable_sessions_and_shared_chrome()
    {
        foreach (var file in new[]
                 {
                     "SlideZoomDialog.cs",
                     "SectionZoomDialog.cs",
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

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
