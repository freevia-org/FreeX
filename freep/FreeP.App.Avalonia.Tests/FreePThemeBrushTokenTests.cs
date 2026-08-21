using Avalonia.Media;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class FreePThemeBrushTokenTests
{
    [Theory]
    [InlineData("FreePAccentBrush", 0xA2, 0x3B, 0x72)]
    [InlineData("FreePAccentDarkBrush", 0x4E, 0x21, 0x3B)]
    [InlineData("FreePSheetSurfaceBrush", 0xF3, 0xF3, 0xF3)]
    [InlineData("FreePWhiteBrush", 0xFF, 0xFF, 0xFF)]
    public void BuildResources_RegistersRendererConsumedBrushes(
        string key,
        byte red,
        byte green,
        byte blue)
    {
        var resources = AvaloniaThemeApplier.BuildResources(BrandThemes.FreeP, "FreeP");

        var brush = resources[key].Should().BeAssignableTo<ISolidColorBrush>().Subject;
        brush.Color.Should().Be(Color.FromRgb(red, green, blue));
    }
}
