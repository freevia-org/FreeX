using System.Windows;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonKeyTipOverlayPlacementTests
{
    [Fact]
    public void PlaceBadge_ClampsBadgeInsideOverlayBounds()
    {
        var elementBounds = new Rect(1268, 710, 24, 20);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(30, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(1250);
        point.Y.Should().Be(704);
    }

    [Fact]
    public void PlaceBadge_WhenBadgeIsLargerThanOverlay_ClampsToOrigin()
    {
        var elementBounds = new Rect(8, 8, 12, 12);
        var overlaySize = new Size(20, 12);
        var badgeSize = new Size(30, 18);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(0);
        point.Y.Should().Be(0);
    }

    [Fact]
    public void PlaceBadge_CentersBadgeNearTopOfElementWhenThereIsRoom()
    {
        var elementBounds = new Rect(100, 40, 80, 24);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(24, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(128);
        point.Y.Should().Be(56);
    }

    [Fact]
    public void PlaceBadge_AnchorsTallRibbonCommandsNearLowerCenter()
    {
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(243);
        point.Y.Should().Be(178);
    }

    [Fact]
    public void PlaceBadge_SnapsFractionalCoordinatesToWholePixels()
    {
        var elementBounds = new Rect(100.5, 40.25, 81, 24.5);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(25, 15.5);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(129);
        point.Y.Should().Be(57);
    }

    [Fact]
    public void PlaceBadge_DefaultKindOverloadMatchesCommandKind()
    {
        // The kind-less overload must preserve the original Command placement so
        // existing command badges are unchanged.
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var legacy = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);
        var command = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);

        command.Should().Be(legacy);
    }

    [Fact]
    public void PlaceBadge_CommandKindAnchorsTallRibbonCommandsNearLowerCenter()
    {
        // Same expectation as the kind-less PlaceBadge_AnchorsTallRibbonCommandsNearLowerCenter
        // test: Command kind must keep the bottom-edge straddle behavior.
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);

        point.X.Should().Be(243);
        point.Y.Should().Be(178);
    }

    [Fact]
    public void PlaceBadge_CommandKindPlacesCompactTitleBarCommandsBelowControl()
    {
        // QAT buttons live in the title bar and should show keytips below the
        // icon, matching Excel's top-level Alt overlay.
        var elementBounds = new Rect(36, 3, 26, 22);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(18, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);

        point.X.Should().Be(40);
        point.Y.Should().Be(27);
    }

    [Fact]
    public void PlaceBadge_CommandKindKeepsCompactRibbonCommandsOnBottomEdge()
    {
        // Compact ribbon-row commands outside the title bar keep the command
        // bottom-edge straddle anchor; only QAT-like title-bar commands move below.
        var elementBounds = new Rect(100, 64, 28, 22);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(18, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);

        point.X.Should().Be(105);
        point.Y.Should().Be(78);
    }

    [Fact]
    public void PlaceBadge_CollapsedGroupKindCentersBadgeInsideTallOverflowControl()
    {
        // Collapsed ribbon groups are tall overflow buttons. Their badges should
        // sit in the button center instead of straddling the caption/chevron edge.
        var elementBounds = new Rect(220, 96, 52, 76);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(28, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.CollapsedGroup);

        point.X.Should().Be(232);
        point.Y.Should().Be(126);
    }

    [Fact]
    public void PlaceBadge_CollapsedGroupKindClampsCenteredBadgeInsideOverlayBounds()
    {
        var elementBounds = new Rect(2, -40, 52, 76);
        var overlaySize = new Size(60, 60);
        var badgeSize = new Size(28, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.CollapsedGroup);

        point.X.Should().Be(14);
        point.Y.Should().Be(0);
    }

    [Fact]
    public void PlaceBadge_ComboBoxKindCentersBadgeBelowSelectorWithGap()
    {
        // Combo-box keytips sit below the selector frame so the badge does not
        // cover the editable value or drop-down arrow.
        var elementBounds = new Rect(300, 96, 116, 22);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(18, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.ComboBox);

        point.X.Should().Be(349);
        point.Y.Should().Be(120);
    }

    [Fact]
    public void PlaceBadge_ComboBoxKindDiffersFromCommandKindVertically()
    {
        var elementBounds = new Rect(300, 96, 116, 22);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(18, 16);

        var command = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);
        var comboBox = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.ComboBox);

        comboBox.X.Should().Be(command.X);
        comboBox.Y.Should().BeGreaterThan(command.Y);
    }

    [Fact]
    public void PlaceBadge_DropdownCommandKindCentersBadgeBelowCommandFrameWithGap()
    {
        // Dropdown and split-command keytips sit below the full control frame
        // so the badge does not cover the caption, chevron, or value area.
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.DropdownCommand);

        point.X.Should().Be(243);
        point.Y.Should().Be(188);
    }

    [Fact]
    public void PlaceBadge_DropdownCommandKindDiffersFromCommandKindVertically()
    {
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var command = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);
        var dropdown = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.DropdownCommand);

        dropdown.X.Should().Be(command.X);
        dropdown.Y.Should().BeGreaterThan(command.Y);
    }

    [Fact]
    public void PlaceBadge_TabKindCentersBadgeBelowTabWithGap()
    {
        // Tab keytips sit centered just below the tab label rather than straddling
        // the bottom edge. Bottom edge = 30 + 24 = 54, plus the 2px gap = 56.
        // X is centered the same way as Command: 120 + 80/2 - 24/2 = 148.
        var elementBounds = new Rect(120, 30, 80, 24);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(24, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Tab);

        point.X.Should().Be(148);
        point.Y.Should().Be(56);
    }

    [Fact]
    public void PlaceBadge_TabKindDiffersFromCommandKindVertically()
    {
        // The Tab anchor (below the edge + gap) must sit lower than the regular
        // ribbon Command straddle (bottom edge - half the badge height) for the same element.
        var elementBounds = new Rect(120, 40, 80, 24);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(24, 16);

        var command = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);
        var tab = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Tab);

        tab.X.Should().Be(command.X);
        tab.Y.Should().BeGreaterThan(command.Y);
    }

    [Fact]
    public void PlaceBadge_TabKindClampsBadgeInsideOverlayBounds()
    {
        // A tab near the bottom edge would push the below-anchored badge off the
        // overlay; it must clamp to the bottom of the overlay (720 - 16 = 704).
        var elementBounds = new Rect(1268, 710, 24, 20);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(30, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Tab);

        point.X.Should().Be(1250);
        point.Y.Should().Be(704);
    }

    [Fact]
    public void PlaceBadge_TabKindSnapsFractionalCoordinatesToWholePixels()
    {
        // Tab kind must round to whole pixels just like Command kind.
        // X = 100.5 + 81/2 - 25/2 = 128.5 -> 129 (away from zero).
        // Y = 40.25 + 24.5 + 2 (gap) = 66.75 -> 67.
        var elementBounds = new Rect(100.5, 40.25, 81, 24.5);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(25, 15.5);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
            elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Tab);

        point.X.Should().Be(129);
        point.Y.Should().Be(67);
    }
}
