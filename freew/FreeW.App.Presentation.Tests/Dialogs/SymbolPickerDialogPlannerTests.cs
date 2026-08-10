using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class SymbolPickerDialogPlannerTests
{
    [Fact]
    public void Catalog_ExposesTheWpfAndAvaloniaGridContract()
    {
        FreeWSymbolPickerDialogPlanner.Columns.Should().Be(6);
        FreeWSymbolPickerDialogPlanner.Glyphs.Should().HaveCount(36);
        FreeWSymbolPickerDialogPlanner.Glyphs.Should().OnlyHaveUniqueItems();
        FreeWSymbolPickerDialogPlanner.Glyphs.Should().ContainInOrder(
            "\u00a9", "\u00ae", "\u2122", "\u00a7", "\u00b6", "\u2022",
            "\u2013", "\u2014", "\u2026", "\u00b0", "\u00b1", "\u00d7",
            "\u00f7", "\u2264", "\u2265", "\u2260", "\u2248", "\u221e",
            "\u2192", "\u2190", "\u2191", "\u2193", "\u20ac", "\u00a3",
            "\u00a5", "\u00a2", "\u00bd", "\u00bc", "\u00be", "\u2030",
            "\u03b1", "\u03b2", "\u03b3", "\u03c0", "\u03a3", "\u03a9");
    }

    [Fact]
    public void Layout_ExposesStableTileAndFooterMetrics()
    {
        FreeWSymbolPickerDialogPlanner.ButtonSize.Should().Be(36);
        FreeWSymbolPickerDialogPlanner.ButtonMargin.Should().Be(2);
        FreeWSymbolPickerDialogPlanner.ButtonFontSize.Should().Be(18);
        FreeWSymbolPickerDialogPlanner.OuterMargin.Should().Be(8);
        FreeWSymbolPickerDialogPlanner.FooterTopMargin.Should().Be(8);
        FreeWSymbolPickerDialogPlanner.FooterButtonMinWidth.Should().Be(72);
        FreeWSymbolPickerDialogPlanner.CancelText.Should().Be("Cancel");
    }

    [Fact]
    public void CodePointLabels_AreStableForEveryGlyph()
    {
        FreeWSymbolPickerDialogPlanner.BuildCodePointLabel("\u00a9").Should().Be("U+00A9");
        FreeWSymbolPickerDialogPlanner.BuildCodePointLabel("\u03a9").Should().Be("U+03A9");
        FreeWSymbolPickerDialogPlanner.Glyphs
            .Select(FreeWSymbolPickerDialogPlanner.BuildCodePointLabel)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Semantic_identity_is_stable_for_both_renderers()
    {
        var semantic = FreeWSymbolPickerDialogPlanner.BuildSemantic("\u03a9");

        semantic.AutomationId.Should().Be("SymbolPicker03A9Button");
        semantic.AutomationName.Should().Be("\u03a9");
        semantic.CodePointLabel.Should().Be("U+03A9");
        FreeWSymbolPickerDialogPlanner.DialogAutomationId.Should().Be("SymbolPickerDialog");
        FreeWSymbolPickerDialogPlanner.CancelAutomationId.Should().Be("SymbolPickerCancelButton");
    }
}
