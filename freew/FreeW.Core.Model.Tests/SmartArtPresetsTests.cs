namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the SmartArt gallery preset catalogs and the new gallery-id properties on
/// <see cref="SmartArt"/>: <see cref="SmartArt.LayoutId"/>, <see cref="SmartArt.ColorSchemeId"/>,
/// <see cref="SmartArt.StyleId"/>.
/// </summary>
public class SmartArtPresetsTests
{
    // ── SmartArtLayoutPreset catalog ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LayoutCatalog_IsNonEmpty_AndContainsAtLeastOneEntryPerCategory()
    {
        SmartArtLayoutPreset.Catalog.Should().NotBeEmpty();

        // At least one List, one Process, one Hierarchy entry.
        SmartArtLayoutPreset.Catalog.Should().Contain(p => p.Kind == SmartArtKind.List);
        SmartArtLayoutPreset.Catalog.Should().Contain(p => p.Kind == SmartArtKind.Process);
        SmartArtLayoutPreset.Catalog.Should().Contain(p => p.Kind == SmartArtKind.Hierarchy);
    }

    [Fact]
    public void LayoutCatalog_HasUniqueIds()
    {
        var ids = SmartArtLayoutPreset.Catalog.Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void LayoutCatalog_AllEntriesHaveNonEmptyNameAndDescription()
    {
        foreach (var preset in SmartArtLayoutPreset.Catalog)
        {
            preset.Name.Should().NotBeNullOrWhiteSpace();
            preset.Id.Should().NotBeNullOrWhiteSpace();
            preset.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void LayoutPreset_FindById_ReturnsCorrectEntry()
    {
        var found = SmartArtLayoutPreset.FindById("process1");

        found.Should().NotBeNull();
        found!.Kind.Should().Be(SmartArtKind.Process);
    }

    [Fact]
    public void LayoutCatalog_ContainsContinuousBlockProcessPreset()
    {
        var found = SmartArtLayoutPreset.FindById("continuousBlockProcess");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Continuous Block Process");
        found.Kind.Should().Be(SmartArtKind.Process);
    }

    [Fact]
    public void LayoutCatalog_ContainsBasicPyramidPreset()
    {
        var found = SmartArtLayoutPreset.FindById("pyramid1");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Basic Pyramid");
        found.Kind.Should().Be(SmartArtKind.List);
        found.Description.Should().Contain("widening bands");
    }

    [Fact]
    public void LayoutPreset_FindById_ReturnsNullForUnknownId()
    {
        SmartArtLayoutPreset.FindById("nonexistent-xyz").Should().BeNull();
    }

    [Fact]
    public void LayoutPreset_Default_IsFirstCatalogEntry()
    {
        SmartArtLayoutPreset.Default.Should().BeSameAs(SmartArtLayoutPreset.Catalog[0]);
    }

    // ── SmartArtColorScheme catalog ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ColorCatalog_IsNonEmpty()
    {
        SmartArtColorScheme.Catalog.Should().NotBeEmpty();
    }

    [Fact]
    public void ColorCatalog_HasUniqueIds()
    {
        SmartArtColorScheme.Catalog.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ColorCatalog_AllEntriesHaveValidHexColors()
    {
        foreach (var scheme in SmartArtColorScheme.Catalog)
        {
            scheme.Color1Hex.Should().StartWith("#").And.HaveLength(7);
            scheme.Color2Hex.Should().StartWith("#").And.HaveLength(7);
            scheme.Color3Hex.Should().StartWith("#").And.HaveLength(7);
            scheme.Color4Hex.Should().StartWith("#").And.HaveLength(7);
        }
    }

    [Fact]
    public void ColorScheme_FillHexAt_CyclesThroughFourSlots()
    {
        var scheme = SmartArtColorScheme.Default;

        scheme.FillHexAt(0).Should().Be(scheme.Color1Hex);
        scheme.FillHexAt(1).Should().Be(scheme.Color2Hex);
        scheme.FillHexAt(2).Should().Be(scheme.Color3Hex);
        scheme.FillHexAt(3).Should().Be(scheme.Color4Hex);
        scheme.FillHexAt(4).Should().Be(scheme.Color1Hex); // wraps
    }

    [Fact]
    public void ColorScheme_FindById_ReturnsCorrectEntry()
    {
        var found = SmartArtColorScheme.FindById("colorful1");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Colorful Range");
    }

    [Fact]
    public void ColorScheme_Default_IsFirstCatalogEntry()
    {
        SmartArtColorScheme.Default.Should().BeSameAs(SmartArtColorScheme.Catalog[0]);
    }

    // ── SmartArtStyle catalog ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StyleCatalog_IsNonEmpty()
    {
        SmartArtStyle.Catalog.Should().NotBeEmpty();
    }

    [Fact]
    public void StyleCatalog_HasUniqueIds()
    {
        SmartArtStyle.Catalog.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void StyleCatalog_AllEntriesHaveNonEmptyName()
    {
        foreach (var style in SmartArtStyle.Catalog)
            style.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Style_FindById_ReturnsCorrectEntry()
    {
        var found = SmartArtStyle.FindById("subtle1");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Simple Fill");
    }

    [Fact]
    public void Style_Default_IsFirstCatalogEntry()
    {
        SmartArtStyle.Default.Should().BeSameAs(SmartArtStyle.Catalog[0]);
    }

    // ── SmartArt gallery-id properties ──────────────────────────────────────────────────────────────

    [Fact]
    public void SmartArt_GalleryIds_DefaultToNull()
    {
        var smartArt = new SmartArt();

        smartArt.LayoutId.Should().BeNull();
        smartArt.ColorSchemeId.Should().BeNull();
        smartArt.StyleId.Should().BeNull();
    }

    [Fact]
    public void SmartArt_GalleryIds_AreSettable()
    {
        var smartArt = new SmartArt
        {
            LayoutId = "cycle1",
            ColorSchemeId = "colorful2",
            StyleId = "intense1"
        };

        smartArt.LayoutId.Should().Be("cycle1");
        smartArt.ColorSchemeId.Should().Be("colorful2");
        smartArt.StyleId.Should().Be("intense1");
    }

    [Fact]
    public void SmartArt_LayoutId_IndependentOfKind()
    {
        // LayoutId can be set to a cycle layout even when Kind remains List.
        var smartArt = new SmartArt { Kind = SmartArtKind.List, LayoutId = "cycle1" };

        smartArt.Kind.Should().Be(SmartArtKind.List);
        smartArt.LayoutId.Should().Be("cycle1");
    }
}
