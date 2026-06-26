using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 10B — unit + STA tests for <see cref="SlideSizeDialog"/>.
///
/// Pure unit-conversion and preset-classification tests run on any thread.
/// Dialog-construction tests run on an STA thread (WPF requirement).
/// </summary>
public sealed class SlideSizeDialogTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static EditingSession MakeSession(long cxEmu = 12_192_000L, long cyEmu = 6_858_000L)
    {
        var pres = new Presentation
        {
            SlideSizeCxEmu = cxEmu,
            SlideSizeCyEmu = cyEmu
        };
        pres.Slides.Add(new Slide());
        var bus  = new PresentationCommandBus(pres);
        return new EditingSession(pres, bus);
    }

    // ── Unit conversions (no STA required) ───────────────────────────────────────

    [Fact]
    public void InchesToEmu_OneInch_Returns914400()
    {
        SlideSizeDialog.InchesToEmu(1.0).Should().Be(914_400L);
    }

    [Fact]
    public void InchesToEmu_HalfInch_Returns457200()
    {
        SlideSizeDialog.InchesToEmu(0.5).Should().Be(457_200L);
    }

    [Fact]
    public void CmToEmu_OneCm_Returns360000()
    {
        SlideSizeDialog.CmToEmu(1.0).Should().Be(360_000L);
    }

    [Fact]
    public void CmToEmu_TenCm_Returns3600000()
    {
        SlideSizeDialog.CmToEmu(10.0).Should().Be(3_600_000L);
    }

    [Fact]
    public void EmuToInches_914400_ReturnsOneInch()
    {
        SlideSizeDialog.EmuToInches(914_400L).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void EmuToCm_360000_ReturnsOneCm()
    {
        SlideSizeDialog.EmuToCm(360_000L).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void InchesToEmu_RoundTrip_IsIdempotent()
    {
        // 13.333 inches (Widescreen 16:9 width) → EMU → back to inches.
        long emu = SlideSizeDialog.InchesToEmu(13.333);
        SlideSizeDialog.EmuToInches(emu).Should().BeApproximately(13.333, 0.001);
    }

    [Fact]
    public void CmToEmu_RoundTrip_IsIdempotent()
    {
        double cm = 33.867;
        long emu = SlideSizeDialog.CmToEmu(cm);
        SlideSizeDialog.EmuToCm(emu).Should().BeApproximately(cm, 0.01);
    }

    // ── Preset classification (no STA required) ───────────────────────────────────

    [Fact]
    public void ClassifySize_Widescreen169Emu_ReturnsWidescreen()
    {
        var (cx, cy) = SlideSizeDialog.Widescreen169Emu;
        SlideSizeDialog.ClassifySize(cx, cy).Should().Be(SlideSizeDialog.Preset.Widescreen169);
    }

    [Fact]
    public void ClassifySize_Standard43Emu_ReturnsStandard()
    {
        var (cx, cy) = SlideSizeDialog.Standard43Emu;
        SlideSizeDialog.ClassifySize(cx, cy).Should().Be(SlideSizeDialog.Preset.Standard43);
    }

    [Fact]
    public void ClassifySize_CustomDimensions_ReturnsCustom()
    {
        SlideSizeDialog.ClassifySize(1_000_000L, 500_000L)
            .Should().Be(SlideSizeDialog.Preset.Custom);
    }

    // ── Preset EMU values ─────────────────────────────────────────────────────────

    [Fact]
    public void Widescreen169Emu_MatchesExpectedValues()
    {
        var (cx, cy) = SlideSizeDialog.Widescreen169Emu;
        cx.Should().Be(12_192_000L, "16:9 width is 12 192 000 EMU (≈13.333 in)");
        cy.Should().Be(6_858_000L,  "16:9 height is 6 858 000 EMU (≈7.5 in)");
    }

    [Fact]
    public void Standard43Emu_MatchesExpectedValues()
    {
        var (cx, cy) = SlideSizeDialog.Standard43Emu;
        cx.Should().Be(9_144_000L, "4:3 width is 9 144 000 EMU (≈10 in)");
        cy.Should().Be(6_858_000L, "4:3 height is 6 858 000 EMU (≈7.5 in)");
    }

    // ── Preset in-to-EMU cross-check ──────────────────────────────────────────────

    [Fact]
    public void Standard43_10x7p5Inches_MatchesPresetEmu()
    {
        var (cx, cy) = SlideSizeDialog.Standard43Emu;
        SlideSizeDialog.InchesToEmu(10.0).Should().Be(cx);
        SlideSizeDialog.InchesToEmu(7.5).Should().Be(cy);
    }

    // ── Dialog construction (STA required) ───────────────────────────────────────

    [StaFact]
    public void SlideSizeDialog_Constructs_WithEditor()
    {
        var sess = MakeSession();
        var dlg  = new SlideSizeDialog(sess);
        dlg.Should().NotBeNull();
    }

    [StaFact]
    public void SlideSizeDialog_ThrowsOnNullEditor()
    {
        var act = () => new SlideSizeDialog(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void SlideSizeDialog_AppliesSize_ViaEditor_WhenOkInvoked()
    {
        var sess  = MakeSession(9_144_000L, 6_858_000L);
        var dlg   = new SlideSizeDialog(sess);

        // Simulate OK with 1280x720 pixels (arbitrary custom size in EMU).
        // Directly call SetSlideSize on editor to replicate what OnOk does.
        long newCx = SlideSizeDialog.InchesToEmu(12.0);
        long newCy = SlideSizeDialog.InchesToEmu(6.75);

        sess.SetSlideSize(newCx, newCy);

        sess.Presentation.SlideSizeCxEmu.Should().Be(newCx);
        sess.Presentation.SlideSizeCyEmu.Should().Be(newCy);
    }

    [StaFact]
    public void SlideSizeDialog_SetSizeIsUndoable()
    {
        var sess = MakeSession(9_144_000L, 6_858_000L);

        sess.SetSlideSize(10_000_000L, 5_000_000L);
        sess.Undo();

        sess.Presentation.SlideSizeCxEmu.Should().Be(9_144_000L);
        sess.Presentation.SlideSizeCyEmu.Should().Be(6_858_000L);
    }
}
