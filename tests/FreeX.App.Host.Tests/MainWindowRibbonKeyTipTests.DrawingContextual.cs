using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void DrawingObjectContextualTabs_FollowSelectedObjectKindAndExposeKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithDrawingObjects);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("ShapeFormatTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PictureFormatTab").Should().BeFalse();

            harness.SelectFirstShapeObject();

            harness.ContextualTabIsVisible("ShapeFormatTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PictureFormatTab").Should().BeFalse();
            harness.ContextualTabIsVisible("ChartDesignTab").Should().BeFalse();
            harness.ContextualTabIsVisible("ChartFormatTab").Should().BeFalse();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain("JS");
            harness.OverlayBadgeTexts.Should().NotContain("JP");
            harness.HandleKeyTip(Key.J);
            harness.KeyTipScope.Should().Be("TopLevel");
            harness.HandleKeyTip(Key.S);

            harness.SelectedRibbonTabHeader.Should().Be("Shape Format");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("F").Should().ContainSingle("Shape Fill");

            harness.SelectFirstPictureObject();

            harness.ContextualTabIsVisible("ShapeFormatTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PictureFormatTab").Should().BeTrue();
            harness.CommandButtonIsEnabled("PictureFormatCropButton").Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain("JP");
            harness.OverlayBadgeTexts.Should().NotContain("JS");
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.P);

            harness.SelectedRibbonTabHeader.Should().Be("Picture Format");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("C").Should().ContainSingle("Crop Picture");

            harness.SelectActiveCell();
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("ShapeFormatTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PictureFormatTab").Should().BeFalse();
        });
    }

    [Fact]
    public void ShapeFormatContextualTab_DisablesShapeOnlyCommandsForTextBoxes()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithDrawingObjects);

            harness.SelectFirstTextBoxObject();

            harness.ContextualTabIsVisible("ShapeFormatTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PictureFormatTab").Should().BeFalse();
            harness.CommandButtonIsEnabled("ShapeFormatGradientButton").Should().BeFalse();
            harness.CommandButtonIsEnabled("ShapeFormatEffectsButton").Should().BeFalse();
        });
    }
}
