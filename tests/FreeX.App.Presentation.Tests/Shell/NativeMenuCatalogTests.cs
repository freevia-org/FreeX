using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class NativeMenuCatalogTests
{
    [Fact]
    public void TopLevelMenus_KeepRibbonBackstageNativeOrder()
    {
        NativeMenuCatalog.TopLevelMenus.Select(menu => menu.Id)
            .Should()
            .Equal(
                NativeMenuTopLevelId.File,
                NativeMenuTopLevelId.Home,
                NativeMenuTopLevelId.Insert,
                NativeMenuTopLevelId.PageLayout,
                NativeMenuTopLevelId.Formulas,
                NativeMenuTopLevelId.Data,
                NativeMenuTopLevelId.Review,
                NativeMenuTopLevelId.View,
                NativeMenuTopLevelId.Sheet,
                NativeMenuTopLevelId.Window,
                NativeMenuTopLevelId.Help);

        NativeMenuCatalog.TopLevelMenus.Select(menu => menu.Header)
            .Should()
            .Equal("File", "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View", "Sheet", "Window", "Help");
    }

    [Fact]
    public void FileMenuEntries_GroupBackstageAndWorkbookCommandsInNativeOrder()
    {
        NativeMenuCatalog.FileMenuEntries
            .Select(DescribeEntry)
            .Should()
            .Equal(
                nameof(NativeFileMenuItemId.NewWorkbook),
                nameof(NativeFileMenuItemId.Open),
                nameof(NativeFileMenuItemId.OpenRecent),
                nameof(NativeFileMenuItemId.ShareWorkbook),
                "|",
                nameof(NativeFileMenuItemId.BackstageInfo),
                nameof(NativeFileMenuItemId.Save),
                nameof(NativeFileMenuItemId.SaveAs),
                "|",
                nameof(NativeFileMenuItemId.Print),
                nameof(NativeFileMenuItemId.PrintPreview),
                nameof(NativeFileMenuItemId.BackstageExport),
                nameof(NativeFileMenuItemId.ExportPdf),
                nameof(NativeFileMenuItemId.WorkbookStatistics),
                nameof(NativeFileMenuItemId.PageSetup),
                "|",
                nameof(NativeFileMenuItemId.CloseWorkbook),
                "|",
                nameof(NativeFileMenuItemId.BackstageAccount),
                nameof(NativeFileMenuItemId.Options),
                "|",
                nameof(NativeFileMenuItemId.Quit));
    }

    [Fact]
    public void FileMenuItems_CarryLocalizedLabelsGesturesAndSmokeExpectations()
    {
        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.NewWorkbook).Should().Be(
            new NativeFileMenuItemPlan(
                NativeFileMenuItemId.NewWorkbook,
                "AvaloniaNativeMenu_NewWorkbook",
                new NativeMenuGesturePlan(NativeMenuGestureKey.N, NativeMenuGestureModifiers.Meta)));

        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.OpenRecent)
            .Should()
            .Be(new NativeFileMenuItemPlan(
                NativeFileMenuItemId.OpenRecent,
                "AvaloniaNativeMenu_OpenRecent",
                Gesture: null,
                RequiresGestureInSmoke: false));

        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.WorkbookStatistics).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(
                NativeMenuGestureKey.G,
                NativeMenuGestureModifiers.Control | NativeMenuGestureModifiers.Shift));

        var quit = NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.Quit);
        quit.Label.Should().Be("Quit FreeX");
        quit.UsesResourceKey.Should().BeFalse();
        quit.Gesture.Should().Be(new NativeMenuGesturePlan(NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta));
    }

    [Fact]
    public void PlanFileMenuAvailability_MatchesAvaloniaNativeFileMenuRules()
    {
        var busyPlan = NativeMenuCatalog.PlanFileMenuAvailability(
            new NativeFileMenuAvailabilityContext(
                IsIdle: false,
                CanOpen: true,
                CanSave: true,
                CanSaveAs: false,
                CanSaveThroughStorageProvider: true));

        busyPlan.IsEnabled(NativeFileMenuItemId.NewWorkbook).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.Open).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.Save).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.SaveAs).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.ExportPdf).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.Options).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.Quit).Should().BeTrue();

        var idleWithoutStoragePlan = NativeMenuCatalog.PlanFileMenuAvailability(
            new NativeFileMenuAvailabilityContext(
                IsIdle: true,
                CanOpen: false,
                CanSave: false,
                CanSaveAs: true,
                CanSaveThroughStorageProvider: false));

        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.OpenRecent).Should().BeTrue();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.BackstageExport).Should().BeFalse();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.ExportPdf).Should().BeFalse();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.WorkbookStatistics).Should().BeTrue();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.PageSetup).Should().BeTrue();
    }

    private static string DescribeEntry(NativeFileMenuEntryPlan entry) =>
        entry.Kind == NativeMenuEntryKind.Separator
            ? "|"
            : entry.Item!.Id.ToString();
}
