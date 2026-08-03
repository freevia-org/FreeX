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

    // R120: neither production capture path (src/FreeX.App.Host/ParityCapture.cs:227/235 and
    // src/FreeX.App.Avalonia/MainWindow.ParityCapture.cs:320/327) ever emits kind:"grid" for
    // grid.demo/grid.sheetTabsOverflow — both explicitly tag them kind:"screen". Before the fix,
    // SeverityOf("screen") fell into the Chrome bucket (a prior commit swept "screen" in alongside
    // legitimately-chrome kinds like static-tab/overlay), so the hard fidelity gate could never
    // fail on a real grid-rendering regression. "screen" must resolve to Hard, same as "grid".
    [Fact]
    public void R120_SeverityOf_screen_is_hard_not_chrome()
    {
        SurfaceComparer.SeverityOf("screen").Should().Be(DiffSeverity.Hard);
        SurfaceComparer.SeverityOf("SCREEN").Should().Be(DiffSeverity.Hard);
    }

    // R120: build the manifest fixture exactly the way the real capture code builds it — explicit
    // Kind="screen" for the grid.* id, not the null-Kind id-prefix-derivation the other fixtures in
    // this file use. This is the production shape SurfaceComparer.Pair actually sees.
    [Fact]
    public void R120_Pair_resolves_production_style_grid_demo_screen_kind_to_hard_severity()
    {
        var win = new CaptureManifest
        {
            Platform = "windows", Shell = "wpf",
            Surfaces = { S("grid.demo", kind: "screen"), S("tab.Home", kind: "tab") },
        };
        var lin = new CaptureManifest
        {
            Platform = "linux", Shell = "avalonia",
            Surfaces = { S("grid.demo", kind: "screen"), S("tab.Home", kind: "tab") },
        };

        var pairs = SurfaceComparer.Pair(win, lin);

        var gridDemo = pairs.Single(p => p.Id == "grid.demo");
        gridDemo.Kind.Should().Be("screen");
        gridDemo.Severity.Should().Be(DiffSeverity.Hard, "grid.demo is the fidelity-critical grid surface, tagged kind=\"screen\" in production");

        // Sibling/no-regression: a genuinely chrome surface tagged the same way it is in
        // production (static ribbon tab) must stay Chrome, never gate-failing.
        var tabHome = pairs.Single(p => p.Id == "tab.Home");
        tabHome.Severity.Should().Be(DiffSeverity.Chrome, "tab.* surfaces are expected-by-design chrome differences, not a fidelity regression");
    }

    // R120 (end-to-end, mirrors the actual gate): a production-shaped manifest pair (explicit
    // Kind="screen" on grid.demo, exactly as ParityCapture.cs/MainWindow.ParityCapture.cs emit it)
    // whose two PNGs decode to drastically different pixels must fail ParityComparison.Passed and
    // appear in HardRegressions — this is the CLI gate's actual exit-code decision (Program.cs:125).
    [Fact]
    public void R120_ParityEngine_fails_gate_for_production_tagged_grid_surface_regression()
    {
        var win = new CaptureManifest
        {
            Platform = "windows", Shell = "wpf",
            Surfaces = { S("grid.demo", kind: "screen", png: "win-grid.png") },
        };
        var lin = new CaptureManifest
        {
            Platform = "linux", Shell = "avalonia",
            Surfaces = { S("grid.demo", kind: "screen", png: "lin-grid.png") },
        };

        // A fully-white 2x2 image vs a fully-black 2x2 image (both fully OPAQUE, alpha=0xFF —
        // an all-zero byte including alpha would be transparent and composite to white on both
        // sides, masking the diff): 100% mean-pixel-diff.
        var white = new PixelImage(2, 2, Enumerable.Repeat((byte)0xFF, 16).ToArray());
        var black = new PixelImage(2, 2, Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 0x00, 0x00, 0x00, 0xFF }).ToArray());

        var engine = new ParityComparisonEngine(
            decode: path => path.Contains("win-grid") ? white : black,
            exists: _ => true);

        var result = engine.Compare(win, lin, winDir: null, linDir: null, imagesDir: null, hardThreshold: 5.0);

        result.Passed.Should().BeFalse("grid.demo diverges far above the 5% hard threshold");
        result.HardRegressions.Should().ContainSingle(r => r.Id == "grid.demo");
    }
}
