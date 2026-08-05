using System.IO;
using System.Reflection;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for the Insert &gt; Icons library (W20 slice):
///   1. Catalog is non-empty and every entry has a valid file path.
///   2. Every catalog entry rasterises to a non-empty PNG InlineImage.
///   3. A rasterised icon round-trips through DocxWriter/DocxReader.
///   4. Search/category filter logic (pure — no disk needed).
///   5. Parity: freew.insert-icon is registered in the ribbon and backed by a command.
/// </summary>
public sealed class IconCatalogTests
{
    private static readonly IReadOnlyList<IconPickerEntry> Catalog =
        IconPickerCatalog.LoadFromBaseDirectory(AppContext.BaseDirectory);

    // ── 1. Catalog non-empty & paths valid ────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_IsNonEmpty()
    {
        Catalog.Should().NotBeEmpty("the ContentIconsSvg folder must have at least one SVG");
    }

    [Fact]
    public void Catalog_AllEntriesHaveValidPaths()
    {
        foreach (var entry in Catalog)
            File.Exists(entry.Path).Should().BeTrue($"SVG file must exist on disk: {entry.Path}");
    }

    [Fact]
    public void Catalog_CoversExpectedCategories()
    {
        var categories = IconPickerDialogPlanner.Categories(Catalog);
        categories.Should().Contain("Arrows",     "arrows/ subfolder must be present");
        categories.Should().Contain("Business",   "business/ subfolder must be present");
        categories.Should().Contain("People",     "people/ subfolder must be present");
        categories.Should().Contain("Shapes",     "shapes/ subfolder must be present");
        categories.Should().Contain("Symbols",    "symbols/ subfolder must be present");
        categories.Should().Contain("Technology", "technology/ subfolder must be present");
    }

    // ── 2. Every entry rasterises ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rasterise every catalog icon and confirm the PNG bytes + dimensions are non-zero.
    /// Runs on STA because SharpVectors / RenderTargetBitmap require STA.
    /// </summary>
    [StaFact]
    public void Catalog_AllEntriesRasteriseToNonEmptyPngInlineImage()
    {
        // If the catalog is empty (content not copied yet) skip rather than fail.
        if (!Catalog.Any())
            return;

        foreach (var entry in Catalog)
        {
            var image = SvgRasterizerHelper.RasterizeToInlineImage(entry.Path);
            image.PngBytes.Should().NotBeEmpty($"{entry.Name} must rasterise to PNG bytes");
            image.WidthPt.Should().BeGreaterThan(0,  $"{entry.Name} WidthPt must be positive");
            image.HeightPt.Should().BeGreaterThan(0, $"{entry.Name} HeightPt must be positive");
        }
    }

    // ── 3. Round-trip through DocxWriter/DocxReader ───────────────────────────────────────────────

    [StaFact]
    public void CatalogIcon_RoundTripsThroughDocx()
    {
        if (!Catalog.Any())
            return;

        // Pick the first entry as a representative sample.
        var entry = Catalog.First();
        var image = SvgRasterizerHelper.RasterizeToInlineImage(entry.Path);

        var doc  = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(para);

        using var ms = new System.IO.MemoryStream();
        FreeW.Core.IO.DocxWriter.Write(doc, ms);
        ms.Position = 0;
        var read = FreeW.Core.IO.DocxReader.Read(ms);

        var recovered = read.Paragraphs.Single().Runs.Single(r => r.Image is not null);
        recovered.Image!.PngBytes.Should().NotBeEmpty("rasterised PNG must survive the docx round-trip");
        recovered.Image.WidthPt.Should().BeGreaterThan(0);
        recovered.Image.HeightPt.Should().BeGreaterThan(0);
    }

    // ── 4. Filter / search logic ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Filter_NullCategoryAndSearch_ReturnsAll()
    {
        IconPickerDialogPlanner.Filter(Catalog, null, null).Count
            .Should().Be(Catalog.Count);
    }

    [Fact]
    public void Filter_AllCategoryLabel_ReturnsAll()
    {
        IconPickerDialogPlanner.Filter(Catalog, IconPickerDialogPlanner.AllCategoriesLabel, null).Count
            .Should().Be(Catalog.Count);
    }

    [Fact]
    public void Filter_ByCategory_ReturnsOnlyThatCategory()
    {
        var category = IconPickerDialogPlanner.Categories(Catalog).First();
        var filtered = IconPickerDialogPlanner.Filter(Catalog, category, null);
        filtered.Should().NotBeEmpty();
        filtered.All(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("filter by category must only return matching entries");
    }

    [Fact]
    public void Filter_BySearchTerm_ReturnsMatchingSubset()
    {
        // All entries have keywords; searching for "a" (almost every name has one) must match some.
        var results = IconPickerDialogPlanner.Filter(Catalog, null, "a");
        results.Should().NotBeEmpty("at least some icons must match the single-letter query 'a'");
        results.Count.Should().BeLessThan(Catalog.Count,
            "a meaningful search must narrow the results");
    }

    [Fact]
    public void Filter_ByNonMatchingTerm_ReturnsEmpty()
    {
        var results = IconPickerDialogPlanner.Filter(Catalog, null, "xyznotanicon9999");
        results.Should().BeEmpty("a nonsense search term must match nothing");
    }

    [Fact]
    public void Filter_ByCategoryAndSearch_IntersectsCorrectly()
    {
        var category = IconPickerDialogPlanner.Categories(Catalog).First();
        var results = IconPickerDialogPlanner.Filter(Catalog, category, "xyznotanicon9999");
        results.Should().BeEmpty("category + no-match search term must yield no results");
    }

    // ── 5. Ribbon parity ──────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void InsertInsertIcon_IsInIllustrationsGroupAndBacked()
    {
        var definition = FreeWRibbon.Build();
        var illustrations = definition.FindTab("insert")!.FindGroup("illustrations");

        illustrations.Should().NotBeNull();
        var commandIds = ContentIconCatalogTests_Helpers.CommandIds(illustrations!);

        commandIds.Should().Contain("freew.insert-icon",
            "Insert > Illustrations must include the Icons command");

        var editor   = new FreeW.App.Host.Editing.DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new Free.Shared.Ribbon.RibbonStateStore());
        registry.TryGet("freew.insert-icon", out _)
            .Should().BeTrue("freew.insert-icon must be registered as a backed command");
    }
}

/// <summary>Internal helper shared by icon catalog tests (mirrors the CommandIds helper in FreeWRibbonParityTests).</summary>
internal static class ContentIconCatalogTests_Helpers
{
    public static IEnumerable<string> CommandIds(Free.Shared.Ribbon.RibbonGroup group) =>
        group.Controls
            .Select(c => c.CommandId.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id));
}
