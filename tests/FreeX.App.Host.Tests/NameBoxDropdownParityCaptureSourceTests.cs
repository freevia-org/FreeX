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
        physicalEvidence.Should().Contain("68000000-0000-0000-0000-000000000001");
        physicalEvidence.Should().Contain("68000000-0000-0000-0000-000000000004");
        physicalEvidence.Should().Contain("SeedNameBoxDropdownPhysicalFixture");
        physicalEvidence.Should().Contain("67000000-0000-0000-0000-000000000001");
        physicalEvidence.Should().Contain("67000000-0000-0000-0000-000000000004");
        physicalEvidence.Should().Contain("partial void CompleteOptionalStartupFileOpen()");
        physicalEvidence.Should().Contain("RefreshShell(_statusText.Text ?? \"Ready\")");
        avaloniaSource.Should().Contain("Width = NameBoxDropdownWidth");
        avaloniaSource.Should().Contain("MaxHeight = NameBoxDropdownMaxHeight");
        avaloniaSource.Should().Contain("CellAddressAutocompleteOverlay");
        avaloniaSource.Should().Contain("if (!ReferenceEquals(_session, sessionBeforeOpen))");
        avaloniaSource.Should().Contain("CompleteOptionalStartupFileOpen()");
        avaloniaSource.Should().Contain("Placement = PlacementMode.BottomEdgeAlignedLeft");
        avaloniaSource.Should().Contain("IsLightDismissEnabled = true");
        avaloniaSource.Should().Contain("PointToScreen(new Point(0, 0))");
        avaloniaSource.Should().Contain("RecordOptionalNameBoxPopupOpened");
        // WPF's Name Box drop-down is a separate top-level popup window, so the Avalonia list must
        // be a native Popup (not the main window's overlay layer) wherever the platform has one.
        avaloniaSource.Should().Contain("_cellAddressAutocompletePopup = new Popup");
        avaloniaSource.Should().Contain("ShouldUseOverlayLayer = false");
        avaloniaSource.Should().Contain("PlacementTarget = _cellAddressDropDownButton");
        avaloniaSource.Should().Contain("\"native-x11-popup-root\"");
        avaloniaSource.Should().NotContain("_cellAddressAutocompleteOverlay");
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
        probe.Should().Contain("read_name_box_popup");
        probe.Should().Contain("\"native-x11-root-crop\"");
        probe.Should().Contain("x11-root-crop-overlay-layer");
        // The native popup is identified as the one new 208px-wide X11 window between the
        // before/open inventories, not by the host string the app reported.
        probe.Should().Contain("\"$popup_host\" == \"native-x11-popup-root\"");
        probe.Should().Contain("if window_id in before:");
        probe.Should().Contain("fields.get(\"WIDTH\") == \"208\"");
        probe.Should().Contain("-crop \"208x136+${popup_x}+${popup_y}\" +repage");
        probe.Should().NotContain("-resize 208x136");
    }
}
