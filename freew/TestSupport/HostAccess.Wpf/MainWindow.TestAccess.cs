using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host;

public sealed partial class MainWindow
{
    internal FreeWViewDepthPagePairNavigationState SideToSideNavigationForTests =>
        _viewSession.PagePairNavigation;
    internal bool HasSideToSideEditablePageSurfaceForTests =>
        _viewSession.CurrentDepth.IsSideToSideActive && _editablePaginatedPanel is not null;
    internal bool HasMultiplePagesEditablePageSurfaceForTests =>
        _viewSession.CurrentDepth.IsMultiplePagesActive && _editablePaginatedPanel is not null;
    internal PaginatedEditorPanel? EditablePaginatedPanelForTests => _editablePaginatedPanel;
    internal bool HasSideToSidePagePairNavigationForTests =>
        _sideToSidePreviousPairButton is not null &&
        _sideToSideNextPairButton is not null &&
        _sideToSidePairStatusText is not null;
    internal DocumentView? SplitEditorForTests => _splitEditor;

    internal void NavigateSideToSideNextPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair);

    internal void NavigateSideToSidePreviousPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair);

    internal bool IsReadModeActiveForTests => _editorInteraction.IsReadModeActive;
    internal double ReadModeMaxWidthForTests => _editor.MaxWidth;
    internal string ReadModeColumnWidthForTests => _editorInteraction.ReadModeColumnWidth;
    internal string ReadModePageColorForTests => _editorInteraction.ReadModePageColor;
    internal bool IsTitleBarVisibleForTests => _titleBar.Visibility == Visibility.Visible;
    internal bool IsRibbonVisibleForTests => _ribbon.Visibility == Visibility.Visible;
    internal bool IsNavigationPaneVisibleForTests => _navPane.Visibility == Visibility.Visible;
    internal bool IsRevealPaneVisibleForTests => _revealPane.Visibility == Visibility.Visible;
    internal bool IsReviewingPaneVisibleForTests => _reviewPane.Visibility == Visibility.Visible;

    internal void SetReadModePaneVisibilityForTests(bool navigation, bool reveal, bool reviewing)
    {
        _navPaneVisible = navigation;
        _revealPaneVisible = reveal;
        _reviewPaneVisible = reviewing;
        _navPane.Visibility = navigation ? Visibility.Visible : Visibility.Collapsed;
        _revealPane.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        _reviewPane.Visibility = reviewing ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ToggleReadModeForTests() => ToggleReadMode();
    internal void ApplyReadModeColumnWidthForTests(string token) => ApplyReadModeColumnWidth(token);
    internal void ApplyReadModePageColorForTests(string token) => ApplyReadModePageColor(token);
}
