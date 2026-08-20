using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
#if FREEP_WINDOWS_CAPTURE
using Free.Shared.AppServices.Windows;
#endif
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Avalonia.Backstage;
using FreeP.App.Avalonia.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal PrintSelection? LastPrintSelectionForTests => _lastPrintSelection;

    internal bool RibbonKeyTipsVisibleForTests => _ribbonKeyTipsVisible;

    internal bool RibbonKeyTipMenuOpenForTests => _ribbonKeyTipMenuItems is not null;

    internal bool RibbonKeyTipFlyoutOpenForTests => _ribbonKeyTipFlyout?.IsOpen == true;

    internal bool SlideCanvasFocusedForTests => _slideCanvas.IsFocused;

    internal IReadOnlyList<MenuItem> RibbonKeyTipRenderedMenuItemsForTests =>
        _ribbonKeyTipRenderedMenuItems ?? Array.Empty<MenuItem>();

    internal void SetRibbonKeyTipMenuScopeForTests(RibbonMenu menu, MenuFlyout flyout)
    {
        _ribbonKeyTipsVisible = true;
        _ribbonKeyTipMenuItems = menu.Items;
        _ribbonKeyTipFlyout = flyout;
        _ribbonKeyTipRenderedMenuItems = flyout.Items.OfType<MenuItem>().ToArray();
        _ribbonKeyTipSequence = string.Empty;
    }

    internal bool HandleRibbonMenuKeyTipForTests(string token) => TryHandleRibbonMenuKeyTip(token);

    internal RibbonCommandRegistry RibbonCommandRegistryForTests => _ribbonCommandRegistry!;

    internal Control? RibbonControlForTests => _ribbonControl;

    internal Border TitleBarForTests => _titleBar;

    internal IReadOnlyList<Button> QuickAccessButtonsForTests => _quickAccessButtons;

    internal string StatusTextForTests => _statusText.Text ?? string.Empty;

    internal bool HasWindowIconForTests => Icon is not null;

    internal int OwnerFocusRestoreCountForTests => _ownerFocusRestoreCount;

    internal void RaiseKeyDownForTests(KeyEventArgs args) => MainWindow_KeyDown(this, args);

    internal Task ClipboardOperationForTests => _clipboardOperationQueue.Completion;

    internal Button SlidePaneNewSlideButtonForTests => _slidePaneNewSlideButton;

    internal IReadOnlyList<string?> SelectionPaneRenameToolTipsForTests => _selectionPane.RenameToolTipsForTests;

    internal bool IsShellShortcutTargetForTests(Control? focused) => IsShellShortcutTarget(focused);

    internal ListBoxItem? SelectedSlidePaneItemForTests => GetCurrentSlidePaneItem();

    internal IReadOnlyList<int> SlidePaneSelectedSlideIndicesForTests =>
        _workareaSession.SlidePaneSession.Selection.SelectedSlideIndices;

    internal IReadOnlyList<string?> SlidePaneSectionHeaderAutomationNamesForTests => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is SlidePaneSectionHeaderTag)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<string?> SlidePaneThumbnailAutomationNamesForTests => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is int)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal bool IsCloseDecisionPendingForTests => _closeCoordinator.IsClosePending;

    internal PresentationViewShowState ViewShowStateForTests => _viewShowState;

    internal PresentationViewZoomState ViewZoomStateForTests => _viewZoomState;

    internal PresentationViewZoomState SlideCanvasViewZoomStateForTests => _slideCanvas.ViewZoomState;

    internal bool? GestureSnapToGridForTests => _gestureHandler?.SnapToGrid;

    internal bool? GestureSnapToShapesForTests => _gestureHandler?.SnapToShapes;

    internal PrinterDiscoveryResult? LatestPrinterDiscoveryForTests => _latestPrinterDiscovery;

    internal bool NativeOutputDetectionStartedForTests => _nativeOutputDetectionStarted;

    internal PresentationNativePrintHandoffHostCapabilities NativePrintHostCapabilitiesForTests => _nativePrintHostCapabilities;

    internal PresentationVideoExportHandoffHostCapabilities VideoExportHostCapabilitiesForTests => _videoExportHostCapabilities;

    internal void StartNativeOutputCapabilityDetectionForTests() => StartNativeOutputCapabilityDetection();

    internal bool InvokeReviewCommentPaneMentionActionForTests(string tag, string? candidateLabel = null)
    {
        var button = EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (button is null)
            return false;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var item = button.ContextMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(candidate => candidateLabel is null ||
                string.Equals(candidate.Header as string, candidateLabel, StringComparison.Ordinal));
        if (item is null)
            return candidateLabel is null;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }

    // r154 remediation (M2): mirrors FreeP.App.Host's ClickNewCommentButtonForTests/
    // ClickReplyButtonForTests (freep/TestSupport/HostAccess.Wpf/MainWindow.ExtendedTestAccess.cs)
    // so a test can drive the real "New Comment"/"Reply" button.Click handlers built by
    // BuildAddCommentInput/BuildReviewCommentCard on the Avalonia shell too, instead of calling the
    // AddComment/ReplyToSelectedComment wrapper methods directly. Lives HERE, not in the shipping
    // MainWindow.cs, because HostAccessOwnershipTests scans the shipping project (and its built
    // Release assembly) for the "ForTests" token.
    internal bool ClickNewCommentButtonForTests(string text) =>
        ClickReviewCommentPaneButtonForTests(PresentationPaneTextResources.NewCommentCommand, text);

    internal bool ClickReplyButtonForTests(string text) =>
        ClickReviewCommentPaneButtonForTests(PresentationPaneTextResources.ReplyCommand, text);

    // r154 remediation (N2): drives the real "@" mention button.Click handler built by
    // BuildCommentMentionButton (rather than calling DispatchCommentMentionPicker directly) so a
    // test can prove the button's own currentAuthor wiring on the single-candidate auto-apply
    // route -- not just that the session/planner stamp the author correctly when given one.
    internal bool ClickCommentMentionButtonForTests(string tag, string text)
    {
        var button = EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (button?.Parent is not Panel row)
            return false;

        var input = row.Children.OfType<TextBox>().FirstOrDefault();
        if (input is null)
            return false;

        input.Text = text;
        input.CaretIndex = text.Length;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    private bool ClickReviewCommentPaneButtonForTests(string caption, string text)
    {
        var button = EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .FirstOrDefault(candidate => Equals(candidate.Content, caption));
        if (button?.Parent is not Panel row)
            return false;

        var input = row.Children.OfType<TextBox>().FirstOrDefault();
        if (input is null)
            return false;

        input.Text = text;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal bool EditPointsEnabledForTests => _slideCanvas.EditPointsEnabled;

    internal IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> PaneAccessibilitySnapshotForTests =>
        _paneAccessibility.BuildSnapshot();

    internal string PaneAccessibilitySnapshotSerializationForTests =>
        _paneAccessibility.SerializeSnapshot();

    internal bool ApplyPrintCustomRangeForTests(string rangeText)
    {
        if (_printCustomRangeInput is null || _printCustomRangeApplyButton is null)
            return false;

        _printCustomRangeInput.Text = rangeText;
        _printCustomRangeApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

}
