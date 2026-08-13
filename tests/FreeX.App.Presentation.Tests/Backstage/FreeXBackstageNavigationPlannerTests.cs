using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageNavigationPlannerTests
{
    [Fact]
    public void Build_ProducesFreeXBackstageRailOrder()
    {
        var entries = FreeXBackstageNavigationPlanner.Build();

        entries.Select(EntryLabel).Should().Equal(
            "MainWindow_Text_Home",
            "Common_New",
            "MainWindow_Text_Open",
            "MainWindow_Text_Share",
            "|",
            "MainWindow_Text_Info",
            "MainWindow_Text_Save",
            "MainWindow_Text_SaveAs",
            "MainWindow_Text_Print",
            "MainWindow_Text_Export",
            "MainWindow_Text_Close",
            "|",
            "MainWindow_Text_Account",
            "MainWindow_Text_Options");

        entries.Where(entry => entry.Kind == FreeXBackstageNavigationEntryKind.Pane)
            .Select(entry => entry.Pane)
            .Should().Equal(FreeXBackstagePaneId.Home, FreeXBackstagePaneId.Info, FreeXBackstagePaneId.Print);

        entries.Where(entry => entry.Kind == FreeXBackstageNavigationEntryKind.Command)
            .Select(entry => entry.Command)
            .Should().Equal(
                FreeXBackstageCommandId.New,
                FreeXBackstageCommandId.Open,
                FreeXBackstageCommandId.Share,
                FreeXBackstageCommandId.Save,
                FreeXBackstageCommandId.SaveAs,
                FreeXBackstageCommandId.Export,
                FreeXBackstageCommandId.Close,
                FreeXBackstageCommandId.Account,
                FreeXBackstageCommandId.Options);
    }

    [Fact]
    public void Build_PinsRailAutomationAndTooltipMetadata()
    {
        var entries = FreeXBackstageNavigationPlanner.Build();

        var saveAs = entries.Single(entry => entry.Command == FreeXBackstageCommandId.SaveAs);
        saveAs.AutomationId.Should().Be("BackstageSaveAsButton");
        saveAs.AutomationNameKey.Should().Be("MainWindow_TooltipTitle_SaveAs");
        saveAs.AutomationHelpTextKey.Should().Be("MainWindow_TooltipDescription_SaveTheWorkbookWithANewNameOrFormat");
        saveAs.KeyTip.Should().Be("A");
        saveAs.Icon.Should().Be(BackstageIconKind.Save);
        saveAs.IconCommandName.Should().Be("Save As");

        var export = entries.Single(entry => entry.Command == FreeXBackstageCommandId.Export);
        export.AutomationId.Should().Be("BackstageExportButton");
        export.AutomationNameKey.Should().Be("MainWindow_TooltipTitle_ExportPDFXPS");
        export.TooltipDescriptionKey.Should().Be("MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage");
        export.Icon.Should().Be(BackstageIconKind.Share);

        var account = entries.Single(entry => entry.Command == FreeXBackstageCommandId.Account);
        account.DockBottom.Should().BeTrue();
        account.AutomationId.Should().Be("BackstageAccountButton");
        account.KeyTip.Should().Be("D");
        account.TooltipTitleKey.Should().Be("MainWindow_TooltipTitle_LocalAccount");
        account.TooltipDescriptionKey.Should().Be("MainWindow_TooltipDescription_MicrosoftAccountIntegrationIsNotImplementedFreeXUsesLocalFilesAndLocalOp_EC989658");

        entries.Single(entry => entry.Command == FreeXBackstageCommandId.Share)
            .KeyTip.Should().Be("R");
    }

    [Fact]
    public void Build_ExposesStablePaneAutomationIds()
    {
        var entries = FreeXBackstageNavigationPlanner.Build();

        entries.Single(entry => entry.Pane == FreeXBackstagePaneId.Home)
            .AutomationId.Should().Be(FreeXBackstageNavigationPlanner.HomePaneAutomationId);
        entries.Single(entry => entry.Pane == FreeXBackstagePaneId.Info)
            .AutomationId.Should().Be(FreeXBackstageNavigationPlanner.InfoPaneAutomationId);
        entries.Single(entry => entry.Pane == FreeXBackstagePaneId.Print)
            .AutomationId.Should().Be(FreeXBackstageNavigationPlanner.PrintPaneAutomationId);
    }

    [Fact]
    public void Build_AllInteractiveEntriesExposeKeytipsAndAutomationIds()
    {
        var entries = FreeXBackstageNavigationPlanner.Build()
            .Where(entry => entry.Kind != FreeXBackstageNavigationEntryKind.Divider)
            .ToList();

        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.KeyTip));
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.AutomationId));
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.AutomationNameKey));
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.AutomationHelpTextKey));
        entries.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.TooltipTitleKey));
    }

    [Fact]
    public void Build_InteractiveKeytipsArePrefixFreeForExactFirstRouting()
    {
        var keyTips = FreeXBackstageNavigationPlanner.Build()
            .Where(entry => entry.Kind != FreeXBackstageNavigationEntryKind.Divider)
            .Select(entry => entry.KeyTip!.Trim().ToUpperInvariant())
            .ToArray();

        keyTips.Should().OnlyHaveUniqueItems();
        keyTips.SelectMany(first => keyTips
                .Where(second => !string.Equals(first, second, StringComparison.Ordinal) &&
                    second.StartsWith(first, StringComparison.Ordinal))
                .Select(second => $"{first}->{second}"))
            .Should()
            .BeEmpty("exact-first activation must not hide a longer Backstage route");
    }

    private static string EntryLabel(FreeXBackstageNavigationEntry entry) =>
        entry.Kind == FreeXBackstageNavigationEntryKind.Divider ? "|" : entry.LabelKey!;
}
