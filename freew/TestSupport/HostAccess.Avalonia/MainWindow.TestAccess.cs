using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    internal NavigationPane NavPane => _navPane;
    internal ReviewingPane ReviewingPane => _reviewingPane;
    internal ReviewBalloonsPane ReviewBalloonsPane => _reviewBalloonsPane;
    internal RevealFormattingPane RevealPane => _revealPane;
    internal FreeWViewDepthMode ViewDepthMode => _viewSession.CurrentDepth.Mode;
    internal bool IsSplitPreviewActive => _viewSession.CurrentDepth.IsSplitActive;
    internal bool IsMultiplePagesPreviewActive => _viewSession.CurrentDepth.IsMultiplePagesActive;
    internal bool IsSideToSidePreviewActive => _viewSession.CurrentDepth.IsSideToSideActive;
    internal string? ViewDepthLimitation => _viewSession.CurrentDepth.Limitation;
    internal bool IsWorkspaceShowingLiveEditor => ReferenceEquals(_workspace.Child, _liveWorkspaceContent);

    /// <summary>
    /// Whether the live editor is on screen at all — directly under the workspace, or nested inside a
    /// preview surface it shares with a snapshot. Split preview puts the live content in row 0 of its own
    /// grid, so <see cref="IsWorkspaceShowingLiveEditor"/> (deliberately a DIRECT-child test, pinned by
    /// MainWindow_split_preview_uses_live_editor_and_read_only_snapshot) answers false there even though
    /// the editor is fully live — as the sibling cross-page-selection test proves by typing into it.
    /// </summary>
    internal bool IsLiveEditorAttachedForTests
    {
        get
        {
            for (var node = (StyledElement?)_liveWorkspaceContent; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, _workspace))
                    return true;
            }

            return false;
        }
    }
    internal bool IsWorkspaceShowingOutline => ReferenceEquals(_workspace.Child, _outlineView);

    internal FreeWViewDepthPagePairNavigationState SideToSideNavigationForTests =>
        _viewSession.PagePairNavigation;
    internal bool HasSideToSidePagePairNavigationForTests =>
        _sideToSidePreviewScrollViewer is not null &&
        _sideToSidePreviousPairButton is not null &&
        _sideToSideNextPairButton is not null &&
        _sideToSidePairStatusText is not null;
    internal Vector SideToSidePreviewOffsetForTests => new(_sideToSidePlannedHorizontalOffsetDip, 0);
    internal Control? WorkspaceContentForTests => _workspace.Child as Control;
    internal bool IsSideToSideEditorEditableForTests =>
        _viewSession.CurrentDepth.IsSideToSideActive && IsLiveEditorAttachedForTests;
    internal bool IsMultiplePagesEditorEditableForTests =>
        _viewSession.CurrentDepth.IsMultiplePagesActive && IsLiveEditorAttachedForTests;
    internal bool IsOutlineModeActiveForTests => _outlineMode;
    internal bool IsPagedEditModeActiveForTests => _pagedEditMode;
    internal void TogglePagedEditViewForTests() => TogglePagedEditView();
    internal OutlineView OutlineViewForTests => _outlineView;
    internal void ToggleOutlineViewForTests() => ToggleOutlineView();
    internal void NavigateSideToSideNextPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair);
    internal void NavigateSideToSidePreviousPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair);

    internal bool IsReadModeActiveForTests => _editorInteraction.IsReadModeActive;
    internal double ReadModeMaxWidthForTests => _editor.MaxWidth;
    internal string? ReadModeBackgroundForTests => _editor.ViewBackgroundColorHex;
    internal bool IsRibbonVisibleForTests => _ribbonHost?.IsVisible == true;
    internal bool IsTitleBarVisibleForTests => _titleBar.IsVisible;
    internal bool IsNavigationPaneVisibleForTests => _navPane.IsVisible;
    internal bool IsRevealPaneVisibleForTests => _revealPane.IsVisible;
    internal bool IsReviewingPaneVisibleForTests => _reviewingPane.IsVisible;
    internal int RibbonStateRefreshCountForTests => _ribbonStateRefreshCount;

    internal void SetReadModePaneVisibilityForTests(bool navigation, bool reveal, bool reviewing)
    {
        _navPane.IsVisible = navigation;
        _revealPane.IsVisible = reveal;
        _reviewingPane.IsVisible = reviewing;
    }

    internal void ToggleReadModeForTests() => ToggleReadMode();
    internal void ApplyReadModeColumnWidthForTests(string token) => ApplyReadModeColumnWidth(token);
    internal void ApplyReadModePageColorForTests(string token) => ApplyReadModePageColor(token);
    internal Task<bool> NewDocumentAsyncForTests() => NewDocumentAsync();
    internal Task<bool> ImportPdfTextAsyncForTests() => ImportPdfTextAsync();
    internal Task<bool> SaveForTests() => SaveAsync();
    internal Task ExportXpsForTests() => ExportXpsAsync();
    internal Task InsertScreenClipForTestAsync() => InsertScreenClipAsync();

    internal bool IsReadAloudActiveForTest => _readAloudSession?.IsActive == true;
    internal void ToggleReadAloudForTest() => ToggleReadAloud();
    internal bool RibbonKeyTipsVisibleForTest => _ribbonKeyTipsVisible;
    internal Control? RibbonControlForTest => _ribbonControl;
    internal IRibbonCommandRegistry? RibbonRegistryForTests => _ribbonRegistry;
    internal bool HasWindowIconForTests => Icon is not null;
    internal Border TitleBarForTests => _titleBar;
    internal IReadOnlyList<Button> QuickAccessButtonsForTests => _quickAccessButtons;
    internal IReadOnlyList<Control> StatusViewControlsForTests =>
        [_readModeSwitch, _printLayoutSwitch, _webLayoutSwitch, _draftSwitch, _pagedEditSwitch];
    internal string PageStatusForTests => _pageStatus.Text ?? string.Empty;
    internal string SectionStatusForTests => _sectionStatus.Text ?? string.Empty;
    internal string CountsStatusForTests => _status.Text ?? string.Empty;
    internal string PrintStatusForTests => _status.Text ?? string.Empty;
    internal MailMergeEngine MailMergeForTests => _mailMerge!;
    internal Task ExecuteFinishMergePlanForTests(MailMergeFinishPlan plan) => ExecuteFinishMergePlanAsync(plan);
    internal string DataFolderStatusForTests => _dataFolderStatus.Text ?? string.Empty;
    internal Slider ZoomSliderForTests => _zoomSlider;
    internal string ZoomLabelForTests => _zoomLabel.Text ?? string.Empty;
    internal void ApplyZoomForTests(double scale) => ApplyZoom(scale);
    internal void RaiseKeyDownForTest(KeyEventArgs args) => MainWindow_KeyDown(this, args);
    internal bool IsCloseDecisionPendingForTests => _closeCoordinator.IsClosePending;
    internal NotesPane NotesPaneForTest => _notesPane;
    internal ThesaurusPane ThesaurusPaneForTest => _thesaurusPane;

    // r148-startup-fileopen: exercises the exact production entry point Opened invokes, bypassing the
    // Opened/Show() event-timing headless tests avoid elsewhere in this suite (see
    // AutosaveAdapterWindowIsolationTests' OfferRecoveryAsync tests for the same pattern).
    internal bool StartupOpenFailedForTests => _startupOpenFailed;
    internal Task ShowStartupOpenFailureForTests() => ShowStartupOpenFailureIfAnyAsync();
}
