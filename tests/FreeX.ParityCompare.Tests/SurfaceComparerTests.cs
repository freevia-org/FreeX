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
    public void Severity_classifies_grid_chrome_and_informational()
    {
        SurfaceComparer.SeverityOf("grid").Should().Be(DiffSeverity.Hard);
        SurfaceComparer.SeverityOf("GRID").Should().Be(DiffSeverity.Hard);
        SurfaceComparer.SeverityOf("tab").Should().Be(DiffSeverity.Chrome);
        SurfaceComparer.SeverityOf("backstage").Should().Be(DiffSeverity.Chrome);
        SurfaceComparer.SeverityOf("BACKSTAGE").Should().Be(DiffSeverity.Chrome);
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
    public void IsHardRegression_fires_for_hard_both_over_threshold()
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

    // H9: a Hard surface present on only one shell is always a hard regression —
    // the grid failed to render on one platform, which is a more severe defect than a pixel diff.
    [Fact]
    public void IsHardRegression_fires_for_hard_surface_present_on_one_shell_only()
    {
        var winOnly = new SurfaceComparison
        {
            Id = "grid.main", Kind = "grid", Severity = DiffSeverity.Hard,
            Presence = SurfacePresence.WindowsOnly, DiffPercent = null,
        };
        var linOnly = new SurfaceComparison
        {
            Id = "grid.main", Kind = "grid", Severity = DiffSeverity.Hard,
            Presence = SurfacePresence.LinuxOnly, DiffPercent = null,
        };
        // Chrome-only-on-one-side is NOT a hard regression (chrome is informational).
        var chromeWinOnly = new SurfaceComparison
        {
            Id = "tab.Home", Kind = "tab", Severity = DiffSeverity.Chrome,
            Presence = SurfacePresence.WindowsOnly, DiffPercent = null,
        };

        winOnly.IsHardRegression(2.0).Should().BeTrue("a hard surface missing on Linux fails the gate");
        linOnly.IsHardRegression(2.0).Should().BeTrue("a hard surface missing on Windows fails the gate");
        chromeWinOnly.IsHardRegression(2.0).Should().BeFalse("chrome one-sided is informational, not a gate failure");
    }

    // H9: Parity verdict fails when a hard surface is missing on one platform.
    [Fact]
    public void Parity_verdict_fails_when_hard_surface_missing_on_one_platform()
    {
        // Windows has grid.main; Linux does not.
        var win = new CaptureManifest
        {
            Platform = "windows", Shell = "wpf",
            Surfaces = { S("grid.main", kind: "grid") },
        };
        var lin = new CaptureManifest
        {
            Platform = "linux", Shell = "avalonia",
            Surfaces = { S("tab.Home", kind: "tab") }, // grid.main is missing entirely
        };

        var engine = new ParityComparisonEngine(
            decode: _ => throw new InvalidOperationException("no PNGs in this test"),
            exists: _ => false);
        var result = engine.Compare(win, lin, winDir: null, linDir: null, imagesDir: null, hardThreshold: 2.0);

        result.Passed.Should().BeFalse("grid.main is present on Windows but absent on Linux");
        result.HardRegressions.Should().ContainSingle(r => r.Id == "grid.main");
    }
}
