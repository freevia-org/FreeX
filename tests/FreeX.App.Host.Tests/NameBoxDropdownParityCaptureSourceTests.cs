using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class NameBoxDropdownParityCaptureSourceTests
{
    [Fact]
    public void WpfParityCapture_UsesTheScreenshotTourFixtureAndProductionPopup()
    {
        var capture = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");
        var helper = DialogSourceTestSupport.ReadHostSources("MainWindow.NameBoxParityCapture.cs");

        capture.Should().Contain("popup.nameBoxDropdown");
        capture.Should().Contain("\"overlay\"");
        capture.Should().Contain("RenderElementOnBackground");
        capture.Should().Contain("NameBoxDropdownParityCaptureWidth");
        capture.Should().Contain("NameBoxDropdownParityCaptureHeight");
        capture.Should().Contain("wpf-production-popup-render-target");
        capture.Should().Contain(
            "string.Equals(targetSurfaceId, \"popup.nameBoxDropdown\", StringComparison.Ordinal)");

        helper.Should().Contain("EnsureFormulaBarNameBoxTourContext");
        helper.Should().Contain("CellAddressBox.IsDropDownOpen = true");
        helper.Should().Contain("FindOpenPopupChild(CellAddressBox)");
    }

    [Fact]
    public void WpfParityCapture_UsesOneFixedNormalizedFrame()
    {
        var helper = DialogSourceTestSupport.ReadHostSources("MainWindow.NameBoxParityCapture.cs");

        helper.Should().Contain("NameBoxDropdownParityCaptureWidth = 208");
        helper.Should().Contain("NameBoxDropdownParityCaptureHeight = 136");
        helper.Should().Contain("internal void CloseNameBoxDropdownForParityCapture");
    }

    [Fact]
    public void AvaloniaParityCapture_KeepsPhysicalFixtureSeparate()
    {
        var capture = WorkspaceFileLocator.ReadAllText("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs");
        var physicalEvidence = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.NameBoxPhysicalEvidence.cs");
        var avaloniaSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var rendererAccess = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Avalonia", "MainWindow.RendererAccess.cs");

        capture.Should().Contain("SeedNameBoxDropdownParityFixture");
        capture.Should().Contain("68000000-0000-0000-0000-000000000001");
        capture.Should().Contain("68000000-0000-0000-0000-000000000004");
        physicalEvidence.Should().Contain("SeedNameBoxDropdownPhysicalFixture");
        physicalEvidence.Should().Contain("67000000-0000-0000-0000-000000000001");
        physicalEvidence.Should().Contain("67000000-0000-0000-0000-000000000004");
        avaloniaSource.Should().NotContain("SeedNameBoxDropdownPhysicalFixture");
        avaloniaSource.Should().Contain("Width = NameBoxDropdownWidth");
        avaloniaSource.Should().Contain("Height = NameBoxDropdownHeight");
        rendererAccess.Should().Contain("NameBoxDropdownWidth = 208");
        rendererAccess.Should().Contain("NameBoxDropdownHeight = 136");
    }

    [Fact]
    public void AvaloniaParityCapture_RejectsSyntheticPopupEvidenceAndRequiresNativeX11()
    {
        var capture = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs");
        var parityManifest = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Avalonia", "Capture", "ParityCapture.cs");
        var probe = WorkspaceFileLocator.ReadAllText(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");

        capture.Should().NotContain("CreateNameBoxDropdownParitySnapshot");
        capture.Should().Contain("managed-popup-diagnostic");
        capture.Should().Contain("Captured: false");
        parityManifest.Should().Contain("evidenceProvenance");
        probe.Should().Contain("probe_name_box_dropdown_parity");
        probe.Should().Contain("\"native-x11-root-crop\"");
        probe.Should().Contain("-crop \"208x136+${popup_x}+${popup_y}\" +repage");
        probe.Should().NotContain("-resize 208x136");
    }
}
