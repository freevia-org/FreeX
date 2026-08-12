using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class BackstageProjectionSourceTests
{
    [Fact]
    public void BackstageDialogs_DelegateDisplayTextAndProjectionToSharedPlanners()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));
        var accountPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Backstage",
            "FreeXBackstageAccountPanePlanner.cs"));
        var exportPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Backstage",
            "FreeXBackstageExportPanePlanner.cs"));
        var projectionPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Backstage",
            "FreeXBackstagePaneProjectionPlanner.cs"));

        source.Should().Contain("WorkbookInfoDisplayPlanner.Build(");
        source.Should().Contain("WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog");
        source.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        source.Should().Contain("FreeXBackstageInfoSurface.AvaloniaInfoDialog");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildInfoDialog(pane)");
        source.Should().Contain("BuildBackstagePaneSpec(");
        source.Should().Contain("foreach (var detail in details)");
        source.Should().NotContain("foreach (var detail in pane.Details)");
        source.Should().NotContain("foreach (var action in pane.Actions)");
        source.Should().NotContain("ResolveBackstageInfoDetailValue");

        source.Should().Contain("FreeXBackstageExportPanePlanner.Build(");
        source.Should().Contain("FreeXBackstageExportPanePlanner.CreateRequest(");
        source.Should().Contain("new FreeXBackstageExportScopeOptionSource<WorkbookExportPrintScope>(");
        source.Should().Contain("FreeXBackstageExportPanePlanner.ToExternalScope<WorkbookExportPrintScope>(scope)");
        source.Should().Contain("FreeXBackstageExportPanePlanner.ToExternalOutputKind<WorkbookExportPrintOutputKind>(outputKind)");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildExportDialog(exportPane)");
        source.Should().NotContain("CreateBackstageExportPaneRequest(");
        source.Should().NotContain("ToBackstageExportScopeId(");
        source.Should().NotContain("ToBackstageExportOutputKindId(");
        source.Should().NotContain("ToWorkbookExportScope(");
        source.Should().NotContain("ToWorkbookExportOutputKind(");
        source.Should().NotContain("FreeXBackstagePaneCatalog.GetExportScopeLabelKey(");
        source.Should().NotContain("FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(");

        source.Should().Contain("LocalAccountInfoPlanner.CreateBackstageAccountPaneRequest(");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(");
        source.Should().NotContain("foreach (var detail in plan.Details)");
        source.Should().NotContain("foreach (var action in plan.Actions)");
        source.Should().NotContain("foreach (var notice in pane.Notices)");
        source.Should().NotContain("ResolveBackstageAccountDetailValue");
        source.Should().NotContain("ResolveBackstageAccountNoticeValue");
        source.Should().NotContain("ResolveBackstageAccountCurrentWorkbook");

        accountPlannerSource.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountDetails()");
        accountPlannerSource.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountActions(request.OptionsAvailable)");
        accountPlannerSource.Should().Contain("Backstage_Account_CurrentWorkbookUnsaved");
        exportPlannerSource.Should().Contain("FreeXBackstagePaneCatalog.GetExportScopeLabelKey(request.Scope");
        exportPlannerSource.Should().Contain("FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(request.OutputKind");
        exportPlannerSource.Should().Contain("Backstage_Export_ScopeHeader");
        projectionPlannerSource.Should().Contain("public static FreeXBackstagePaneProjectionPlan BuildInfoDialog(");
        projectionPlannerSource.Should().Contain("public static FreeXBackstagePaneProjectionPlan BuildInfoPane(");
        projectionPlannerSource.Should().Contain("public static FreeXBackstagePaneProjectionPlan BuildExportDialog(");
        projectionPlannerSource.Should().Contain("public static FreeXBackstagePaneProjectionPlan BuildAccountDialog(");
        projectionPlannerSource.Should().Contain("new FreeXBackstageSectionHeaderProjectionElement(pane.FileSectionHeaderKey)");
        projectionPlannerSource.Should().Contain("new FreeXBackstageInfoActionRowProjectionElement(pane.Actions)");
        projectionPlannerSource.Should().Contain("new FreeXBackstageAccountActionRowProjectionElement(pane.Actions)");

        source.Should().NotContain("FormatBackstageFileSize");
        source.Should().NotContain("FormatBackstageLastModified");
        source.Should().NotContain("FormatBackstageProtection");
        source.Should().NotContain("FormatBackstageStatistics");
        source.Should().NotContain("FormatExportScopeLabel");
    }

    [Fact]
    public void BackstageDialogs_UseSharedAvaloniaBackstageChromeAsThinProjectionAdapter()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));
        var sharedSource = File.ReadAllText(RepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstageChrome.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("AvaloniaBackstageChromeStyle BackstageChromeStyle");
        source.Should().Contain("AvaloniaBackstageChrome.CreateDialogLayout(");
        source.Should().Contain("AvaloniaBackstageChrome.CreatePane(");
        source.Should().Contain("new AvaloniaBackstagePaneSpec(elements)");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildInfoDialog(pane)");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildExportDialog(exportPane)");
        source.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(");
        source.Should().Contain("AvaloniaBackstageDetailRowsElementSpec");
        source.Should().Contain("AvaloniaBackstageActionRowElementSpec");
        source.Should().Contain("AvaloniaBackstageRadioGroupElementSpec");
        source.Should().Contain("AvaloniaBackstageChrome.CreateActionButton(");
        source.Should().Contain("CreateBackstageClosingActionButtonSpec(");
        source.Should().NotContain("new ScrollViewer");
        source.Should().NotContain("new RadioButton");
        source.Should().NotContain("CreateBackstageDetailGrid");
        source.Should().NotContain("AddBackstageDetailRow");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateHeading(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateSectionHeader(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateNote(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateDetailGrid(");
        source.Should().NotContain("AvaloniaBackstageChrome.AddDetailRow(");
        source.Should().NotContain("new StackPanel { Spacing = 14 }");
        source.Should().NotContain("var rowIndex = grid.RowDefinitions.Count");
        source.Should().NotContain("new RowDefinition(GridLength.Auto)");
        source.Should().NotContain("ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")");
        source.Should().NotContain("            LineHeight = 20");

        sharedSource.Should().Contain("public static class AvaloniaBackstageChrome");
        sharedSource.Should().Contain("public sealed record AvaloniaBackstagePaneSpec");
        sharedSource.Should().Contain("public sealed record AvaloniaBackstageRadioGroupElementSpec");
        sharedSource.Should().Contain("public static StackPanel CreatePane(");
        sharedSource.Should().Contain("public static DockPanel CreateDialogLayout(");
        sharedSource.Should().Contain("public static Grid CreateDetailRows(");
        sharedSource.Should().Contain("public static StackPanel CreateActionRow(");
        sharedSource.Should().Contain("public static StackPanel CreateRadioGroup(");
    }

    [Fact]
    public void ParityCaptureBackstagePanes_UseSharedProjectionPlanner()
    {
        var captureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));
        var hostCaptureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Wpf", "Capture", "ParityCapture.cs"));
        var projectionPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Backstage",
            "FreeXBackstagePaneProjectionPlanner.cs"));

        projectionPlannerSource.Should().Contain("public static FreeXBackstagePaneProjectionPlan BuildInfoPane(");
        projectionPlannerSource.Should().Contain("new FreeXBackstageHeadingProjectionElement(pane.TitleKey)");
        projectionPlannerSource.Should().Contain("new FreeXBackstageSectionHeaderProjectionElement(pane.ActionsHeadingKey)");
        projectionPlannerSource.Should().Contain("new FreeXBackstageDetailRowsProjectionElement(ProjectInfoDetails(pane.Details))");

        captureSource.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildInfoPane(");
        captureSource.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(");
        captureSource.Should().Contain("FreeXBackstageDetailRowsProjectionElement");
        captureSource.Should().Contain("BuildParityCapturedBackstageAccountRows(detailRows.Rows)");
        captureSource.Should().NotContain("foreach (var action in pane.Actions)");
        captureSource.Should().NotContain("foreach (var detail in pane.Details)");

        hostCaptureSource.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(");
        hostCaptureSource.Should().Contain("FreeXBackstageDetailRowsProjectionElement");
        hostCaptureSource.Should().NotContain("pane.Details");
    }

    [Fact]
    public void LiveBackstageRecentRows_UseSharedProjectionAndDescriptors()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs"));

        source.Should().Contain("BackstageRecentFileListPlanner.Build(");
        source.Should().Contain("BackstageRecentFileListPlanner.SelectPinnedFirst(");
        source.Should().Contain("FreeXBackstageHomePanePlanner.Build()");
        source.Should().Contain("BackstageGreetingFormatter.FormatGreeting(DateTime.Now)");
        source.Should().Contain("RecentFileViewModel entry");
        source.Should().Contain("entry.LastOpenedText");
        source.Should().Contain("entry.OpenAutomationName");
        source.Should().Contain("entry.FileAccessIdentity");
        source.Should().NotContain(".OrderByDescending(entry => entry.IsPinned)");
        source.Should().NotContain("entry.LastOpened.LocalDateTime.ToString(\"g\")");
        source.Should().NotContain("GetLiveBackstageGreeting(");
    }

    [Fact]
    public void LiveBackstageFrame_UsesSharedAvaloniaFrameAsThinProductAdapter()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs"));
        var sharedFrameSource = File.ReadAllText(RepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstageFrame.cs"));
        var sharedSessionSource = File.ReadAllText(RepoFile(
            "shared",
            "Free.Shared.Shell",
            "BackstageFrameSession.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("FreeXBackstageFramePlan LiveBackstageFramePlan");
        source.Should().Contain("new AvaloniaBackstageFrame(");
        source.Should().Contain("AvaloniaBackstageRibbonChrome.Create(");
        source.Should().Contain("LiveBackstageFramePlan.Entries.Select(MapLiveBackstageEntry)");
        source.Should().Contain("SisterBackstageEntryPlan<Control>.Pane(");
        source.Should().Contain("SisterBackstageEntryPlan<Control>.Command(");
        source.Should().Contain("StableId = entry.StableId");
        source.Should().Contain("AutomationId = navigation.AutomationId");
        source.Should().Contain("KeyTip = navigation.KeyTip");
        source.Should().Contain("_backstageOverlay.TryActivateEntry(");
        source.Should().Contain("_backstageOverlay.GetEntryButton(");

        source.Should().NotContain("BuildLiveBackstageRail(");
        source.Should().NotContain("CreateLiveBackstageRailButton(");
        source.Should().NotContain("_backstagePaneButtons");
        source.Should().NotContain("_backstageCommandButtons");
        source.Should().NotContain("_backstageContentHost");
        source.Should().NotContain("_activeBackstagePane");
        source.Should().NotContain("NavigateBackstageOverlay(");
        source.Should().NotContain("button.RaiseEvent(");
        source.Should().NotContain("button.PointerEntered");
        source.Should().NotContain("button.PointerExited");

        sharedFrameSource.Should().Contain("public string? CurrentEntryId => _session.CurrentEntryId;");
        sharedSessionSource.Should().Contain("public string? CurrentEntryId { get; private set; }");
        sharedFrameSource.Should().Contain("public Button? GetEntryButton(string idOrLabel)");
        sharedFrameSource.Should().Contain("if (!button.IsVisible || !button.IsEffectivelyEnabled)");
        sharedSessionSource.Should().Contain("if (entry.DismissOnActivate)");
        sharedFrameSource.Should().Contain("BackstageFrameEntryIdentity.From(entry).ResolveAutomationId()");
        sharedFrameSource.Should().Contain("ToolTip.SetTip(button, tooltip)");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
