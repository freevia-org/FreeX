using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for J51: ColorPickerDialog swatch selection state must be exposed through
/// UI Automation (AutomationProperties.ItemStatus), not conveyed only via BorderBrush/Thickness
/// visual styling, so screen readers can tell which swatch is currently selected. Mirrors the
/// ItemStatus selection convention already used for gallery-style selection UI in the Avalonia
/// shell (MainWindow.cs / MainWindow.Charts.cs AutomationProperties.SetItemStatus).
/// </summary>
public sealed class TA11yColorPickerSwatchSelectionStatusTests
{
    [Fact]
    public void Dialog_InitialSwatchSelection_ExposesSelectedItemStatusOnlyForTheInitialColor()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0xFF, 0x00, 0x00); // Standard "Red" swatch.
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var standardPanel = (Panel)dialog.FindName("StandardColorsPanel");
                var selectedButton = FindSwatchButton(standardPanel, initialColor);
                var otherButton = FindSwatchButton(standardPanel, new CellColor(0xFF, 0xFF, 0x00)); // "Yellow"

                AutomationProperties.GetItemStatus(selectedButton).Should().Be("Selected");
                AutomationProperties.GetItemStatus(otherButton).Should().Be("Not selected");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ClickingASwatch_MovesTheSelectedItemStatusToTheNewSwatchAndClearsThePrevious()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0xFF, 0x00, 0x00); // "Red"
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var standardPanel = (Panel)dialog.FindName("StandardColorsPanel");
                var previouslySelectedButton = FindSwatchButton(standardPanel, initialColor);
                var newlySelectedButton = FindSwatchButton(standardPanel, new CellColor(0xFF, 0xFF, 0x00)); // "Yellow"

                DialogSourceTestSupport.ClickButton(newlySelectedButton);

                AutomationProperties.GetItemStatus(newlySelectedButton).Should().Be("Selected");
                AutomationProperties.GetItemStatus(previouslySelectedButton).Should().Be("Not selected");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static Button FindSwatchButton(Panel panel, CellColor color) =>
        panel.Children
            .OfType<Button>()
            .Single(button => button.Tag is CellColorSwatch swatch && swatch.Color == color);
}
