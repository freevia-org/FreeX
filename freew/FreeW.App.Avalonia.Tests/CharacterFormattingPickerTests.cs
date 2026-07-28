using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class CharacterFormattingPickerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void Shared_planner_keeps_palette_choices_and_cancel_distinct()
    {
        CharacterFormattingPickerPlanner.BorderPalette.Should().HaveCount(12);
        CharacterFormattingPickerPlanner.ShadingPalette.Should().HaveCount(12);

        var border = CharacterFormattingPickerPlanner.SelectBorder(2);
        border.Accepted.Should().BeTrue();
        border.Border!.ColorHex.Should().Be("#0070C0");
        border.Border.LineStyle.Should().Be(BorderLineStyle.Single);

        var clearBorder = CharacterFormattingPickerPlanner.SelectNoBorder();
        clearBorder.Accepted.Should().BeTrue();
        clearBorder.Border.Should().BeNull();
        CharacterFormattingPickerPlanner.CancelBorder().Accepted.Should().BeFalse();

        var shading = CharacterFormattingPickerPlanner.SelectShading(7);
        shading.Accepted.Should().BeTrue();
        shading.Hex.Should().Be("#FFF2CC");
        CharacterFormattingPickerPlanner.SelectNoColor().Accepted.Should().BeTrue();
        CharacterFormattingPickerPlanner.SelectNoColor().Hex.Should().BeNull();
        CharacterFormattingPickerPlanner.CancelShading().Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Avalonia_border_and_shading_surfaces_expose_the_Wpf_palette_lifecycle()
    {
        await Session.Dispatch(() =>
        {
            var border = CharacterFormattingPickerDialog.ForTestBorder();
            border.PaletteButtonsForTest.Should().HaveCount(CharacterFormattingPickerPlanner.BorderPalette.Count);
            border.PaletteButtonsForTest.Select(button => AutomationProperties.GetName(button))
                .Should().Equal(CharacterFormattingPickerPlanner.BorderPalette.Select(choice => choice.Label));
            AutomationProperties.GetAutomationId(border.ClearButtonForTest)
                .Should().Be("CharacterBorderNoBorderButton");

            var shading = CharacterFormattingPickerDialog.ForTestShading();
            shading.PaletteButtonsForTest.Should().HaveCount(CharacterFormattingPickerPlanner.ShadingPalette.Count);
            shading.PaletteButtonsForTest.Select(button => AutomationProperties.GetName(button))
                .Should().Equal(CharacterFormattingPickerPlanner.ShadingPalette.Select(choice => choice.Label));
            AutomationProperties.GetAutomationId(shading.ClearButtonForTest)
                .Should().Be("CharacterShadingNoColorButton");
        }, CancellationToken.None);
    }
}
