using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaSisterAppAdaptersTests
{
    [Fact]
    public void Backstage_themes_materialize_the_shared_sister_palettes()
    {
        var freeW = AvaloniaSisterBackstageTheme.FreeW;
        var freeP = AvaloniaSisterBackstageTheme.FreeP;

        freeW.Accent.Sidebar.Should().Be(Color.FromRgb(0x17, 0x32, 0x4D));
        freeW.Accent.Hover.Should().Be(Color.FromRgb(0x26, 0x4B, 0x6B));
        freeW.Accent.Selected.Should().Be(Color.FromRgb(0x0F, 0x24, 0x38));
        freeW.Accent.Separator.Should().Be(Color.FromRgb(0x36, 0x55, 0x73));
        freeW.LinkColor.Should().Be(Color.FromRgb(0x0F, 0x6D, 0x8C));
        freeW.TileWidth.Should().Be(150);
        freeW.TileHeight.Should().Be(190);

        freeP.Accent.Sidebar.Should().Be(Color.FromRgb(0xB7, 0x47, 0x2A));
        freeP.Accent.Hover.Should().Be(Color.FromRgb(0xC9, 0x5A, 0x3D));
        freeP.Accent.Selected.Should().Be(Color.FromRgb(0x8F, 0x37, 0x21));
        freeP.Accent.Separator.Should().Be(Color.FromRgb(0xCE, 0x6A, 0x4F));
        freeP.LinkColor.Should().Be(Color.FromRgb(0xB7, 0x47, 0x2A));
        freeP.TileWidth.Should().Be(190);
        freeP.TileHeight.Should().Be(150);
    }

    [Fact]
    public async Task External_uri_adapter_rejects_a_null_relative_control()
    {
        Func<Task> act = () => AvaloniaExternalUriLauncher.OpenAsync(null!, "https://example.test");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
