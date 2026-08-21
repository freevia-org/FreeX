using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class SisterBackstageEntryBuilderTests
{
    [Fact]
    public void SisterBackstageTheme_ExposesFreeWAndFreePPresets()
    {
        SisterBackstagePalette.FreeW.Sidebar.Should().Be(new BackstageRgb(0x4B, 0x2F, 0x12));
        SisterBackstagePalette.FreeW.Selected.Should().Be(new BackstageRgb(0x36, 0x20, 0x0C));
        SisterBackstageTheme.FreeW.Accent.Sidebar.Should().Be(Color.FromRgb(0x4B, 0x2F, 0x12));
        SisterBackstageTheme.FreeW.Accent.Selected.Should().Be(Color.FromRgb(0x36, 0x20, 0x0C));
        SisterBackstageTheme.FreeW.LinkColor.Should().Be(Color.FromRgb(0xA2, 0x67, 0x14));
        SisterBackstageTheme.FreeW.TileWidth.Should().Be(150);
        SisterBackstageTheme.FreeW.TileHeight.Should().Be(190);

        SisterBackstageTheme.FreeP.Accent.Sidebar.Should().Be(Color.FromRgb(0x4E, 0x21, 0x3B));
        SisterBackstageTheme.FreeP.Accent.Hover.Should().Be(Color.FromRgb(0xA2, 0x3B, 0x72));
        SisterBackstageTheme.FreeP.LinkColor.Should().Be(Color.FromRgb(0xA2, 0x3B, 0x72));
        SisterBackstageTheme.FreeP.TileWidth.Should().Be(190);
        SisterBackstageTheme.FreeP.TileHeight.Should().Be(150);
    }

    [Fact]
    public void Build_WithCurrentFreeWPdfImport_InsertsDirectActionAfterOpen()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
            BuildHomePane = Pane,
            UseNewPane = true,
            BuildOpenPane = Pane,
            ImportPdfText = () => { },
            BuildSharePane = Pane,
            BuildSaveAsPane = Pane,
            SaveCopy = () => { },
            BuildPrintPane = Pane,
            BuildExportPane = Pane,
            Close = () => { },
            BuildAccountPane = Pane,
            HideRecentPane = true,
        });

        entries.Select(EntryLabel).Should().Equal(
            "Home",
            "New",
            "Open",
            "Import PDF (text only)",
            "Share",
            "Info",
            "|",
            "Save",
            "Save As",
            "Save a Copy",
            "Print",
            "Export",
            "Close",
            "Account",
            "Options");
        entries.Single(entry => entry.Label == "Import PDF (text only)").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Import PDF (text only)").ContentFactory.Should().BeNull();
    }

    [Fact]
    public void Build_WithPrintAndExport_ProducesFreeWBackstageOrder()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
            SaveCopy = () => { },
            Close = () => { },
            Print = () => { },
            BuildHomePane = Pane,
            UseNewPane = true,
            BuildOpenPane = Pane,
            ImportPdfText = () => { },
            BuildSharePane = Pane,
            BuildSaveAsPane = Pane,
            BuildPrintPane = Pane,
            BuildExportPane = Pane,
            BuildAccountPane = Pane,
            HideRecentPane = true
        });

        entries.Select(EntryLabel).Should().Equal(
            "Home",
            "New",
            "Open",
            "Import PDF (text only)",
            "Share",
            "Info",
            "|",
            "Save",
            "Save As",
            "Save a Copy",
            "Print",
            "Export",
            "Close",
            "Account",
            "Options");
        entries.Single(entry => entry.Label == "Options").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Account").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Close").DockBottom.Should().BeFalse();
        entries.Single(entry => entry.Label == "Home").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "New").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "New").Action.Should().BeNull();
        entries.Single(entry => entry.Label == "Open").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Share").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Save As").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Print").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Print").Action.Should().BeNull();
        entries.Single(entry => entry.Label == "Export").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Account").ContentFactory.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Save a Copy").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Close").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Import PDF (text only)").Action.Should().NotBeNull();
        entries.Should().NotContain(entry => entry.Label == "Recent");
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
        entries.Should().NotContain(entry => entry.Label == "Account");
        entries.Should().NotContain(entry => entry.Label == "Save a Copy");
        entries.Should().NotContain(entry => entry.Label == "Home");
        entries.Single(entry => entry.Label == "Recent").ContentFactory.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithoutOpenAndSaveAsPanes_KeepsSiblingAppCommands()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec());

        entries.Single(entry => entry.Label == "Open").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Open").ContentFactory.Should().BeNull();
        entries.Single(entry => entry.Label == "Save As").Action.Should().NotBeNull();
        entries.Single(entry => entry.Label == "Save As").ContentFactory.Should().BeNull();
    }

    [Fact]
    public void Build_WithPrintActionOnly_KeepsSiblingAppPrintCommand()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
            Print = () => { },
        });

        var print = entries.Single(entry => entry.Label == "Print");

        print.Action.Should().NotBeNull();
        print.ContentFactory.Should().BeNull();
    }

    [Fact]
    public void Build_InvokesSuppliedActions()
    {
        var newInvoked = false;
        var closeInvoked = false;
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
            New = () => newInvoked = true,
            Close = () => closeInvoked = true,
        });

        entries.Single(entry => entry.Label == "New").Action!();
        entries.Single(entry => entry.Label == "Close").Action!();

        newInvoked.Should().BeTrue();
        closeInvoked.Should().BeTrue();
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
