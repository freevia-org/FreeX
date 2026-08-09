using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaPrintPreviewSourceTests
{
    [Fact]
    public void PrintPreview_DelegatesSettingsSurfaceChoicesToSharedPresentationPlanners()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PrintPreview.cs"));

        source.Should().Contain("PrintPreviewSurfacePlanner.CreateTopToolbarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateDocumentToolbarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateFindBarPlan(");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateSettingsRailPlan(");
        source.Should().Contain("PrintPreviewSettingsTextResolver");
        source.Should().Contain("Content = topToolbarPlan.PrintButtonText");
        source.Should().Contain("Header = plan.CloseButtonText");
        source.Should().Contain("AutomationProperties.SetAutomationId(overflowButton, PrintPreviewDialogPlanner.CloseButtonAutomationId)");
        source.Should().Contain("CreatePreviewToolbarButton(documentToolbarPlan.NavigationButtons[0])");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, plan.AutomationId)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, printWhatOptions, plan.Settings.PrintWhatSelectedIndex)");
        source.Should().Contain("var printWhatOptions = plan.Settings.PrintWhatOptions;");
        source.Should().Contain("AvaloniaPrintPreviewPaginationContext.TryCreateWorkbook(");
        source.Should().NotContain("DisableUnsupportedPrintWhatScopes");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.SidesOptions, plan.Settings.SidesSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.CollationOptions, plan.Settings.CollationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.OrientationOptions, plan.Settings.OrientationSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.PaperSizeOptions, plan.Settings.PaperSizeSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.MarginOptions, plan.Settings.MarginsSelectedIndex)");
        source.Should().Contain("CreatePreviewChoiceComboBox(plan.ChoiceComboWidth, plan.Settings.ScalingOptions, plan.Settings.ScalingSelectedIndex)");
        source.Should().Contain("RenderPreviewInstructions(canvas, painting.Instructions)");
        source.Should().Contain("embedded chart object blocks");
        source.Should().Contain("IsChecked = plan.Settings.PrintGridlines");
        source.Should().Contain("Content = plan.PrintHeadingsText");
        source.Should().Contain("IsChecked = plan.Settings.PrintHeadings");
        source.Should().Contain("IsEnabled = plan.Settings.IgnorePrintAreaEnabled");
        source.Should().Contain("IReadOnlyList<PrintPreviewParityPage>? parityPages = null");
        source.Should().Contain("BuildPreviewParityPageView(parityPages[pageIndex])");
        source.Should().Contain("AvaloniaRibbonIcons.BuildMonochrome");
        source.Should().Contain("PrintPreviewSurfacePlanner.DocumentToolbarChrome");
        source.Should().Contain("CreateDocumentToolbarIcon(RibbonCommandIconKind.Print");
        source.Should().Contain("PrintPreviewSurfacePlanner.ParityClientWidth");
        source.Should().Contain("PrintPreviewSurfacePlanner.ParityClientHeight");
        source.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Left");
        source.Should().Contain("PrintPreviewSurfacePlanner.PreviewPageLeftPadding");
        source.Should().Contain("ScrollBarVisibility.Auto");
        source.Should().Contain("CreateFindNavigationButton");
        source.Should().Contain("new MenuFlyout { Items = { overflowItem } }");
        source.Should().Contain("IsVisible = false");

        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PrintWhatActiveSheets\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_SidesOneSided\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CollatedOption\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CopiesSectionLabel\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PageSetupButton\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_PrintButton\"");
        source.Should().NotContain("PrintPreviewText(\"PrintPreview_CloseButton\"");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Portrait\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"A4\")");
        source.Should().NotContain("CreatePreviewComboBox(183, \"Narrow\")");
        source.Should().NotContain("CreatePreviewComboBox(82, \"100%\")");
        source.Should().NotContain("AlignCellTextLeft");
        source.Should().NotContain("drawing objects / charts on the page (the page-content model omits them by design)");
    }

    [Fact]
    public void PrintPreview_MatchesWpfDocumentViewerPaperAndSurround()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PrintPreview.cs"));

        source.Should().Contain("private static readonly IBrush PrintPreviewSurfaceBackground = Brush(240, 240, 240);");
        source.Should().NotContain("PrintPreviewSurfaceBackground = Brush(82, 86, 92)");
        source.Should().Contain("Foreground = Brush(92, 92, 92)");
        source.Should().Contain("Background = Brushes.White");
        source.Should().Contain("BorderBrush = Brushes.Black");
        source.Should().Contain("OffsetX = 4");
        source.Should().Contain("OffsetY = 4");
        source.Should().Contain("Color = Color.FromArgb(89, 0, 0, 0)");
    }

    [Fact]
    public void ParityCapture_UsesTheSharedPrintPreviewFixtureInsteadOfLiveWorksheetPagination()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("PrintPreviewParityFixture.Pages");
        source.Should().Contain("ShowPrintPreviewDialogAsync(");
        source.Should().NotContain("SeedPrintPreviewParityReport();\n    await ShowPrintPreviewDialogAsync(");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
