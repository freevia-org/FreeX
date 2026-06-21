using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class SisterBackstageEntryBuilderTests
{
    [Fact]
    public void SisterBackstageTheme_ExposesFreeWAndFreePPresets()
    {
        SisterBackstageTheme.FreeW.Accent.Sidebar.Should().Be(Color.FromRgb(0x17, 0x32, 0x4D));
        SisterBackstageTheme.FreeW.Accent.Selected.Should().Be(Color.FromRgb(0x0F, 0x24, 0x38));
        SisterBackstageTheme.FreeW.LinkColor.Should().Be(Color.FromRgb(0x0F, 0x6D, 0x8C));
        SisterBackstageTheme.FreeW.TileWidth.Should().Be(150);
        SisterBackstageTheme.FreeW.TileHeight.Should().Be(190);

        SisterBackstageTheme.FreeP.Accent.Sidebar.Should().Be(Color.FromRgb(0xB7, 0x47, 0x2A));
        SisterBackstageTheme.FreeP.Accent.Hover.Should().Be(Color.FromRgb(0xC9, 0x5A, 0x3D));
        SisterBackstageTheme.FreeP.LinkColor.Should().Be(Color.FromRgb(0xB7, 0x47, 0x2A));
        SisterBackstageTheme.FreeP.TileWidth.Should().Be(190);
        SisterBackstageTheme.FreeP.TileHeight.Should().Be(150);
    }

    [Fact]
    public void Build_WithPrintAndExport_ProducesFreeWBackstageOrder()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
            SaveCopy = () => { },
            Print = () => { },
            BuildExportPane = Pane
        });

        entries.Select(EntryLabel).Should().Equal(
            "Info",
            "New",
            "Open",
            "|",
            "Save",
            "Save As",
            "Save a Copy",
            "Print",
            "Export",
            "Recent",
            "New from template",
            "Options",
            "Close");
        entries.Single(entry => entry.Label == "Options").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Close").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Export").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Print").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Save a Copy").Action.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithoutPrintAndExport_ProducesFreePBackstageOrder()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec());

        entries.Select(EntryLabel).Should().Equal(
            "Info",
            "New",
            "Open",
            "|",
            "Save",
            "Save As",
            "Recent",
            "New from template",
            "Options",
            "Close");
        entries.Should().NotContain(entry => entry.Label == "Print");
        entries.Should().NotContain(entry => entry.Label == "Export");
        entries.Should().NotContain(entry => entry.Label == "Save a Copy");
    }

    [Fact]
    public void Build_InvokesSuppliedActions()
    {
        var invoked = false;
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with { New = () => invoked = true });

        entries.Single(entry => entry.Label == "New").Action!();

        invoked.Should().BeTrue();
    }

    private static SisterBackstageEntrySpec CreateSpec() =>
        new(
            Pane,
            New: () => { },
            Open: () => { },
            Save: () => { },
            SaveAs: () => { },
            BuildRecentPane: Pane,
            BuildNewPane: Pane,
            BuildOptionsPane: Pane);

    private static UIElement Pane() => throw new InvalidOperationException("Pane factories should stay lazy in these tests.");

    private static string EntryLabel(BackstageEntry entry) => entry.Separator ? "|" : entry.Label;
}
