using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

public class SurfaceComparerTests
{
    private static CapturedSurface S(string id, bool captured = true, string? kind = null, string? png = null) =>
        new() { Id = id, Captured = captured, Kind = kind, Png = png ?? id + ".png" };

    [Fact]
    public void Manifest_parses_from_contract_json()
    {
        const string json = """
        {
          "platform": "linux",
          "shell": "avalonia",
          "surfaces": [
            { "id": "tab.Home", "kind": "tab", "png": "tab.Home.png", "captured": true, "note": "ok" },
            { "id": "grid.demo", "kind": "grid", "png": "grid.demo.png", "captured": false, "note": "skipped" }
          ]
        }
        """;
        var m = CaptureManifest.Parse(json);
        m.Platform.Should().Be("linux");
        m.Shell.Should().Be("avalonia");
        m.Surfaces.Should().HaveCount(2);
        m.Surfaces[1].Captured.Should().BeFalse();
        m.Surfaces[1].Note.Should().Be("skipped");
    }

    [Fact]
    public void Kind_is_derived_from_id_prefix_when_absent()
    {
        SurfaceComparer.KindOf(new CapturedSurface { Id = "dialog.FormatCells" }).Should().Be("dialog");
        SurfaceComparer.KindOf(new CapturedSurface { Id = "grid.demo", Kind = "explicit" }).Should().Be("explicit");
        SurfaceComparer.KindOf(new CapturedSurface { Id = "noprefix" }).Should().Be("other");
    }

    [Fact]
    public void Grid_surfaces_are_hard_others_informational()
    {
        SurfaceComparer.SeverityOf("grid").Should().Be(DiffSeverity.Hard);
        SurfaceComparer.SeverityOf("GRID").Should().Be(DiffSeverity.Hard);
        SurfaceComparer.SeverityOf("tab").Should().Be(DiffSeverity.Informational);
        SurfaceComparer.SeverityOf("dialog").Should().Be(DiffSeverity.Informational);
    }

    [Fact]
    public void Pair_classifies_presence_both_winonly_linuxonly()
    {
        var win = new CaptureManifest
        {
            Platform = "windows", Shell = "wpf",
            Surfaces = { S("tab.Home"), S("grid.demo"), S("dialog.OnlyWin") },
        };
        var lin = new CaptureManifest
        {
            Platform = "linux", Shell = "avalonia",
            Surfaces = { S("tab.Home"), S("grid.demo"), S("dialog.OnlyLinux") },
        };

        var pairs = SurfaceComparer.Pair(win, lin);

        pairs.Single(p => p.Id == "tab.Home").Presence.Should().Be(SurfacePresence.Both);
        pairs.Single(p => p.Id == "grid.demo").Presence.Should().Be(SurfacePresence.Both);
        pairs.Single(p => p.Id == "dialog.OnlyWin").Presence.Should().Be(SurfacePresence.WindowsOnly);
        pairs.Single(p => p.Id == "dialog.OnlyLinux").Presence.Should().Be(SurfacePresence.LinuxOnly);
    }

    [Fact]
    public void Uncaptured_entry_counts_as_missing_on_that_shell()
    {
        var win = new CaptureManifest { Surfaces = { S("grid.demo", captured: true) } };
        var lin = new CaptureManifest { Surfaces = { S("grid.demo", captured: false) } };

        var pair = SurfaceComparer.Pair(win, lin).Single();
        pair.Presence.Should().Be(SurfacePresence.WindowsOnly);
    }

    [Fact]
    public void IsHardRegression_only_fires_for_hard_both_over_threshold()
    {
        var hardOver = new SurfaceComparison
        {
            Id = "grid.x", Kind = "grid", Severity = DiffSeverity.Hard,
            Presence = SurfacePresence.Both, DiffPercent = 5.0,
        };
        var hardUnder = new SurfaceComparison
        {
            Id = "grid.y", Kind = "grid", Severity = DiffSeverity.Hard,
            Presence = SurfacePresence.Both, DiffPercent = 0.5,
        };
        var chromeOver = new SurfaceComparison
        {
            Id = "tab.Home", Kind = "tab", Severity = DiffSeverity.Informational,
            Presence = SurfacePresence.Both, DiffPercent = 40.0,
        };

        hardOver.IsHardRegression(2.0).Should().BeTrue();
        hardUnder.IsHardRegression(2.0).Should().BeFalse();
        chromeOver.IsHardRegression(2.0).Should().BeFalse();
    }
}
