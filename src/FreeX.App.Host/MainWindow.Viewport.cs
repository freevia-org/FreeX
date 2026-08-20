using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Scrolling the grid moves the column header the AutoFilter flyout is anchored to without
        // changing window activation, so dismiss the flyout here (its own deactivation handler covers
        // click-away cases). Scrolling within the flyout's own list does not raise this event.
        CloseAutoFilterDropdown();
        UpdateViewport();
        BroadcastScrollOffsetToSideBySidePartner();
    }

    private void VerticalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(VerticalScroll, GetScrollableRowLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void HorizontalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(HorizontalScroll, GetScrollableColumnLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void ScrollBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollBar scrollBar ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<RepeatButton>(source) is not { } button)
            return;

        var isForwardLineButton =
            scrollBar.Orientation == Orientation.Vertical && Equals(button.Command, ScrollBar.LineDownCommand) ||
            scrollBar.Orientation == Orientation.Horizontal && Equals(button.Command, ScrollBar.LineRightCommand);
        if (!isForwardLineButton)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var absoluteLimit = scrollBar.Orientation == Orientation.Vertical
            ? GetScrollableRowLimit(sheet)
            : GetScrollableColumnLimit(sheet);
        if (!TryExtendScrollRangeFromScrollbarArrow(scrollBar, absoluteLimit))
            return;

        e.Handled = true;
    }

    private static void ExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit);
    }

    private static bool TryExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        return ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit);
    }

    private void SheetGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int notches = WorkbookViewportScrollPlanner.NormalizeWheelNotches(e.Delta);
        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (SheetGrid.Viewport is { } wheelViewport)
        {
            var wheelPos = e.GetPosition(SheetGrid);
            var wheelTarget = FreeX.App.UI.GridView.ResolveSplitPaneWheelTarget(
                wheelViewport,
                _currentSheetId,
                wheelPos,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight,
                horizontal);
            _activeSplitPaneRegion = wheelTarget.Region;
            horizontal = wheelTarget.Horizontal;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+Scroll = zoom
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + notches * 10));
            e.Handled = true;
            return;
        }

        if (SheetGrid.Viewport?.SplitPanes is not null &&
            !FreeX.App.UI.GridView.CanScrollSplitPaneRegion(_activeSplitPaneRegion, horizontal))
        {
            e.Handled = true;
            return;
        }

        if (TryScrollIndependentSplitPane(horizontal, notches))
        {
            e.Handled = true;
            return;
        }

        if (horizontal)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var step = WorkbookViewportScrollPlanner.NormalizeWheelScrollStep(
                GetSystemWheelScrollLines(),
                HorizontalScroll.ViewportSize);
            var (maximum, value) = CalculateWheelScroll(
                HorizontalScroll.Value,
                HorizontalScroll.Maximum,
                notches,
                step,
                HorizontalScroll.ViewportSize,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Maximum = maximum;
            HorizontalScroll.Value = value;
        }
        else
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var step = WorkbookViewportScrollPlanner.NormalizeWheelScrollStep(
                GetSystemWheelScrollLines(),
                VerticalScroll.ViewportSize);
            var (maximum, value) = CalculateWheelScroll(
                VerticalScroll.Value,
                VerticalScroll.Maximum,
                notches,
                step,
                VerticalScroll.ViewportSize,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Maximum = maximum;
            VerticalScroll.Value = value;
        }
        e.Handled = true;
    }

    private static int GetSystemWheelScrollLines()
    {
        int? externalLines = null;
        TryGetExternalWheelScrollLines(ref externalLines);
        if (externalLines is { } lines)
            return lines;

        try
        {
            return SystemParameters.WheelScrollLines;
        }
        catch
        {
            // SystemParameters can throw in atypical hosting scenarios (e.g. no desktop session);
            // fall back to the previous hardcoded behavior rather than letting the wheel handler fail.
            return WorkbookViewportScrollPlanner.DefaultWheelScrollLinesPerNotch;
        }
    }

    private void OnAutofillEdgeScrollRequested(FreeX.App.Presentation.GridInteraction.GridAutoScrollRequest request)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (request.HorizontalDirection != 0)
        {
            var (maximum, value) = WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
                HorizontalScroll.Value,
                HorizontalScroll.Maximum,
                request.HorizontalDirection,
                step: 1,
                HorizontalScroll.ViewportSize,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Maximum = maximum;
            HorizontalScroll.Value = value;
        }

        if (request.VerticalDirection != 0)
        {
            var (maximum, value) = WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
                VerticalScroll.Value,
                VerticalScroll.Maximum,
                request.VerticalDirection,
                step: 1,
                VerticalScroll.ViewportSize,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Maximum = maximum;
            VerticalScroll.Value = value;
        }
    }

    private bool TryScrollIndependentSplitPane(bool horizontal, int notches)
    {
        // TopRight (columns) and BottomLeft (rows) share exactly the SAME scrollbar as the main
        // (bottom-right) pane in Excel's split model -- top-right/bottom-right always show
        // identical columns, and bottom-left/bottom-right always show identical rows. Neither has
        // an independent scroll offset of its own (r56 fix: the old sticky TopRightLeftCol/
        // BottomLeftTopRow offset let these two panes desync from the main pane permanently, with
        // no way to ever resync). Always fall through to the normal main-scrollbar wheel handling
        // in SheetGrid_MouseWheel, which keeps them locked together by construction.
        return false;
    }

    private void OnSplitPaneScrollbarScrolled(FreeX.App.UI.SplitPaneScrollbarScrollTarget target)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return;

        // The TopRight/BottomLeft scrollbar chrome shares the SAME scroll position as the main
        // (bottom-right) pane in Excel's split model (see TryScrollIndependentSplitPane) -- route
        // a drag/click on that chrome straight into the main scrollbar rather than a separate
        // sticky per-pane offset, so the two panes in that band can never desync.
        if (target is
            {
                Region: FreeX.App.UI.SplitPaneRegion.TopRight,
                Orientation: FreeX.App.UI.SplitPaneScrollbarOrientation.Horizontal
            })
        {
            HorizontalScroll.Maximum = Math.Max(HorizontalScroll.Maximum, target.Index);
            HorizontalScroll.Value = target.Index;
            return;
        }

        if (target is
            {
                Region: FreeX.App.UI.SplitPaneRegion.BottomLeft,
                Orientation: FreeX.App.UI.SplitPaneScrollbarOrientation.Vertical
            })
        {
            VerticalScroll.Maximum = Math.Max(VerticalScroll.Maximum, target.Index);
            VerticalScroll.Value = target.Index;
        }
    }

    private void EnsureCellVisible(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp == null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);

        var plan = WorkbookViewportScrollPlanner.PlanCellReveal(
            vp,
            sheet,
            addr,
            VerticalScroll.Maximum,
            HorizontalScroll.Maximum);
        if (plan.Vertical.ShouldScroll)
        {
            VerticalScroll.Maximum = plan.Vertical.Maximum;
            VerticalScroll.Value = plan.Vertical.Value;
        }

        if (plan.Horizontal.ShouldScroll)
        {
            HorizontalScroll.Maximum = plan.Horizontal.Maximum;
            HorizontalScroll.Value = plan.Horizontal.Value;
        }

        // The target cell may be out of view in the bottom-left/top-right pane; since that pane
        // shares its scroll position with the main pane (see TryScrollIndependentSplitPane), reveal
        // it by moving the SAME main scrollbar rather than a separate sticky per-pane offset --
        // bottom-left/top-right have no independent scroll offset of their own (r56 fix).
        if (plan.BottomLeftTopRow is { } newBottomLeftTopRow)
        {
            VerticalScroll.Maximum = Math.Max(VerticalScroll.Maximum, newBottomLeftTopRow);
            VerticalScroll.Value = newBottomLeftTopRow;
        }

        if (plan.TopRightLeftCol is { } newTopRightLeftCol)
        {
            HorizontalScroll.Maximum = Math.Max(HorizontalScroll.Maximum, newTopRightLeftCol);
            HorizontalScroll.Value = newTopRightLeftCol;
        }
    }

    private readonly record struct TableContextRefreshKey(
        Sheet? Sheet,
        GridRange? SelectedRange,
        ulong NavigationRevision,
        Visibility TableDesignVisibility);

    private readonly record struct ChartContextRefreshKey(
        Sheet? Sheet,
        Guid SelectedObjectId,
        FreeX.App.UI.ObjectKind SelectedObjectKind,
        bool HasVisibleNormalChart,
        ulong NavigationRevision,
        Visibility ChartDesignVisibility,
        Visibility ChartFormatVisibility);

    private readonly record struct DrawingObjectContextRefreshKey(
        Sheet? Sheet,
        Guid SelectedObjectId,
        FreeX.App.UI.ObjectKind SelectedObjectKind,
        ulong NavigationRevision,
        Visibility ShapeFormatVisibility,
        Visibility PictureFormatVisibility,
        bool ShapeGradientEnabled,
        bool ShapeEffectsEnabled,
        bool PictureCropEnabled);

    private readonly record struct PivotFieldListRefreshKey(
        Sheet? Sheet,
        GridRange? SelectedRange,
        ulong NavigationRevision,
        Visibility PaneVisibility,
        bool HasPendingLayout);

    private readonly record struct SlicerTimelineRefreshKey(
        Workbook Workbook,
        ulong NavigationRevision,
        bool Dismissed,
        Visibility PaneVisibility,
        int SlicerCount,
        int TimelineCount);

    private TableContextRefreshKey? _lastViewportTableContextRefreshKey;
    private ChartContextRefreshKey? _lastViewportChartContextRefreshKey;
    private DrawingObjectContextRefreshKey? _lastViewportDrawingObjectContextRefreshKey;
    private PivotFieldListRefreshKey? _lastViewportPivotFieldListRefreshKey;
    private SlicerTimelineRefreshKey? _lastViewportSlicerTimelineRefreshKey;

    // ── Navigation helpers ────────────────────────────────────────────────────

    /// <summary>
    /// This window's own view mode/zoom for <paramref name="sheet"/> (Excel "New Window"
    /// independence -- R83-app-view-modes-5-1). Falls back to the Normal/100% defaults for a
    /// null sheet without touching the store.
    /// </summary>
    private WorksheetViewStateSnapshot GetEffectiveViewState(Sheet? sheet) =>
        sheet is null
            ? new WorksheetViewStateSnapshot(WorksheetViewMode.Normal, 100, true, true, true)
            : _worksheetViewStates.GetOrSeed(sheet);

    /// <summary>
    /// R142-services-freeze-split-newwindow-1: seeds THIS (brand-new secondary) window's own
    /// <see cref="_worksheetViewStates"/> for <paramref name="source"/>'s current sheet from
    /// <paramref name="source"/>'s own effective view state, before this window has rendered that
    /// sheet at all. Called from <see cref="ViewNewWindowBtn_Click"/> right after
    /// <see cref="SetNewWindowSourceHint"/>, so <see cref="GetEffectiveViewState"/>'s lazy
    /// <see cref="WorksheetViewStateStore.GetOrSeed"/> finds an already-seeded snapshot here
    /// instead of falling back to the shared <see cref="Sheet"/> fields (which may have been last
    /// written by an unrelated third window).
    /// </summary>
    internal void SeedWorksheetViewStateFromSourceWindow(MainWindow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source._workbook.GetSheet(source._currentSheetId) is not { } sourceSheet)
            return;

        _worksheetViewStates.Set(sourceSheet.Id, source.GetEffectiveViewState(sourceSheet));
    }

    /// <summary>
    /// Records this window's own just-applied view-mode/zoom/display-toggle/Freeze-Panes/Split
    /// change so it survives a sibling window later mutating the shared <see cref="Sheet"/>'s
    /// corresponding fields. Call once, right after successfully executing a
    /// view-mode/zoom/Gridlines/Headings/Ruler/Freeze-Panes/Split command in THIS window, for
    /// every sheet the command targeted (grouped-sheet edits touch more than one).
    /// </summary>
    private void SyncWindowViewState(IReadOnlyList<SheetId> sheetIds)
    {
        foreach (var sheetId in sheetIds)
        {
            if (_workbook.GetSheet(sheetId) is { } sheet)
                _worksheetViewStates.Set(sheet.Id, new WorksheetViewStateSnapshot(
                    sheet.ViewMode,
                    sheet.ZoomPercent,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowFormulas));
        }
    }

    /// <summary>
    /// Projects this WPF window's existing per-sheet view snapshots into the shared session before
    /// a session-owned grouped view command runs. This is the view-state counterpart to
    /// <c>SynchronizeWorkbookSessionSelection</c> and lets the shared command preserve values that
    /// differ from a sibling window without moving native viewport rendering into services.
    /// </summary>
    private void SynchronizeWorkbookSessionViewState(IReadOnlyList<SheetId> sheetIds)
    {
        var snapshots = new Dictionary<SheetId, WorksheetViewStateSnapshot>();
        foreach (var sheetId in sheetIds)
        {
            if (_workbook.GetSheet(sheetId) is { } sheet)
                snapshots[sheetId] = GetEffectiveViewState(sheet);
        }

        _session.SynchronizeWorksheetViewState(snapshots);
    }

    /// <summary>
    /// Pushes this window's own remembered view-mode/zoom/display-toggle/Freeze-Panes/Split state
    /// (<see cref="_worksheetViewStates"/>) back onto the shared <see cref="Sheet"/> fields for
    /// every sheet this window has ever rendered, immediately before this window serializes the
    /// workbook (R120-corewriter-persist-saving-window-view-1).
    /// <para>
    /// <see cref="Sheet.ZoomPercent"/>/<see cref="Sheet.ViewMode"/>/<see cref="Sheet.ShowGridlines"/>/
    /// <see cref="Sheet.ShowHeadings"/>/<see cref="Sheet.ShowRulers"/>/<see cref="Sheet.ShowFormulas"/>/
    /// <see cref="Sheet.FrozenRows"/>/<see cref="Sheet.FrozenCols"/>/<see cref="Sheet.SplitRow"/>/
    /// <see cref="Sheet.SplitColumn"/> are one shared field per sheet, mutated in place by whichever
    /// window's command last executed -- <see cref="WorksheetViewStateStore"/> exists so each open
    /// "New Window" sibling keeps displaying its own remembered value even after a sibling changes
    /// those shared fields, but <c>XlsxWorksheetViewWriter</c> (and every other writer) still only
    /// ever reads the shared fields. Without this reconciliation, Ctrl+S from a window whose own
    /// view has diverged from the shared fields would silently persist whichever sibling window's
    /// view last touched them instead of this window's own. Call this once, right before handing
    /// the workbook to <c>WorkbookSaveService</c>.
    /// </para>
    /// <para>
    /// R138: <see cref="Sheet.ActiveRow"/>/<see cref="Sheet.ActiveCol"/> get the same treatment
    /// here, for the same reason. Unlike the fields above, this window writes its active
    /// cell/selection straight onto those shared fields the instant the selection changes
    /// (<c>SetActiveCell</c>/<c>MoveActiveCellWithinSelection</c>/etc., MainWindow.Selection.cs) --
    /// so a sibling "New Window" moving ITS OWN selection after this window's last move silently
    /// overwrites what this window is displaying. <see cref="_selectionAnchor"/> is this window's
    /// own live active cell for <see cref="_currentSheetId"/> (kept in sync with the grid, never
    /// shared with a sibling window); <see cref="_worksheetSelections"/> remembers this window's own
    /// active cell for every OTHER sheet it has visited and navigated away from, exactly like
    /// <see cref="_worksheetViewStates"/> above. Reconciling both right before save means Ctrl+S from
    /// THIS window persists what THIS window is actually showing, not whichever sibling window's
    /// selection last mutated the shared fields.
    /// </para>
    /// <para>
    /// R152-freeze-split-F2: <see cref="Sheet.ViewTopRow"/>/<see cref="Sheet.ViewLeftCol"/> get the
    /// same treatment for the currently displayed sheet. Unlike every field above, there is no
    /// per-window store for scroll position on sheets this window has navigated away from -- but
    /// for <see cref="_currentSheetId"/>, this window's own live <c>VerticalScroll.Value</c>/
    /// <c>HorizontalScroll.Value</c> (native WPF <see cref="System.Windows.Controls.Primitives.ScrollBar"/>
    /// instances, one per window, never shared) already IS this window's own scroll position, exactly
    /// like <see cref="_selectionAnchor"/> is this window's own active cell above. Recomputing the
    /// origin from them the same way <see cref="UpdateViewport"/> does and writing it onto the shared
    /// sheet right before save means Ctrl+S from THIS window persists what THIS window is actually
    /// scrolled to, instead of whichever sibling window's <see cref="UpdateViewport"/> last happened
    /// to run and overwrite the shared fields.
    /// </para>
    /// </summary>
    private void ReconcileViewStateForSave()
    {
        foreach (var (sheetId, snapshot) in _worksheetViewStates.Snapshots)
        {
            if (_workbook.GetSheet(sheetId) is not { } sheet)
                continue;

            sheet.ViewMode = snapshot.ViewMode;
            sheet.ZoomPercent = snapshot.ZoomPercent;
            sheet.ShowGridlines = snapshot.ShowGridlines;
            sheet.ShowHeadings = snapshot.ShowHeadings;
            sheet.ShowRulers = snapshot.ShowRulers;
            sheet.ShowFormulas = snapshot.ShowFormulas;
            sheet.FrozenRows = snapshot.FrozenRows;
            sheet.FrozenCols = snapshot.FrozenCols;
            sheet.SplitRow = snapshot.SplitRow;
            sheet.SplitColumn = snapshot.SplitColumn;
        }

        if (_workbook.GetSheet(_currentSheetId) is { } currentSheet && _selectionAnchor is { } activeCell)
        {
            currentSheet.ActiveRow = activeCell.Row;
            currentSheet.ActiveCol = activeCell.Col;
        }

        if (_workbook.GetSheet(_currentSheetId) is { } currentSheetForScroll && SheetGrid is not null)
        {
            var scrollViewState = GetEffectiveViewState(currentSheetForScroll);
            var (topRow, leftCol) = GetEffectiveViewportOrigin(
                currentSheetForScroll,
                VerticalScroll.Value,
                HorizontalScroll.Value);
            topRow = ClampViewportOrigin(
                topRow,
                CellAddress.MaxRow,
                SheetGrid.Viewport is null
                    ? 40
                    : (uint)WorkbookViewportScrollPlanner.CountVisibleScrollableRows(SheetGrid.Viewport, scrollViewState.FrozenRows));
            leftCol = ClampViewportOrigin(
                leftCol,
                CellAddress.MaxCol,
                SheetGrid.Viewport is null
                    ? 15
                    : (uint)WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(SheetGrid.Viewport, scrollViewState.FrozenCols));
            currentSheetForScroll.ViewTopRow = topRow;
            currentSheetForScroll.ViewLeftCol = leftCol;
        }

        foreach (var (sheetId, snapshot) in _worksheetSelections.Snapshots)
        {
            if (_workbook.GetSheet(sheetId) is not { } otherSheet)
                continue;

            otherSheet.ActiveRow = snapshot.Anchor.Row;
            otherSheet.ActiveCol = snapshot.Anchor.Col;
        }
    }

    /// <summary>
    /// This window's own effective view-origin for <paramref name="sheet"/> (Excel "New Window"
    /// independence for Freeze Panes -- R89-freeze-split-per-window-1): resolves the
    /// scrollbar-to-worksheet-index mapping against THIS window's effective frozen-row/column
    /// count (<see cref="GetEffectiveViewState"/>) instead of the shared Sheet's, so a sibling
    /// window's Freeze Panes change never shifts what this window's scrollbars resolve to. Use
    /// this instead of the plain <see cref="CalculateViewportOrigin(Sheet?, double, double)"/>
    /// static overload (which is kept, unmodified, for <c>ViewportOriginTests</c>) anywhere the
    /// call happens on behalf of THIS window's live scroll state.
    /// </summary>
    private (uint TopRow, uint LeftCol) GetEffectiveViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue)
    {
        var viewState = GetEffectiveViewState(sheet);
        return WorkbookViewportScrollPlanner.CalculateViewportOrigin(
            viewState.FrozenRows,
            viewState.FrozenCols,
            verticalScrollValue,
            horizontalScrollValue);
    }

    private void UpdateViewport()
    {
        if (_workbookSessionDisposed || SheetGrid == null || _viewportService == null) return;

        // Dismiss the AutoFilter dropdown flyout if we've moved to a different sheet.
        CloseAutoFilterDropdownOnSheetChange();

        var sheet = _workbook.GetSheet(_currentSheetId);
        // View mode and zoom are this window's own state (Excel "New Window" independence --
        // R83-app-view-modes-5-1): read them from the per-window store instead of straight off
        // the shared Sheet, which every window viewing this document mutates in common.
        var viewState = GetEffectiveViewState(sheet);
        if (sheet is not null)
        {
            SyncWorkbookActiveSheetIndex();
            SyncZoomFromSheet(viewState.ZoomPercent);
            SyncPageLayoutScaleToFitControls(sheet);
        }
        EnsureActiveCellSelection(sheet);
        SynchronizeWorkbookSessionSelection();

        var (topRow, leftCol) = GetEffectiveViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        topRow = ClampViewportOrigin(
            topRow,
            CellAddress.MaxRow,
            SheetGrid.Viewport is null
                ? 40
                : (uint)WorkbookViewportScrollPlanner.CountVisibleScrollableRows(SheetGrid.Viewport, viewState.FrozenRows));
        leftCol = ClampViewportOrigin(
            leftCol,
            CellAddress.MaxCol,
            SheetGrid.Viewport is null
                ? 15
                : (uint)WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(SheetGrid.Viewport, viewState.FrozenCols));
        if (sheet is not null)
        {
            sheet.ViewTopRow = topRow;
            sheet.ViewLeftCol = leftCol;
        }

        // Compute the correct row-header width before building the viewport so it is
        // built exactly once, even when crossing a row-digit boundary (e.g. 999→1000).
        var rowHeaderWidth = ComputeCorrectRowHeaderWidth(sheet, topRow, leftCol);
        var viewport = CreateViewport(sheet, topRow, leftCol, rowHeaderWidth);

        SheetGrid.Viewport = viewport;
        SheetGrid.ValidationCircleCells = sheet?.ValidationCircleCells;
        SheetGrid.PinnedNoteAddresses = sheet is null
            ? null
            : sheet.ShownComments.Count == 0
                ? null
                : sheet.ShownComments
                    .Select(a => (a.Row, a.Col))
                    .ToHashSet<(uint Row, uint Col)>();
        SheetGrid.HiddenRows = sheet?.HiddenRows;
        SheetGrid.HiddenColumns = sheet?.HiddenCols;
        // Feed the page-break preview overlay the sheet's real "effectively hidden" predicates
        // (AutoFilter-hidden rows + collapsed outline groups), not just the manual hidden sets
        // above, so its pagination matches the real print output (R15-print-preview-interaction-2).
        SheetGrid.SheetIsRowHiddenPredicate = sheet is null ? null : sheet.IsRowEffectivelyHidden;
        SheetGrid.SheetIsColHiddenPredicate = sheet is null ? null : sheet.IsColEffectivelyHidden;
        GridRange? autoFilterRange = sheet is not null &&
                                      AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var resolvedAutoFilterRange)
            ? resolvedAutoFilterRange
            : null;
        SheetGrid.AutoFilterRange = autoFilterRange;
        // R72-commands-sort-filter-4-1: without this, the AutoFilter dropdown arrow never shows the
        // filtered-state (funnel) icon for a filtered column -- GridView.Rendering.AutoFilter.cs reads
        // ActiveAutoFilterColumns (a set of column offsets from AutoFilterRange.Start.Col) to decide
        // which header buttons draw the "active" glyph, but nothing in the WPF host ever populated it.
        SheetGrid.ActiveAutoFilterColumns = sheet is not null && autoFilterRange is { } activeFilterRange
            ? AutoFilterHeaderButtonPlanner.GetActiveColumnOffsets(sheet, activeFilterRange)
            : null;
        IReadOnlyList<PivotHeaderDropdownTarget> pivotHeaderDropdownTargets = sheet is null
            ? []
            : PivotGridAdornmentPlanner.BuildHeaderTargets(_workbook, sheet);
        _pivotHeaderDropdownTargets = BuildPivotHeaderDropdownTargetLookup(pivotHeaderDropdownTargets);
        SheetGrid.PivotHeaderDropdowns = pivotHeaderDropdownTargets;
        SheetGrid.PivotRowLabelAdornments = sheet is null
            ? []
            : PivotGridAdornmentPlanner.BuildRowLabelAdornments(_workbook, sheet);
        SheetGrid.FormulaTraceSheetId = _currentSheetId;
        SheetGrid.FormulaTraceArrows = _formulaTraceArrows;
        SheetGrid.HyperlinkCells = sheet is null
            ? null
            : sheet.Hyperlinks.Keys
                .Select(address => new CellAddress(default, address.Row, address.Col))
                .ToHashSet();
        SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch
        {
            AppOptionsObjectDisplay.Placeholders => FreeX.App.UI.GridObjectDisplayMode.Placeholders,
            AppOptionsObjectDisplay.Nothing => FreeX.App.UI.GridObjectDisplayMode.Nothing,
            _ => FreeX.App.UI.GridObjectDisplayMode.All
        };
        var keepObjectData = _options.ObjectsDisplay != AppOptionsObjectDisplay.Nothing;
        SheetGrid.Charts = keepObjectData ? sheet?.Charts : null;
        SheetGrid.TextBoxes = keepObjectData ? sheet?.TextBoxes : null;
        SheetGrid.DrawingShapes = keepObjectData ? sheet?.DrawingShapes : null;
        SheetGrid.WorkbookTheme = _workbook.Theme;
        SheetGrid.Pictures = keepObjectData ? sheet?.Pictures : null;
        SheetGrid.DrawingObjectZOrder = keepObjectData ? sheet?.DrawingObjectZOrder : null;
        var nativeVisualFilters = keepObjectData && sheet is not null
            ? SlicerTimelinePanePlanner.GetNativeVisualFilters(_workbook, sheet)
            : null;
        if (nativeVisualFilters is { Slicers.Count: > 0 })
        {
            new SlicerTimelineSourceSession(_workbook).PopulateAvailableItems(nativeVisualFilters.Slicers);
        }
        SheetGrid.NativeSlicers = nativeVisualFilters?.Slicers;
        SheetGrid.NativeTimelines = nativeVisualFilters?.Timelines;
        if (keepObjectData && sheet is not null && sheet.FormControls.Count > 0)
        {
            // R17-form-controls-linkedcell-1: mirror the Avalonia shell (MainWindow.FormControls.cs)
            // and re-derive each control's IsChecked/Value/SelectedIndex from its linked cell's
            // current value BEFORE resolving selected-item text, so a direct cell edit or formula
            // recalc (not just a click on the control itself) is reflected on every viewport refresh
            // instead of leaving the WPF checkbox/spinner/scrollbar/list-box stale.
            FreeX.Core.Commands.FormControlInteractionService.SyncControlsFromLinkedCells(sheet, _workbook);

            // Resolve each list control's selected-item text (ListFillRange[SelectedIndex]) into the
            // render-model's SelectedText so the GridView draws the selection without raw workbook access.
            FreeX.Core.Commands.FormControlListResolver.PopulateSelectedText(sheet, _workbook);
        }
        SheetGrid.FormControls = keepObjectData ? sheet?.FormControls : null;
        SheetGrid.WorksheetBackground = sheet?.BackgroundImage;
        SheetGrid.ActiveSheetId = _currentSheetId;
        // Mirror the Avalonia shell (MainWindow.cs MapCellFlowDirection/MapCellTextAlignment): bind the
        // active sheet's Sheet.IsRightToLeft (Excel's sheetView rightToLeft="1") to the grid so
        // Context-reading-order cells resolve to RTL instead of always defaulting to LTR (P28).
        SheetGrid.IsSheetRightToLeft = sheet?.IsRightToLeft ?? false;
        SheetGrid.SheetRichTextRuns = sheet?.RichTextRuns;
        SheetGrid.Sparklines = sheet?.Sparklines;
        SheetGrid.SparklineValues = sheet is null
            ? null
            : _sparklineValueCache.GetOrCreate(
                sheet,
                _navigationCacheRevision,
                () => SparklineSeriesReader.BuildValues(_workbook, sheet));
        SheetGrid.MergedRegions = sheet?.MergedRegions;
        SheetGrid.WorksheetViewMode = viewState.ViewMode;
        // Gridlines/Headings/Rulers are this window's own state, just like ViewMode/Zoom above
        // (R87-order-guard-window-state-sweep-1) -- read them from the per-window store instead
        // of straight off the shared Sheet, which every window viewing this document mutates in
        // common, or toggling them in one "New Window" sibling would leak into every other one.
        SheetGrid.ShowGridLines = viewState.ShowGridlines;
        SheetGrid.ShowHeaders = viewState.ShowHeadings;
        SheetGrid.ShowRulers = viewState.ShowRulers;
        _suppressViewOptionSync = true;
        try
        {
            // Publish the complete per-window worksheet-view projection through the shared planner.
            // This includes aliases on Page Layout plus Show Formulas and Split, which must never
            // read the shared Sheet fields after sibling workbook windows diverge.
            WorkbookViewRibbonStatePlanner.Build(
                    viewState.ViewMode,
                    viewState.ShowGridlines,
                    viewState.ShowHeadings,
                    viewState.ShowRulers,
                    viewState.ShowFormulas,
                    viewState.SplitRow is not null || viewState.SplitColumn is not null)
                .Publish(_ribbonState);
            SyncStatusViewShortcutState(WorksheetViewModeUiStatePlanner.Build(viewState.ViewMode));
            RefreshViewWindowCommandState();
        }
        finally
        {
            _suppressViewOptionSync = false;
        }
        WorkbookPageLayoutSheetOptionsRibbonStatePlanner.Build(
                viewState.ShowGridlines,
                sheet?.PrintGridlines ?? false,
                viewState.ShowHeadings,
                sheet?.PrintHeadings ?? false)
            .Publish(_ribbonState);
        SheetGrid.RowPageBreaks = sheet?.RowPageBreaks;
        SheetGrid.ColumnPageBreaks = sheet?.ColumnPageBreaks;
        SheetGrid.PrintArea = sheet?.PrintArea;
        // R91-render-frozen-print-titles-5-2: also bind the FULL print-area list so the Page Break
        // Preview / Page Layout overlay (GridView.Overlays.cs) can paginate/un-mask every configured
        // _xlnm.Print_Area region, not just the first (sheet?.PrintArea above).
        SheetGrid.PrintAreas = sheet?.PrintAreas;
        SheetGrid.PagePreviewRange = CalculatePagePreviewRange(sheet, viewport);
        // Split is this window's own state too (R89-freeze-split-per-window-1), same reasoning
        // as Gridlines/Headings/Rulers above.
        SheetGrid.SplitRow = viewState.SplitRow;
        SheetGrid.SplitColumn = viewState.SplitColumn;
        SheetGrid.PageMargins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        SheetGrid.PageOrientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        SheetGrid.PaperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;
        SheetGrid.PageOrder = sheet?.PageOrder ?? WorksheetPageOrder.DownThenOver;
        SheetGrid.ScaleToFit = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        SheetGrid.PrintTitleRows = sheet?.PrintTitleRows;
        SheetGrid.PrintTitleColumns = sheet?.PrintTitleColumns;
        SheetGrid.SheetRowHeights = sheet?.RowHeights;
        SheetGrid.SheetDefaultRowHeight = sheet?.DefaultRowHeight ?? PagePaginationPlanner.NominalRowHeight;
        SheetGrid.SheetColumnWidths = sheet?.ColumnWidths;
        SheetGrid.SheetDefaultColumnWidth = sheet?.DefaultColumnWidth ?? 8.43;
        SheetGrid.SheetHeaderMargin = sheet?.HeaderMargin ?? 0.3;
        SheetGrid.SheetFooterMargin = sheet?.FooterMargin ?? 0.3;

        // Adjust scrollbar range to the used data range + buffer, thumb to visible area
        UpdateScrollbarMaximums(sheet);
        var scrollableRowCount = WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, viewState.FrozenRows);
        var scrollableColumnCount = WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, viewState.FrozenCols);
        VerticalScroll.ViewportSize   = scrollableRowCount;
        HorizontalScroll.ViewportSize = scrollableColumnCount;
        VerticalScroll.LargeChange    = Math.Max(1, scrollableRowCount);
        HorizontalScroll.LargeChange  = Math.Max(1, scrollableColumnCount);
        RefreshViewportValidationDropdown(sheet);
        RefreshViewportFormulaReferenceHighlights();
        RefreshViewportTableContextualTab(sheet);
        RefreshViewportDrawingObjectContextualTabs(sheet);
        RefreshViewportChartContextualTabs(sheet);
        RefreshViewportPivotFieldListPane(sheet);
        RefreshViewportSlicerTimelinePane();
        RefreshTextBoxInlineEditorPosition();
        UpdateChartsheetPresentation(sheet);
    }

    /// <summary>
    /// R76-render-freeze-scroll-4-1: Insert/Delete Rows renumbers every row at or below the edit
    /// point, so if the edit happens AT OR ABOVE the current viewport's top-left anchor
    /// (ViewTopRow), the same scrollbar Value now points at DIFFERENT worksheet content -- the
    /// view visibly jumps even though nothing scrolled. Excel instead keeps the same content on
    /// screen by shifting the anchor by the inserted/deleted row count. Only applies when the
    /// edit is at/above the view; an edit strictly below the view never moves it. Must be called
    /// BEFORE <see cref="UpdateViewport"/> so the shifted Value is honored on the next viewport
    /// rebuild instead of being silently overwritten by the stale one (mirrors the
    /// preTopRow/newVerticalValue pattern in <c>SetFreezePanes</c>, MainWindow.ViewCommands.cs).
    /// </summary>
    private void ShiftScrollOriginForRowEdit(uint editRow, int rowDelta)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var (topRow, _) = GetEffectiveViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        var newTopRow = WorkbookViewportScrollPlanner.PlanStructuralEditOriginShift(
            topRow,
            editRow,
            rowDelta,
            CellAddress.MaxRow);
        if (newTopRow is null) return;

        var frozenRows = GetEffectiveViewState(sheet).FrozenRows;
        var newVerticalValue = WorksheetIndexToScrollbarValue(newTopRow.Value, frozenRows);

        // Bump Maximum first if needed so assigning Value below isn't silently clamped by a
        // range still sized for the pre-edit row count; UpdateViewport() (called next)
        // recalculates the real Maximum right after.
        if (newVerticalValue > VerticalScroll.Maximum)
            VerticalScroll.Maximum = newVerticalValue;
        VerticalScroll.Value = newVerticalValue;
    }

    /// <summary>
    /// Column counterpart of <see cref="ShiftScrollOriginForRowEdit"/> for Insert/Delete Columns.
    /// </summary>
    private void ShiftScrollOriginForColEdit(uint editCol, int colDelta)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var (_, leftCol) = GetEffectiveViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        var newLeftCol = WorkbookViewportScrollPlanner.PlanStructuralEditOriginShift(
            leftCol,
            editCol,
            colDelta,
            CellAddress.MaxCol);
        if (newLeftCol is null) return;

        var frozenCols = GetEffectiveViewState(sheet).FrozenCols;
        var newHorizontalValue = WorksheetIndexToScrollbarValue(newLeftCol.Value, frozenCols);

        if (newHorizontalValue > HorizontalScroll.Maximum)
            HorizontalScroll.Maximum = newHorizontalValue;
        HorizontalScroll.Value = newHorizontalValue;
    }

    private static IReadOnlyDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget> BuildPivotHeaderDropdownTargetLookup(
        IReadOnlyList<PivotHeaderDropdownTarget> targets)
    {
        var lookup = new Dictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>(targets.Count);
        foreach (var target in targets)
            lookup[(target.HeaderCell.Row, target.HeaderCell.Col)] = target;

        return lookup;
    }

    private void RefreshViewportValidationDropdown(Sheet? sheet)
    {
        if (_validationDropdown?.Visibility == Visibility.Visible ||
            sheet?.DataValidations.Count > 0)
        {
            RefreshValidationDropdown();
            RefreshDvInputMessage();
        }
    }

    private void RefreshViewportFormulaReferenceHighlights()
    {
        if (GetFormulaReferenceHighlightEditor() is not null ||
            _formulaReferenceGridOverlayActiveCount != 0)
        {
            RefreshFormulaReferenceHighlights();
        }
    }

    private void RefreshViewportTableContextualTab(Sheet? sheet)
    {
        var key = CreateTableContextRefreshKey(sheet);
        if (_lastViewportTableContextRefreshKey == key)
            return;

        RefreshTableContextualTab();
        _lastViewportTableContextRefreshKey = CreateTableContextRefreshKey(sheet);
    }

    private TableContextRefreshKey CreateTableContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedRange,
            _navigationCacheRevision,
            TableDesignTab?.Visibility ?? Visibility.Collapsed);

    private void RefreshViewportChartContextualTabs(Sheet? sheet)
    {
        var key = CreateChartContextRefreshKey(sheet);
        if (_lastViewportChartContextRefreshKey == key)
            return;

        RefreshChartContextualTabs();
        _lastViewportChartContextRefreshKey = CreateChartContextRefreshKey(sheet);
    }

    private ChartContextRefreshKey CreateChartContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedObjectId,
            SheetGrid.SelectedObjectKind,
            ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet()),
            _navigationCacheRevision,
            ChartDesignTab?.Visibility ?? Visibility.Collapsed,
            ChartFormatTab?.Visibility ?? Visibility.Collapsed);

    private void RefreshViewportDrawingObjectContextualTabs(Sheet? sheet)
    {
        var key = CreateDrawingObjectContextRefreshKey(sheet);
        if (_lastViewportDrawingObjectContextRefreshKey == key)
            return;

        RefreshDrawingObjectContextualTabs();
        _lastViewportDrawingObjectContextRefreshKey = CreateDrawingObjectContextRefreshKey(sheet);
    }

    private DrawingObjectContextRefreshKey CreateDrawingObjectContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedObjectId,
            SheetGrid.SelectedObjectKind,
            _navigationCacheRevision,
            ShapeFormatTab?.Visibility ?? Visibility.Collapsed,
            PictureFormatTab?.Visibility ?? Visibility.Collapsed,
            _ribbonState.GetState("Shape Gradient").IsEnabled,
            _ribbonState.GetState("Shape Effects").IsEnabled,
            _ribbonState.GetState("Crop Picture").IsEnabled);

    private void RefreshViewportPivotFieldListPane(Sheet? sheet)
    {
        var key = CreatePivotFieldListRefreshKey(sheet);
        if (_lastViewportPivotFieldListRefreshKey == key)
            return;

        RefreshPivotFieldListPane();
        _lastViewportPivotFieldListRefreshKey = CreatePivotFieldListRefreshKey(sheet);
    }

    private PivotFieldListRefreshKey CreatePivotFieldListRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedRange,
            _navigationCacheRevision,
            PivotFieldListPane?.Visibility ?? Visibility.Collapsed,
            _pendingPivotLayout is not null);

    private void RefreshViewportSlicerTimelinePane()
    {
        var key = CreateSlicerTimelineRefreshKey();
        if (_lastViewportSlicerTimelineRefreshKey == key)
            return;

        RefreshSlicerTimelinePane();
        _lastViewportSlicerTimelineRefreshKey = CreateSlicerTimelineRefreshKey();
    }

    private SlicerTimelineRefreshKey CreateSlicerTimelineRefreshKey() =>
        new(
            _workbook,
            _navigationCacheRevision,
            _slicerTimelinePaneDismissed,
            SlicerTimelinePane?.Visibility ?? Visibility.Collapsed,
            _workbook.Slicers.Count,
            _workbook.Timelines.Count);

    /// <summary>
    /// Returns the row-header width that will be needed for the given top row, by querying
    /// only the cheap row-metric and outline-group data — no cell materialization occurs.
    /// This prevents the viewport from being built twice when crossing a row-digit boundary
    /// (e.g. row 999→1000).
    /// </summary>
    private double ComputeCorrectRowHeaderWidth(Sheet? sheet, uint topRow, uint leftCol)
    {
        if (!SheetGrid.ShowHeaders)
            return 0.0;

        // Use a placeholder width for the first pass — the available width passed here
        // does not affect row metrics, so any reasonable value works.
        var placeholderWidth = SheetGrid.ActualRowHeaderWidth;
        var viewState = GetEffectiveViewState(sheet);
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, placeholderWidth, _zoomLevel),
            IncludeObjects: false,
            SplitPaneOffsets: null,
            FrozenRowsOverride: viewState.FrozenRows,
            FrozenColsOverride: viewState.FrozenCols);

        var (lastVisibleRow, rowOutlineGroups) =
            _viewportService.ComputeRowMetricsSummary(_workbook, _currentSheetId, request);
        return FreeX.App.UI.GridView.CalculateRowHeaderWidth(lastVisibleRow, rowOutlineGroups);
    }

    private ViewportModel CreateViewport(Sheet? sheet, uint topRow, uint leftCol, double rowHeaderWidth)
    {
        // Freeze Panes/Window > Split/Show Formulas are this window's own state (Excel "New
        // Window" independence -- R89-freeze-split-per-window-1, extended to Show Formulas by
        // R89-show-formulas-per-window-1): the shared ViewportService already accepts
        // FrozenRowsOverride/FrozenColsOverride/SplitOverride/ShowFormulasOverride on
        // ViewportRequest (added for the Avalonia shell's own per-view overrides), so route THIS
        // window's effective values through instead of letting it fall back to the shared
        // Sheet.FrozenRows/FrozenCols/SplitRow/SplitColumn/ShowFormulas, which every window
        // viewing this document shares.
        var viewState = GetEffectiveViewState(sheet);
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, rowHeaderWidth, _zoomLevel),
            IncludeObjects: _options.ObjectsDisplay == AppOptionsObjectDisplay.All,
            SplitPaneOffsets: GetSplitPaneViewportOffsets(viewState, topRow, leftCol),
            FrozenRowsOverride: viewState.FrozenRows,
            FrozenColsOverride: viewState.FrozenCols,
            SplitOverride: new SplitPaneStateOverride(viewState.SplitRow, viewState.SplitColumn),
            ShowFormulasOverride: viewState.ShowFormulas);

        return _viewportService.GetViewport(_workbook, _currentSheetId, request);
    }

    // TopRight (columns) and BottomLeft (rows) share the SAME shared scrollbar as the main
    // (bottom-right) pane in Excel's split model -- neither has an independent scroll offset of
    // its own, so this always mirrors the CURRENT main topRow/leftCol rather than ever consulting
    // a sticky per-sheet offset, which used to let these two panes desync permanently from the
    // main pane with no way to resync (r56 fix). Takes THIS window's effective view state
    // (R89-freeze-split-per-window-1) rather than the shared Sheet directly.
    private static SplitPaneViewportOffsets? GetSplitPaneViewportOffsets(
        WorksheetViewStateSnapshot viewState, uint topRow, uint leftCol)
    {
        if (!viewState.SplitRow.HasValue && !viewState.SplitColumn.HasValue)
            return null;

        return new SplitPaneViewportOffsets(
            viewState.SplitColumn.HasValue ? leftCol : null,
            viewState.SplitRow.HasValue ? topRow : null);
    }

    private static GridRange? CalculatePagePreviewRange(Sheet? sheet, ViewportModel viewport)
    {
        if (sheet is null || sheet.PrintArea is not null)
            return null;

        var usedRange = sheet.GetUsedRange();
        if (viewport.RowMetrics.Count == 0 || viewport.ColMetrics.Count == 0)
            return usedRange;

        var firstRow = uint.MaxValue;
        var lastRow = 0u;
        foreach (var row in viewport.RowMetrics)
        {
            firstRow = Math.Min(firstRow, row.Row);
            lastRow = Math.Max(lastRow, row.Row);
        }

        var firstColumn = uint.MaxValue;
        var lastColumn = 0u;
        foreach (var column in viewport.ColMetrics)
        {
            firstColumn = Math.Min(firstColumn, column.Col);
            lastColumn = Math.Max(lastColumn, column.Col);
        }

        if (firstRow == uint.MaxValue || firstColumn == uint.MaxValue || lastRow == 0 || lastColumn == 0)
            return usedRange;

        var visibleRowSpan = lastRow - firstRow + 1;
        var visibleColumnSpan = lastColumn - firstColumn + 1;
        var startRow = Math.Min(usedRange?.Start.Row ?? 1u, firstRow);
        var startColumn = Math.Min(usedRange?.Start.Col ?? 1u, firstColumn);
        var endRow = Math.Max(
            Math.Max(usedRange?.End.Row ?? 1u, lastRow),
            AddWithLimit(lastRow, visibleRowSpan, CellAddress.MaxRow));
        var endColumn = Math.Max(
            Math.Max(usedRange?.End.Col ?? 1u, lastColumn),
            AddWithLimit(lastColumn, visibleColumnSpan, CellAddress.MaxCol));

        return new GridRange(
            new CellAddress(sheet.Id, startRow, startColumn),
            new CellAddress(sheet.Id, endRow, endColumn));
    }

    private static uint AddWithLimit(uint value, uint addend, uint limit)
    {
        if (value >= limit)
            return limit;

        var remaining = limit - value;
        return addend >= remaining ? limit : value + addend;
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        WorkbookViewportScrollPlanner.CalculateViewportOrigin(sheet, verticalScrollValue, horizontalScrollValue);

    public static uint ScrollbarValueToWorksheetIndex(
        double scrollbarValue,
        uint frozenCount,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.ScrollbarValueToWorksheetIndex(scrollbarValue, frozenCount, absoluteLimit);

    public static uint WorksheetIndexToScrollbarValue(
        uint worksheetIndex,
        uint frozenCount) =>
        WorkbookViewportScrollPlanner.WorksheetIndexToScrollbarValue(worksheetIndex, frozenCount);

    public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount)
        => WorkbookViewportScrollPlanner.CalculateScrollableLimit(absoluteLimit, frozenCount);

    // R89-freeze-split-per-window-1: resolves against THIS window's effective Freeze Panes
    // count (GetEffectiveViewState), not the shared Sheet.FrozenRows/FrozenCols a sibling
    // "New Window" may have changed -- mirrors the ShowGridlines/ShowHeadings/ShowRulers
    // per-window pattern above (R87-order-guard-window-state-sweep-1). The plain
    // Sheet-based WorkbookViewportScrollPlanner.GetScrollableRowLimit/GetScrollableColumnLimit
    // overloads are left untouched (ViewportOriginTests/ViewportScrollCalculatorTests still
    // exercise those directly against a bare Sheet).
    private uint GetScrollableRowLimit(Sheet? sheet) =>
        WorkbookViewportScrollPlanner.GetScrollableRowLimit(GetEffectiveViewState(sheet).FrozenRows);

    private uint GetScrollableColumnLimit(Sheet? sheet) =>
        WorkbookViewportScrollPlanner.GetScrollableColumnLimit(GetEffectiveViewState(sheet).FrozenCols);

    public static uint ClampViewportOrigin(double rawValue, uint absoluteLimit, uint visibleSpan)
        => WorkbookViewportScrollPlanner.ClampViewportOrigin(rawValue, absoluteLimit, visibleSpan);

    public static double CalculateViewportAvailableWidth(
        double gridWidth,
        double rowHeaderWidth,
        double zoomLevel) =>
        WorkbookViewportScrollPlanner.CalculateViewportAvailableWidth(gridWidth, rowHeaderWidth, zoomLevel);

    public static uint CalculateOpenedWorksheetScrollValue(
        uint? savedTopLeftIndex,
        uint fallbackIndex,
        uint absoluteLimit,
        uint frozenCount = 0) =>
        WorkbookViewportScrollPlanner.CalculateOpenedWorksheetScrollValue(
            savedTopLeftIndex,
            fallbackIndex,
            absoluteLimit,
            frozenCount);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit,
        uint visibleSpan) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit,
            visibleSpan);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex) =>
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(targetIndex, firstVisibleIndex, lastVisibleIndex);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForKeyboardReveal(
            currentMaximum,
            desiredScrollValue,
            absoluteLimit);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForKeyboardReveal(currentMaximum, desiredScrollValue);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateWheelScroll(
        double currentValue,
        double currentMaximum,
        int wheelNotches,
        double stepPerNotch,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateWheelScroll(
            currentValue,
            currentMaximum,
            wheelNotches,
            stepPerNotch,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateDragAutoScroll(
        double currentValue,
        double currentMaximum,
        int direction,
        double step,
        double visibleSpan,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
            currentValue,
            currentMaximum,
            direction,
            step,
            visibleSpan,
            absoluteLimit);

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)
        => WorkbookViewportScrollPlanner.CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);

    public static uint CalculateScrollbarMaximumForUsedRange(
        uint usedMax,
        uint visibleSpan,
        uint currentScrollValue,
        uint absoluteLimit) =>
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForUsedRange(
            usedMax,
            visibleSpan,
            currentScrollValue,
            absoluteLimit);

    public static (uint UsedMaxRow, uint UsedMaxCol) CalculateUsedRangeExtents(Sheet? sheet)
    {
        var usedRange = sheet?.GetUsedRange();
        return usedRange is null
            ? (1u, 1u)
            : (usedRange.Value.End.Row, usedRange.Value.End.Col);
    }

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        var (usedMaxRow, usedMaxCol) = CalculateUsedRangeExtents(sheet);

        // Freeze Panes is this window's own state (R89-freeze-split-per-window-1): resolve
        // against GetEffectiveViewState instead of the shared Sheet.FrozenRows/FrozenCols.
        var viewState = GetEffectiveViewState(sheet);
        var frozenRows = viewState.FrozenRows;
        var frozenCols = viewState.FrozenCols;

        var vp = SheetGrid.Viewport;
        uint visRows = (uint)Math.Max(10, vp is null ? 40 : WorkbookViewportScrollPlanner.CountVisibleScrollableRows(vp, frozenRows));
        uint visCols = (uint)Math.Max(5,  vp is null ? 15 : WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(vp, frozenCols));

        uint currentRow = Math.Max(1, (uint)VerticalScroll.Value);
        uint currentCol = Math.Max(1, (uint)HorizontalScroll.Value);
        uint vMaxRow = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxRow, frozenRows),
            visRows,
            currentRow,
            GetScrollableRowLimit(sheet));
        uint vMaxCol = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxCol, frozenCols),
            visCols,
            currentCol,
            GetScrollableColumnLimit(sheet));

        VerticalScroll.Maximum   = Math.Min(vMaxRow, GetScrollableRowLimit(sheet));
        HorizontalScroll.Maximum = Math.Min(vMaxCol, GetScrollableColumnLimit(sheet));
    }
}
