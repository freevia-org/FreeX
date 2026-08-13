using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonPaletteCatalogTests
{
    [Fact]
    public void Command_palettes_have_unique_ids_and_explicit_clear_payloads()
    {
        var palettes = new[]
        {
            FreeWRibbonPaletteCatalog.FontColors,
            FreeWRibbonPaletteCatalog.ParagraphShading,
            FreeWRibbonPaletteCatalog.CharacterShading,
            FreeWRibbonPaletteCatalog.CharacterBorders,
            FreeWRibbonPaletteCatalog.Highlights,
            FreeWRibbonPaletteCatalog.PageColors,
        };

        var choices = palettes.SelectMany(palette => palette).ToArray();
        choices.Select(choice => choice.CommandId).Should().OnlyHaveUniqueItems();
        choices.Where(choice => choice.Hex is not null)
            .Should().OnlyContain(choice => choice.Hex!.Length == 7 && choice.Hex[0] == '#');
        FreeWRibbonPaletteCatalog.FontColors.Single(choice => choice.CommandId.EndsWith("automatic"))
            .Hex.Should().BeNull();
        FreeWRibbonPaletteCatalog.Highlights.Single(choice => choice.CommandId.EndsWith("none"))
            .StartsNewGroup.Should().BeTrue();
    }

    [Fact]
    public void Wpf_picker_sequences_preserve_the_existing_visual_order()
    {
        FreeWRibbonPaletteCatalog.TextAndHighlightPickerSwatches.Should().Equal(
            "#000000", "#404040", "#7F7F7F", "#C00000", "#FF0000", "#FFC000",
            "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#2F5496",
            "#7030A0", "#FFFFFF");
        FreeWRibbonPaletteCatalog.ParagraphShadingPickerSwatches.Should().Equal(
            "#FFFF00", "#92D050", "#00B0F0", "#FFC000", "#FF0000", "#D9D9D9",
            "#A6A6A6", "#FFF2CC", "#DEEBF7", "#E2EFDA", "#FCE4D6", "#EDEDED");
        FreeWRibbonPaletteCatalog.PageColorPickerSwatches.Should().HaveCount(18);
    }

    [Fact]
    public void Character_picker_labels_preserve_dialog_specific_wording()
    {
        FreeWRibbonPaletteCatalog.CharacterShading
            .Single(choice => choice.CommandId == "freew.char-shading.light-gray")
            .PickerLabel.Should().Be("Dark Gray");
        FreeWRibbonPaletteCatalog.CharacterShading
            .Single(choice => choice.CommandId == "freew.char-shading.light-peach")
            .PickerLabel.Should().Be("Light Orange");
    }
}
