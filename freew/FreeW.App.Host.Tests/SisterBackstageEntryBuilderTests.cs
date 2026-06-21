using System;
using System.Linq;
using System.Windows;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class SisterBackstageEntryBuilderTests
{
    [Fact]
    public void Build_WithPrintAndExport_ProducesFreeWBackstageOrder()
    {
        var entries = SisterBackstageEntryBuilder.Build(CreateSpec() with
        {
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
