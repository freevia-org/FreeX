using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonRuntimeCatalogPlannerTests
{
    [Fact]
    public void GetSurfaces_ExposesRuntimeGalleriesThatStaticXamlCatalogCannotSee()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces();

        surfaces.Select(surface => surface.CommandTitle).Should().Equal(
            "Format as Table",
            "Number Format Dropdown",
            "Conditional Formatting Data Bars",
            "Conditional Formatting Color Scales",
            "Conditional Formatting Icon Sets",
            "Themes",
            "PivotTable Styles");

        Surface(surfaces, "Format as Table").Groups.Select(group => (group.Name, group.Items.Count))
            .Should()
            .Equal(("Light", 21), ("Medium", 28), ("Dark", 11));

        Surface(surfaces, "Number Format Dropdown").Groups.Select(group => group.Name)
            .Should()
            .Equal("Formats", "Actions");

        Surface(surfaces, "Conditional Formatting Data Bars").Groups.Select(group => (group.Name, group.Items.Count))
            .Should()
            .Equal(("Gradient Fill", 6), ("Solid Fill", 6));

        Surface(surfaces, "Conditional Formatting Color Scales").Groups.Select(group => (group.Name, group.Items.Count))
            .Should()
            .Equal(("3-Color Scale", 6), ("2-Color Scale", 4));

        Surface(surfaces, "Conditional Formatting Icon Sets").Groups.Select(group => (group.Name, group.Items.Count))
            .Should()
            .Equal(("Directional", 6), ("Shapes", 6), ("Indicators", 2), ("Ratings", 4));

        Surface(surfaces, "Themes").Groups.Select(group => group.Name)
            .Should()
            .Equal("Themes", "Colors", "Fonts", "Effects");

        Surface(surfaces, "Themes").Groups.Select(group => (group.Name, Items: string.Join("|", group.Items)))
            .Should()
            .Equal(
                ("Themes", "Office|FreeX Colorful|Grayscale|Customize..."),
                ("Colors", "Office|FreeX Colorful|Grayscale|Customize Colors..."),
                ("Fonts", "Office|Arial|Times New Roman|Customize Fonts..."),
                ("Effects", "Office|Subtle|Refined|Customize Effects..."));

        Surface(surfaces, "PivotTable Styles").Groups.Select(group => (group.Name, group.Items.Count))
            .Should()
            .Equal(("Light", 28), ("Medium", 28), ("Dark", 28));
    }

    [Fact]
    public void GetSurfaces_MapBackToDocumentedRibbonInventoryRows()
    {
        var inventoryRows = LoadInventoryRows();

        foreach (var surface in RibbonRuntimeCatalogPlanner.GetSurfaces())
        {
            inventoryRows.TryGetValue(surface.InventorySection, out var sectionRows)
                .Should()
                .BeTrue($"{surface.CommandTitle} should point at an existing inventory section");

            sectionRows!.Should().Contain(
                surface.InventoryRow,
                $"{surface.CommandTitle} should be represented by a documented inventory status row");
        }
    }

    [Fact]
    public void GetSurfaces_StayBoundToTheirRuntimeProviderSources()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces();

        Surface(surfaces, "Format as Table").ItemCount.Should().Be(TableStyleGalleryPlanner.GetOptions().Count);
        Surface(surfaces, "Number Format Dropdown").ItemCount.Should()
            .Be(HomeNumberFormatDropdownPlanner.Options.Count);
        Surface(surfaces, "Conditional Formatting Data Bars").ItemCount.Should()
            .Be(ConditionalFormatPresetGalleryPlanner.DataBarOptions.Count);
        Surface(surfaces, "Conditional Formatting Color Scales").ItemCount.Should()
            .Be(ConditionalFormatPresetGalleryPlanner.ColorScaleOptions.Count);
        Surface(surfaces, "Conditional Formatting Icon Sets").ItemCount.Should().Be(ConditionalFormatIconSetPlanner.Options.Count);
        Surface(surfaces, "Themes").ItemCount.Should().Be(
            WorkbookThemeCatalog.ThemePresets.Count +
            WorkbookThemeCatalog.ColorPresets.Count +
            WorkbookThemeCatalog.FontPresets.Count +
            WorkbookThemeCatalog.EffectPresets.Count);
        Surface(surfaces, "Themes").Source.Should().Be(nameof(WorkbookThemeCatalog));
        Surface(surfaces, "PivotTable Styles").ItemCount.Should().Be(PivotStyleCatalog.BuiltInStyleNames.Length);
    }

    private static RibbonRuntimeCatalogSurface Surface(
        IEnumerable<RibbonRuntimeCatalogSurface> surfaces,
        string commandTitle) =>
        surfaces.Single(surface => string.Equals(surface.CommandTitle, commandTitle, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, HashSet<string>> LoadInventoryRows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json")));
        var rowsBySection = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var section in document.RootElement.GetProperty("menuToolbarRows").EnumerateArray()
                     .Concat(document.RootElement.GetProperty("commandSurfaceRows").EnumerateArray()))
        {
            var sectionName = section.GetProperty("name").GetString() ?? "";
            if (!rowsBySection.TryGetValue(sectionName, out var rows))
            {
                rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                rowsBySection.Add(sectionName, rows);
            }

            if (section.TryGetProperty("rows", out var flatRows))
                AddRows(rows, flatRows);
            if (section.TryGetProperty("groups", out var groups))
            {
                foreach (var group in groups.EnumerateArray())
                    AddRows(rows, group.GetProperty("rows"));
            }
        }

        return rowsBySection;
    }

    private static void AddRows(ISet<string> rows, JsonElement rowElements)
    {
        foreach (var row in rowElements.EnumerateArray())
            rows.Add(row.GetProperty("name").GetString() ?? "");
    }
}
