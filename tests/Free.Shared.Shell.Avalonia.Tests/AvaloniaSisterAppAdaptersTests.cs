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

        freeW.Accent.Sidebar.Should().Be(Color.FromRgb(0x4B, 0x2F, 0x12));
        freeW.Accent.Hover.Should().Be(Color.FromRgb(0xA2, 0x67, 0x14));
        freeW.Accent.Selected.Should().Be(Color.FromRgb(0x36, 0x20, 0x0C));
        freeW.Accent.Separator.Should().Be(Color.FromRgb(0x4B, 0x2F, 0x12));
        freeW.LinkColor.Should().Be(Color.FromRgb(0xA2, 0x67, 0x14));
        freeW.TileWidth.Should().Be(150);
        freeW.TileHeight.Should().Be(190);

        freeP.Accent.Sidebar.Should().Be(Color.FromRgb(0x4E, 0x21, 0x3B));
        freeP.Accent.Hover.Should().Be(Color.FromRgb(0xA2, 0x3B, 0x72));
        freeP.Accent.Selected.Should().Be(Color.FromRgb(0x35, 0x14, 0x26));
        freeP.Accent.Separator.Should().Be(Color.FromRgb(0x4E, 0x21, 0x3B));
        freeP.LinkColor.Should().Be(Color.FromRgb(0xA2, 0x3B, 0x72));
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
