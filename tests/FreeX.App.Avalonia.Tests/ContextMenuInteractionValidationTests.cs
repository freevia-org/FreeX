using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services.Ribbon;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

public sealed class ContextMenuInteractionValidationTests
{
    private readonly ITestOutputHelper _output;

    public ContextMenuInteractionValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Inventory_CoversEveryAuthoritativeFamilyAndVariantExactly()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        var expectedFamilies = InteractionSurfaceCatalog.ContextMenus.Select(family => family.Id).ToHashSet(StringComparer.Ordinal);
        var actualFamilies = inventory.Select(row => row.FamilyId).ToHashSet(StringComparer.Ordinal);
        actualFamilies.Should().BeEquivalentTo(expectedFamilies);

        foreach (var family in InteractionSurfaceCatalog.ContextMenus)
        {
            var expectedVariants = family.Variants.Select(variant => variant.Id).ToHashSet(StringComparer.Ordinal);
            var actualVariants = inventory
                .Where(row => row.FamilyId == family.Id)
                .Select(row => row.VariantId)
                .ToHashSet(StringComparer.Ordinal);
            actualVariants.Should().BeEquivalentTo(expectedVariants, family.Id);
            actualVariants.Should().OnlyContain(variant =>
                inventory.Any(row => row.FamilyId == family.Id && row.VariantId == variant));
            _output.WriteLine($"{family.Id}: {inventory.Count(row => row.FamilyId == family.Id)} rows");
        }

        inventory.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        inventory.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.ProductionRoute));

        var nativeRows = inventory.Count(row => row.FamilyId == "context-menu.native-application");
        var disabledRows = inventory.Count(row => !row.IsEnabled);
        var managedDispatches = inventory
            .Where(row => row.IsEnabled && row.FamilyId != "context-menu.native-application")
            .Select(row => $"{row.FamilyId}|{row.VariantId}|{row.ActionKey}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        _output.WriteLine($"total inventory rows: {inventory.Count}");
        _output.WriteLine($"managed production dispatch probes: {managedDispatches}");
        _output.WriteLine($"explicit-disabled rows: {disabledRows}");
        _output.WriteLine($"native-boundary skipped rows: {nativeRows}");
    }

    [Fact]
    public void Inventory_CoversEveryPlannerActionIncludingWorksheetShowNotes()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();

        AssertActionSet<WorksheetContextMenuAction>(inventory, "context-menu.worksheet", WorksheetContextMenuAction.None);
        inventory.Should().Contain(row =>
            row.FamilyId == "context-menu.worksheet" &&
            row.ActionKey == nameof(WorksheetContextMenuAction.ShowNotes));
        AssertActionSet<SheetTabContextMenuAction>(inventory, "context-menu.sheet-tabs", SheetTabContextMenuAction.None);
        AssertActionSet<PivotFieldContextMenuAction>(inventory, "context-menu.pivot-field", PivotFieldContextMenuAction.None);
        AssertActionSet<PivotChartFieldContextMenuAction>(
            inventory,
            "context-menu.pivot-chart",
            PivotChartFieldContextMenuAction.None,
            PivotChartFieldContextMenuAction.Summary);
        AssertActionSet<PivotHeaderMenuAction>(inventory, "context-menu.pivot-header", PivotHeaderMenuAction.Separator);

        inventory.Where(row => row.FamilyId == "context-menu.recent-files")
            .Select(row => row.ActionKey)
            .Distinct()
            .Should().BeEquivalentTo(Enum.GetNames<BackstageRecentFileMenuAction>());
        inventory.Where(row => row.FamilyId == "context-menu.quick-access-toolbar")
            .Select(row => row.ActionKey.Split(':')[0])
            .Distinct()
            .Should().BeEquivalentTo(
                nameof(QuickAccessToolbarMenuAction.Add),
                nameof(QuickAccessToolbarMenuAction.Remove),
                nameof(QuickAccessToolbarMenuAction.ExecuteHistory));
    }

    [Fact]
    public void Inventory_CoversEveryAutoFilterCriterionAndNativeMenuEntry()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        var expectedCriteria = Enum.GetValues<AutoFilterMenuFilterKind>()
            .Sum(kind => AutoFilterMenuCatalog.GetCriteriaDescriptors(kind).Count);
        inventory.Count(row => row.FamilyId == "context-menu.auto-filter-criteria")
            .Should().Be(expectedCriteria);

        var expectedNative = NativeMenuCatalog.FileMenuEntries.Count(entry => entry.Kind == NativeMenuEntryKind.Item) +
            NativeMenuCatalog.TopLevelMenus
                .Where(menu => menu.Id != NativeMenuTopLevelId.File)
                .Sum(menu => NativeMenuCatalog.GetMenuEntries(menu.Id).Count(entry => entry.Kind == NativeMenuEntryKind.Item));
        var nativeRows = inventory.Where(row => row.FamilyId == "context-menu.native-application").ToArray();
        nativeRows.Should().HaveCount(expectedNative);
        nativeRows.Select(row => row.ActionKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Inventory_HasNoPlannerOnlyActionableRoute()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        inventory.Where(row => row.IsEnabled).Should().OnlyContain(row =>
            !row.ProductionRoute.Contains("planner", StringComparison.OrdinalIgnoreCase) &&
            !row.ProductionRoute.Contains("catalog", StringComparison.OrdinalIgnoreCase));

        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InteractionValidation.cs"));
        source.Should().NotContain("planned-enabled");
        source.Should().NotContain("planned-disabled");
        source.Should().NotContain("neutral-planner-backed");
    }

    private static void AssertActionSet<TAction>(
        IReadOnlyList<ContextMenuValidationDescriptor> inventory,
        string familyId,
        params TAction[] excluded)
        where TAction : struct, Enum
    {
        var expected = Enum.GetNames<TAction>()
            .Except(excluded.Select(value => value.ToString()), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var actual = inventory.Where(row => row.FamilyId == familyId)
            .Select(row => row.ActionKey)
            .ToHashSet(StringComparer.Ordinal);
        actual.Should().BeEquivalentTo(expected);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");
        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
