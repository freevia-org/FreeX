using FluentAssertions;
using File = FreeX.App.Services.Tests.AvaloniaShellSourceFile;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Guards the cross-app Find &amp; Replace open mode. The two-state Find/Replace mode is the same
/// framework-independent decision in FreeX, FreeW and FreeP, so it lives in
/// <c>Free.Shared.AppServices.FindReplaceDialogPolicy</c>. Both FreeX renderers must resolve
/// mode-dependent chrome through <c>FindReplaceDialogPlanner</c>'s projection of that policy
/// instead of comparing TabItems inline.
/// </summary>
public sealed class FindReplaceOpenModeDedupSourceTests
{
    [Fact]
    public void SharedPolicy_OwnsTheFindReplaceOpenMode()
    {
        var policy = File.ReadAllText(RepositoryFileLocator.Find(
            "shared", "Free.Shared.AppServices", "FindReplaceDialogPolicy.cs"));

        policy.Should().Contain("public enum FindReplaceOpenMode");
        policy.Should().Contain("public static FindReplaceOpenMode OpenModeFor(bool showReplace)");
        policy.Should().Contain("public static bool ShowsReplaceSurface(FindReplaceOpenMode mode)");
    }

    [Fact]
    public void FreeXPlanner_ProjectsTheSharedOpenModeForBothRenderers()
    {
        var planner = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "FindReplaceDialogPlanner.cs"));

        planner.Should().Contain("FindReplaceDialogPolicy.OpenModeFor(");
        planner.Should().Contain("FindReplaceDialogPolicy.ShowsReplaceSurface(");
        planner.Should().Contain("public static FindReplaceOpenMode OpenModeFor(bool replaceMode)");
        planner.Should().Contain("public static bool ShowsReplaceCommands(FindReplaceOpenMode mode)");
        planner.Should().NotContain("public enum FindReplaceOpenMode");
    }

    [Fact]
    public void FreeXRenderers_ResolveReplaceModeChromeThroughThePlannerProjection()
    {
        var wpf = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs"));
        var avalonia = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));

        wpf.Should().Contain("internal FindReplaceOpenMode OpenMode =>");
        wpf.Should().Contain("FindReplaceDialogPlanner.OpenModeFor(");
        wpf.Should().Contain("FindReplaceDialogPlanner.ShowsReplaceCommands(OpenMode)");
        wpf.Should().Contain("FindReplaceDialogPlanner.ShowsReplaceCommands(replaceMode)");
        wpf.Should().NotContain("FindReplaceTabs.SelectedItem == ReplaceTab ? ReplaceFindBox : FindBox");
        wpf.Should().NotContain("FindReplaceTabs.SelectedItem == ReplaceTab ? Visibility.Visible");

        avalonia.Should().Contain("FindReplaceOpenMode CurrentOpenMode() =>");
        avalonia.Should().Contain("FindReplaceDialogPlanner.OpenModeFor(tabs.SelectedItem == replaceTabItem)");
        avalonia.Should().Contain(
            "bool OnReplaceTab() => FindReplaceDialogPlanner.ShowsReplaceCommands(CurrentOpenMode());");
        avalonia.Should().Contain(
            "SelectedIndex = FindReplaceDialogPlanner.ShowsReplaceCommands(replaceMode) ? 1 : 0,");
        avalonia.Should().NotContain("Height = replaceMode ? FindReplaceDialogPlanner.ReplaceTabHeight");
    }

    /// <summary>
    /// Records the well-evidenced not-duplication verdicts so a later pass does not force these
    /// merges. FreeX's status text is localized through resource keys, its blank-search allowance
    /// is a format-criterion rule no sister app has, and its result cursor is anchored to the
    /// active cell in workbook order rather than the modular cursor the shared policy serves.
    /// </summary>
    [Fact]
    public void FreeXPlanner_DocumentsTheConcernsThatDeliberatelyStayLocal()
    {
        var planner = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "FindReplaceDialogPlanner.cs"));

        planner.Should().Contain("Deliberately NOT shared with FreeW/FreeP");
        planner.Should().Contain("localized FindReplaceDialogText resource");
        planner.Should().Contain("blank-search allowance");
        planner.Should().Contain("FindReplaceWorkflowSession anchors the next match to the active cell");
    }
}
