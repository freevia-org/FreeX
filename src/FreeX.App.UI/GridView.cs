using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.UI;

public enum GridObjectDisplayMode
{
    All,
    Placeholders,
    Nothing
}

/// <summary>
/// A high-performance, virtualized spreadsheet grid control.
/// Renders only the visible portion of the workbook using low-level DrawingContext.
/// </summary>
public partial class GridView : FrameworkElement
{
    public GridView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        Unloaded += (_, _) => StopMarchTimer();
        Unloaded += (_, _) => DismissCommentPreview();
    }

    /// <summary>
    /// Returns a custom automation peer for the worksheet grid and its visible cell
    /// peers, including grid, selection, grid-item, value, and selection-item patterns.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new GridViewAutomationPeer(this);

    /// <summary>
    /// Raises UI Automation selection/focus notifications whenever the active cell or
    /// selected range changes (e.g. via arrow-key/Tab/Enter navigation), so screen readers
    /// announce the new active cell's address and value. Mirrors the pattern used by the
    /// status bar (see MainWindow.GridStatus.cs NotifyStatusStatisticAutomationChanged), except
    /// it does not gate on IsLoaded: unlike a status-bar TextBlock, GridView's automation peer
    /// (and the per-cell peers it tracks) can legitimately be queried and kept in sync before the
    /// control is attached to a visual tree (e.g. hosted in an offscreen/print preview surface).
    /// Prefers <see cref="ActiveCell"/> (the host's true anchor/active cell) over
    /// <see cref="SelectedRange"/>'s normalized Start corner: a Shift+Up/Left extension keeps the
    /// anchor where the user started selecting, but renormalizes Start to the top-left corner, so
    /// using Start alone would announce the wrong cell (and its wrong value) whenever the
    /// selection was extended upward or leftward.
    /// </summary>
    private void NotifySelectionAutomationChanged()
    {
        if (UIElementAutomationPeer.FromElement(this) is not GridViewAutomationPeer peer)
            return;

        peer.NotifySelectionChanged(ActiveCell ?? SelectedRange?.Start);
    }

    /// <summary>
    /// Evicts cached cell automation peers that fell out of the visible viewport (e.g. after
    /// scrolling/navigating a large workbook), so <c>_cellPeers</c> tracks only currently
    /// reachable cells instead of growing without bound for the life of the grid; also
    /// re-announces the active cell's value if it changed without a selection move (e.g.
    /// Ctrl+Enter commit or F9 recalc).
    /// </summary>
    private void NotifyViewportAutomationChanged()
    {
        if (UIElementAutomationPeer.FromElement(this) is not GridViewAutomationPeer peer)
            return;

        peer.EvictStaleCellPeers();
        peer.NotifyActiveCellValueIfChanged();
    }

    /// <summary>
    /// Flags describing a cell's attached metadata for building its screen-reader announcement
    /// (UIA Name) -- the accessible parity to the sighted indicators GridView already renders
    /// (comment corner-triangle, formula-bar "=" prefix, merged span, hyperlink hand-cursor).
    /// R80 added <see cref="HasComment"/>/<see cref="CommentTitle"/> only
    /// (R80-app-accessibility-a11y-5-3); R81 adds <see cref="IsFormula"/>, <see cref="IsMerged"/>,
    /// and <see cref="HasHyperlink"/>, all backed by data GridView already has wired
    /// (<c>DisplayCell.Formula</c>, <see cref="MergedRegions"/>, <see cref="HyperlinkCells"/>).
    /// <see cref="HasDataValidation"/> and <see cref="IsLocked"/> are included so the builder and
    /// its cue text are ready and unit-testable, but neither is wired to a live GridView signal:
    /// GridView has no property carrying "cells with a data-validation rule" (only
    /// <see cref="ValidationCircleCells"/>, which is the narrower "current value fails its rule"
    /// set -- conflating the two would misannounce every cell with a passing validation as having
    /// none) or "the active sheet is protected" (a prerequisite for "locked" to mean anything;
    /// <c>CellStyle.Locked</c> defaults to true for virtually every cell, so surfacing it
    /// unconditionally would announce "is locked" on almost every cell in almost every workbook).
    /// Wiring those two needs a new signal sourced outside FreeX.App.UI.
    /// </summary>
    internal readonly record struct CellAnnouncementMetadata(
        bool HasComment = false,
        string? CommentTitle = null,
        bool IsFormula = false,
        bool IsMerged = false,
        bool HasDataValidation = false,
        bool HasHyperlink = false,
        bool IsLocked = false);

    /// <summary>
    /// Pure, unit-testable builder for a cell's UIA Name: the cell address (plus its value, if
    /// any) followed by a comma-separated "has X"/"is X" cue for each set metadata flag. Kept
    /// free of any GridView/AutomationPeer dependency so it can be exercised directly in tests
    /// without constructing a GridView, Viewport, or AutomationPeer.
    /// </summary>
    internal static string BuildCellAnnouncementName(string address, string? value, CellAnnouncementMetadata metadata)
    {
        var name = string.IsNullOrWhiteSpace(value) ? address : $"{address}: {value}";

        List<string>? cues = null;
        void AddCue(string cue) => (cues ??= []).Add(cue);

        if (metadata.HasComment && !string.IsNullOrEmpty(metadata.CommentTitle))
            AddCue($"has {metadata.CommentTitle.ToLowerInvariant()}");
        if (metadata.IsFormula)
            AddCue("is a formula");
        if (metadata.IsMerged)
            AddCue("is merged");
        if (metadata.HasDataValidation)
            AddCue("has data validation");
        if (metadata.HasHyperlink)
            AddCue("has a hyperlink");
        if (metadata.IsLocked)
            AddCue("is locked");

        return cues is null ? name : $"{name}, {string.Join(", ", cues)}";
    }

    private sealed class GridViewAutomationPeer(GridView owner) :
        FrameworkElementAutomationPeer(owner),
        IGridProvider,
        ISelectionProvider
    {
        private readonly Dictionary<(uint Row, uint Col), GridViewCellAutomationPeer> _cellPeers = [];

        private CellAddress? _lastNotifiedActiveCell = owner.ActiveCell ?? owner.SelectedRange?.Start;

        private string? _lastNotifiedActiveCellDisplayText;

        private GridView OwnerGrid => (GridView)Owner;

        public int RowCount => GetVisibleRows(OwnerGrid.Viewport).Count;

        public int ColumnCount => GetVisibleColumns(OwnerGrid.Viewport).Count;

        public bool CanSelectMultiple => true;

        public bool IsSelectionRequired => false;

        public IRawElementProviderSimple GetItem(int row, int column)
        {
            var rows = GetVisibleRows(OwnerGrid.Viewport);
            var columns = GetVisibleColumns(OwnerGrid.Viewport);
            if (row < 0 || row >= rows.Count)
                throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= columns.Count)
                throw new ArgumentOutOfRangeException(nameof(column));

            return ProviderFromPeer(GetOrCreateCellPeer(rows[row], columns[column]));
        }

        public IRawElementProviderSimple[] GetSelection()
        {
            var rows = GetVisibleRows(OwnerGrid.Viewport);
            var columns = GetVisibleColumns(OwnerGrid.Viewport);
            if (rows.Count == 0 || columns.Count == 0)
                return [];

            var selected = new List<IRawElementProviderSimple>();
            foreach (var row in rows)
            {
                foreach (var column in columns)
                {
                    if (IsCellSelected(row, column))
                        selected.Add(ProviderFromPeer(GetOrCreateCellPeer(row, column)));
                }
            }

            return [.. selected];
        }

        internal int GetRowIndex(uint row) => IndexOf(GetVisibleRows(OwnerGrid.Viewport), row);

        internal int GetColumnIndex(uint column) => IndexOf(GetVisibleColumns(OwnerGrid.Viewport), column);

        internal bool IsCellSelected(uint row, uint column)
        {
            if (OwnerGrid.SelectedRanges is { Count: > 0 } ranges)
                return ranges.Any(range => ContainsCell(range, row, column));

            return OwnerGrid.SelectedRange is { } range && ContainsCell(range, row, column);
        }

        internal bool IsActiveCell(uint row, uint column) =>
            _lastNotifiedActiveCell is { } active && active.Row == row && active.Col == column;

        internal string GetCellDisplayText(uint row, uint column) =>
            TryGetDisplayCell(row, column, out var cell)
                ? cell.DisplayText
                : string.Empty;

        /// <summary>
        /// Returns the cell's note/threaded-comment display (title + body), if any, so the cell's
        /// UIA AutomationPeer can announce its presence -- sighted users see a corner-triangle
        /// indicator (see GridView.Rendering.cs DrawCommentIndicator) but a screen-reader user
        /// otherwise has no equivalent cue (R80-app-accessibility-a11y-5-3).
        /// </summary>
        internal bool TryGetCellComment(uint row, uint column, out CellCommentDisplay? comment)
        {
            if (TryGetDisplayCell(row, column, out var cell) && cell.HasComment)
            {
                comment = cell.CommentDisplay;
                return true;
            }

            comment = null;
            return false;
        }

        /// <summary>
        /// Whether the cell is formula-backed (<c>DisplayCell.Formula</c> is populated whenever
        /// the viewport request includes formulas, which is the default), so the cell's
        /// AutomationPeer can announce "is a formula" the way a sighted user sees the formula
        /// text in the formula bar (R81 completion of R80-app-accessibility-a11y-5-3).
        /// </summary>
        internal bool IsCellFormula(uint row, uint column) =>
            TryGetDisplayCell(row, column, out var cell) && !string.IsNullOrEmpty(cell.Formula);

        /// <summary>
        /// Whether the cell falls within one of <see cref="MergedRegions"/> (checked directly
        /// against the dependency property rather than the render-built <c>_mergeLookup</c> cache,
        /// so it is correct even before the first render pass -- e.g. for a peer queried right
        /// after construction, as the unit tests do).
        /// </summary>
        internal bool IsCellMerged(uint row, uint column)
        {
            if (OwnerGrid.MergedRegions is not { Count: > 0 } merges)
                return false;

            var address = new CellAddress(OwnerGrid.ActiveSheetId, row, column);
            foreach (var merge in merges)
            {
                if (merge.Contains(address))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the cell is one of the host-supplied <see cref="HyperlinkCells"/> (the same
        /// set GridView.Input.cs consults for the Ctrl+hover hand cursor), so the cell's
        /// AutomationPeer can announce "has a hyperlink".
        /// </summary>
        internal bool IsCellHyperlinked(uint row, uint column) =>
            OwnerGrid.HyperlinkCells is { Count: > 0 } links &&
            links.Contains(new CellAddress(OwnerGrid.ActiveSheetId, row, column));

        internal Rect GetCellBoundingRectangle(uint row, uint column)
        {
            var viewport = OwnerGrid.Viewport;
            if (viewport is null || !TryGetSplitAwareBounds(viewport, OwnerGrid.ShowHeaders, row, column, out var bounds))
                return Rect.Empty;

            try
            {
                var topLeft = OwnerGrid.PointToScreen(bounds.TopLeft);
                var bottomRight = OwnerGrid.PointToScreen(bounds.BottomRight);
                return new Rect(topLeft, bottomRight);
            }
            catch (InvalidOperationException)
            {
                return bounds;
            }
        }

        /// <summary>
        /// Resolves a cell's bounds honoring split panes: cells that live only in
        /// SplitPanes.TopRows/LeftColumns/BottomLeftRows/TopRightColumns are looked up in
        /// those metric lists (not just the main Viewport.RowMetrics/ColMetrics), and the
        /// pane origin is offset by the pinned-pane extent (via the split divider layout)
        /// so bounds match what GridView actually renders, mirroring
        /// GridView.SplitPanes.cs's HitTestViewportCell/SplitPaneCellLayoutPlanner logic.
        /// </summary>
        private static bool TryGetSplitAwareBounds(ViewportModel viewport, bool showHeaders, uint row, uint column, out Rect bounds)
        {
            var rowHeaderWidth = showHeaders ? GridView.CalculateRowHeaderWidth(viewport) : 0.0;
            var colHeaderHeight = showHeaders ? GridView.CalculateColumnHeaderHeight(viewport) : 0.0;
            if (!ViewportGeometryPlanner.TryGetCellBounds(
                    viewport,
                    row,
                    column,
                    new ViewportGeometrySettings(
                        rowHeaderWidth,
                        colHeaderHeight,
                        MetricPlacement: ViewportMetricPlacement.MetricOffsets,
                        SplitColumnHeaderHeight: GridView.ColHeaderHeight,
                        SplitRowHeaderWidth: GridView.CalculateRowHeaderWidth(viewport)),
                    out var layoutBounds))
            {
                bounds = Rect.Empty;
                return false;
            }

            bounds = new Rect(
                layoutBounds.X,
                layoutBounds.Y,
                layoutBounds.Width,
                layoutBounds.Height);
            return true;
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface switch
            {
                PatternInterface.Grid => this,
                PatternInterface.Selection => this,
                _ => base.GetPattern(patternInterface)
            };

        /// <summary>
        /// Raises UIA selection/focus notifications for the new active cell (the host's
        /// tracked anchor/active cell, or the top-left corner of the current selection when
        /// no anchor is tracked). Called whenever ActiveCell/SelectedRange/SelectedRanges
        /// changes so screen readers announce cell navigation with the cell's address and
        /// current value, matching Excel's behavior on arrow-key/Tab/Enter movement.
        /// </summary>
        internal void NotifySelectionChanged(CellAddress? activeCell)
        {
            var previousActiveCell = _lastNotifiedActiveCell;
            _lastNotifiedActiveCell = activeCell;

            if (activeCell == previousActiveCell)
                return;

            RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);

            if (activeCell is not { } address)
            {
                _lastNotifiedActiveCellDisplayText = null;
                return;
            }

            var cellPeer = GetOrCreateCellPeer(address.Row, address.Col);

            if (previousActiveCell is { } previousAddress &&
                _cellPeers.TryGetValue((previousAddress.Row, previousAddress.Col), out var previousPeer))
            {
                previousPeer.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
            }

            cellPeer.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
            cellPeer.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
            cellPeer.NotifyNameChanged();
            _lastNotifiedActiveCellDisplayText = GetCellDisplayText(address.Row, address.Col);
        }

        /// <summary>
        /// Re-announces the still-active cell's Name/Value when its displayed content changes
        /// without a selection move (e.g. Ctrl+Enter commit that leaves the selection in place,
        /// or an F9 recalc that updates the focused formula cell), so a screen reader's braille
        /// display / value readout does not keep showing the pre-edit value. Called whenever the
        /// viewport is rebuilt (see GridView.Properties.cs OnViewportChanged).
        /// </summary>
        internal void NotifyActiveCellValueIfChanged()
        {
            if (_lastNotifiedActiveCell is not { } address)
                return;

            var displayText = GetCellDisplayText(address.Row, address.Col);
            if (displayText == _lastNotifiedActiveCellDisplayText)
                return;

            _lastNotifiedActiveCellDisplayText = displayText;
            var cellPeer = GetOrCreateCellPeer(address.Row, address.Col);
            cellPeer.NotifyNameChanged();
        }

        protected override List<AutomationPeer> GetChildrenCore()
        {
            var rows = GetVisibleRows(OwnerGrid.Viewport);
            var columns = GetVisibleColumns(OwnerGrid.Viewport);
            if (rows.Count == 0 || columns.Count == 0)
                return [];

            var children = new List<AutomationPeer>(rows.Count * columns.Count);
            foreach (var row in rows)
            {
                foreach (var column in columns)
                    children.Add(GetOrCreateCellPeer(row, column));
            }

            return children;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.DataGrid;

        protected override string GetClassNameCore() => nameof(GridView);

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        private GridViewCellAutomationPeer GetOrCreateCellPeer(uint row, uint column)
        {
            var key = (row, column);
            if (_cellPeers.TryGetValue(key, out var peer))
                return peer;

            peer = new GridViewCellAutomationPeer(this, row, column);
            _cellPeers[key] = peer;
            return peer;
        }

        /// <summary>
        /// Drops cached peers for cells no longer in the visible viewport (main + split
        /// panes) or the active cell, so navigating/scrolling a large workbook does not
        /// accumulate unbounded automation peers for the lifetime of the grid.
        /// </summary>
        internal void EvictStaleCellPeers()
        {
            if (_cellPeers.Count == 0)
                return;

            var rows = GetVisibleRows(OwnerGrid.Viewport);
            var columns = GetVisibleColumns(OwnerGrid.Viewport);

            List<(uint Row, uint Col)>? stale = null;
            foreach (var key in _cellPeers.Keys)
            {
                if (rows.Contains(key.Row) && columns.Contains(key.Col))
                    continue;

                if (_lastNotifiedActiveCell is { } active && active.Row == key.Row && active.Col == key.Col)
                    continue;

                (stale ??= []).Add(key);
            }

            if (stale is null)
                return;

            foreach (var key in stale)
                _cellPeers.Remove(key);
        }

        private bool TryGetDisplayCell(uint row, uint column, out DisplayCell cell)
        {
            if (TryGetDisplayCell(OwnerGrid.Viewport?.Cells, row, column, out cell))
                return true;

            return TryGetDisplayCell(OwnerGrid.Viewport?.SplitPanes?.Cells, row, column, out cell);
        }

        private static bool TryGetDisplayCell(IReadOnlyList<DisplayCell>? cells, uint row, uint column, out DisplayCell cell)
        {
            if (cells is not null)
            {
                foreach (var candidate in cells)
                {
                    if (candidate.Row == row && candidate.Col == column)
                    {
                        cell = candidate;
                        return true;
                    }
                }
            }

            cell = default;
            return false;
        }

        private static IReadOnlyList<uint> GetVisibleRows(ViewportModel? viewport)
        {
            if (viewport is null)
                return [];

            var rows = new List<uint>();
            AddRows(rows, viewport.RowMetrics);
            AddRows(rows, viewport.SplitPanes?.TopRows);
            AddRows(rows, viewport.SplitPanes?.BottomLeftRows);
            return rows;
        }

        private static IReadOnlyList<uint> GetVisibleColumns(ViewportModel? viewport)
        {
            if (viewport is null)
                return [];

            var columns = new List<uint>();
            AddColumns(columns, viewport.ColMetrics);
            AddColumns(columns, viewport.SplitPanes?.LeftColumns);
            AddColumns(columns, viewport.SplitPanes?.TopRightColumns);
            return columns;
        }

        private static void AddRows(List<uint> rows, IReadOnlyList<RowMetric>? metrics)
        {
            if (metrics is null)
                return;

            foreach (var metric in metrics)
            {
                if (!rows.Contains(metric.Row))
                    rows.Add(metric.Row);
            }
        }

        private static void AddColumns(List<uint> columns, IReadOnlyList<ColMetric>? metrics)
        {
            if (metrics is null)
                return;

            foreach (var metric in metrics)
            {
                if (!columns.Contains(metric.Col))
                    columns.Add(metric.Col);
            }
        }

        private static int IndexOf(IReadOnlyList<uint> values, uint value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                    return i;
            }

            return -1;
        }

        private static bool ContainsCell(GridRange range, uint row, uint column) =>
            row >= range.Start.Row &&
            row <= range.End.Row &&
            column >= range.Start.Col &&
            column <= range.End.Col;
    }

    private sealed class GridViewCellAutomationPeer(
        GridViewAutomationPeer parent,
        uint row,
        uint column) :
        AutomationPeer,
        IGridItemProvider,
        IValueProvider,
        ISelectionItemProvider
    {
        public int Row => parent.GetRowIndex(row);

        public int Column => parent.GetColumnIndex(column);

        public int RowSpan => 1;

        public int ColumnSpan => 1;

        public IRawElementProviderSimple ContainingGrid => ProviderFromPeer(parent);

        public bool IsReadOnly => true;

        public string Value => parent.GetCellDisplayText(row, column);

        public bool IsSelected => parent.IsCellSelected(row, column);

        public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(parent);

        public void SetValue(string value) =>
            throw new InvalidOperationException("Grid cells are edited through the worksheet editor.");

        public void Select() =>
            throw new InvalidOperationException("Grid cell selection is owned by the worksheet surface.");

        public void AddToSelection() =>
            throw new InvalidOperationException("Grid cell selection is owned by the worksheet surface.");

        public void RemoveFromSelection() =>
            throw new InvalidOperationException("Grid cell selection is owned by the worksheet surface.");

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface switch
            {
                PatternInterface.GridItem => this,
                PatternInterface.Value => this,
                PatternInterface.SelectionItem => this,
                _ => null
            };

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.DataItem;

        protected override string GetClassNameCore() => "GridViewCell";

        protected override string GetAutomationIdCore() =>
            $"Cell_{CellAddress.NumberToColumnName(column)}{row}";

        protected override string GetNameCore()
        {
            var address = $"{CellAddress.NumberToColumnName(column)}{row}";

            // Append "has note"/"has comment", "is a formula", "is merged", and "has a hyperlink"
            // cues so a screen reader can discover metadata a sighted user sees via the corner-
            // triangle indicator (GridView.Rendering.cs DrawCommentIndicator), the formula-bar "="
            // prefix, a merged span, and the Ctrl+hover hand cursor, respectively. See
            // R80-app-accessibility-a11y-5-3 (comment cue, added round 80) and its R81 completion
            // (formula/merged/hyperlink cues; data-validation/locked deliberately left unwired --
            // see CellAnnouncementMetadata's doc comment for why).
            parent.TryGetCellComment(row, column, out var comment);
            var metadata = new CellAnnouncementMetadata(
                HasComment: comment is not null,
                CommentTitle: comment?.Title,
                IsFormula: parent.IsCellFormula(row, column),
                IsMerged: parent.IsCellMerged(row, column),
                HasHyperlink: parent.IsCellHyperlinked(row, column));

            return GridView.BuildCellAnnouncementName(address, Value, metadata);
        }

        protected override Rect GetBoundingRectangleCore() =>
            parent.GetCellBoundingRectangle(row, column);

        protected override List<AutomationPeer> GetChildrenCore() => [];

        protected override Point GetClickablePointCore()
        {
            var bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty
                ? new Point(double.NaN, double.NaN)
                : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override string GetHelpTextCore() =>
            parent.TryGetCellComment(row, column, out var comment) && comment is not null
                ? $"{comment.Title}: {comment.Body}"
                : string.Empty;

        protected override string GetItemStatusCore() => string.Empty;

        protected override string GetItemTypeCore() => string.Empty;

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override string GetLocalizedControlTypeCore() => "cell";

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        protected override bool HasKeyboardFocusCore() => parent.IsActiveCell(row, column);

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => true;

        /// <summary>
        /// Raises a UIA Name-property-changed notification for this cell so screen readers
        /// re-announce its address and current value when it becomes the active cell.
        /// </summary>
        internal void NotifyNameChanged()
        {
            var name = GetNameCore();
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, null, name);
        }

        protected override bool IsOffscreenCore() =>
            GetBoundingRectangleCore().IsEmpty;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => false;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override void SetFocusCore()
        {
        }
    }

    public const double ColHeaderHeight = 18;
    public const double RowHeaderWidth = 30;

    public double ActualRowHeaderWidth => ShowHeaders ? CalculateRowHeaderWidth(Viewport) : 0.0;

    public double EffectiveColHeaderHeight => ShowHeaders ? CalculateColumnHeaderHeight(Viewport) : 0.0;

    public static double CalculateRowHeaderWidth(ViewportModel? viewport)
    {
        var maxRow = viewport?.RowMetrics.Count > 0
            ? viewport.RowMetrics[^1].Row
            : 0u;

        return maxRow switch
        {
            >= 1_000_000 => 54,
            >= 100_000   => 48,
            >= 10_000    => 42,
            >= 1_000     => 36,
            _            => RowHeaderWidth,
        } + CalculateRowOutlineGutterWidth(viewport);
    }

    /// <summary>
    /// Computes the row header width from the last-visible-row number and outline groups
    /// without requiring a fully-materialized viewport. Used to determine the correct width
    /// before building the viewport so it is built only once.
    /// </summary>
    public static double CalculateRowHeaderWidth(
        uint lastVisibleRow,
        IReadOnlyList<OutlineGroupRange> rowOutlineGroups)
    {
        var baseWidth = lastVisibleRow switch
        {
            >= 1_000_000 => 54,
            >= 100_000   => 48,
            >= 10_000    => 42,
            >= 1_000     => 36,
            _            => RowHeaderWidth,
        };

        var maxLevel = 0;
        foreach (var group in rowOutlineGroups)
        {
            if (group.Level > maxLevel)
                maxLevel = group.Level;
        }

        var gutterWidth = maxLevel <= 0
            ? 0
            : OutlineGutterPadding * 2 + maxLevel * OutlineLevelPitch;

        return baseWidth + gutterWidth;
    }

    public static double CalculateColumnHeaderHeight(ViewportModel? viewport) =>
        ColHeaderHeight + CalculateColumnOutlineGutterHeight(viewport);

    private const double ResizeHitZone = 4;
    private const double SplitDividerHitZone = 4;
    private const double OutlineLevelPitch = 14;
    private const double OutlineGutterPadding = 6;
    private const double OutlineButtonSize = 13;
    private const double DefaultCellFontSizePoints = 11.0;
    internal const double SuperSubFontSizeFactor = CellTextMaterializationPlanner.ScriptFontSizeFactor;
    internal const double SuperScriptBaselineRatio = CellTextMaterializationPlanner.SuperscriptBaselineRatio;
    internal const double SubScriptBaselineRatio = CellTextMaterializationPlanner.SubscriptBaselineRatio;
    private const double PageMarginGuideHitZone = 5;
    private const int MarchingAntsPhaseCount = 16;

    private static readonly Typeface DefaultTypeface = new("Calibri");
    private static readonly Brush GridLineBrush = MakeBrush(220, 220, 220);
    private static readonly Brush TextBrush = Brushes.Black;
    private static readonly Brush HeaderBackgroundBrush = MakeBrush(242, 242, 242);
    private static readonly Brush HeaderHighlightBrush = MakeBrush(218, 232, 218);
    private static readonly Brush OutlineGlyphBrush = MakeBrush(84, 130, 53);
    private static readonly Brush OutlineButtonBrush = MakeBrush(255, 255, 255);
    private static readonly Pen OutlineGlyphPen = MakePen(MakeBrush(84, 130, 53), 1);
    private static readonly Pen OutlineButtonPen = MakePen(MakeBrush(117, 117, 117), 1);
    private static readonly Pen GridPen = MakeGridPen();
    private static readonly Brush SelectionBrush = MakeBrushAlpha(32, 33, 115, 70);
    private static readonly Pen SelectionPen = MakePen(MakeBrush(33, 115, 70), 2);
    private static readonly Brush SelectionHandleBrush = MakeBrush(33, 115, 70);
    private static readonly Brush QuickAnalysisPreviewBrush = MakeBrushAlpha(38, 91, 155, 213);
    private static readonly Pen QuickAnalysisPreviewPen = MakePen(MakeBrush(47, 117, 181), 2);
    private static readonly Brush QuickAnalysisDataBarPreviewBrush = MakeBrushAlpha(156, 91, 155, 213);
    private static readonly Brush[] QuickAnalysisColorScalePreviewBrushes =
    [
        MakeBrushAlpha(176, 248, 105, 107),
        MakeBrushAlpha(176, 255, 235, 132),
        MakeBrushAlpha(176, 99, 190, 123)
    ];
    private static readonly Brush[] QuickAnalysisIconSetPreviewBrushes =
    [
        MakeBrush(99, 190, 123),
        MakeBrush(255, 192, 0),
        MakeBrush(248, 105, 107)
    ];
    private static readonly Brush QuickAnalysisHighlightPreviewBrush = MakeBrushAlpha(96, 255, 235, 156);
    private static readonly Pen QuickAnalysisHighlightPreviewPen = MakePen(MakeBrush(191, 143, 0), 1);
    private static readonly Brush QuickAnalysisClearFormatPreviewBrush = MakeBrushAlpha(50, 217, 217, 217);
    private static readonly Pen QuickAnalysisClearFormatPreviewPen = MakePen(MakeBrush(128, 128, 128), 1);
    private static readonly Brush QuickAnalysisTotalPreviewBrush = MakeBrushAlpha(70, 198, 239, 206);
    private static readonly Pen QuickAnalysisTotalPreviewPen = MakePen(MakeBrush(84, 130, 53), 1);
    private static readonly Brush QuickAnalysisTablePreviewBrush = MakeBrushAlpha(58, 189, 215, 238);
    private static readonly Pen QuickAnalysisTablePreviewPen = MakePen(MakeBrush(91, 155, 213), 1);
    private static readonly Pen QuickAnalysisSparklinePreviewPen = MakePen(MakeBrush(68, 114, 196), 1.5);
    private static readonly Brush QuickAnalysisWinLossPositiveBrush = MakeBrushAlpha(180, 84, 130, 53);
    private static readonly Brush QuickAnalysisWinLossNegativeBrush = MakeBrushAlpha(180, 192, 80, 77);
    private static readonly Brush QuickAnalysisColumnChartPreviewBrush = MakeBrushAlpha(170, 68, 114, 196);
    private static readonly Brush QuickAnalysisPieChartAccentBrush = MakeBrushAlpha(176, 237, 125, 49);
    private static readonly Brush QuickAnalysisAreaChartPreviewBrush = MakeBrushAlpha(96, 68, 114, 196);
    private static readonly Brush QuickAnalysisScatterChartPreviewBrush = MakeBrushAlpha(190, 112, 173, 71);
    private static readonly Pen QuickAnalysisColumnChartAxisPen = MakePen(MakeBrush(89, 89, 89), 1);
    private static readonly double[] QuickAnalysisColumnChartHeights = [0.42, 0.76, 0.58, 0.9];
    private static readonly double[] QuickAnalysisStackedColumnChartHeights = [0.68, 0.84, 0.58, 0.92];
    private static readonly double[] QuickAnalysisStackedColumnChartTopSegments = [0.36, 0.48, 0.42, 0.31];
    private static readonly double[] QuickAnalysisBarChartWidths = [0.48, 0.86, 0.64, 0.72];
    private static readonly (double X, double Y)[] QuickAnalysisLineChartPointFactors =
    [
        (0.0, 0.74),
        (0.32, 0.32),
        (0.66, 0.56),
        (1.0, 0.18)
    ];
    private static readonly (double X, double Y)[] QuickAnalysisAreaChartPointFactors =
    [
        (0.0, 0.78),
        (0.28, 0.36),
        (0.62, 0.52),
        (1.0, 0.2)
    ];
    private static readonly (double X, double Y)[] QuickAnalysisScatterChartPointFactors =
    [
        (0.18, 0.72),
        (0.35, 0.42),
        (0.55, 0.62),
        (0.78, 0.28)
    ];
    private static readonly Pen ResizeLinePen = MakeResizeLinePen();
    private static readonly Pen AutofillPreviewPen = MakeAutofillPreviewPen();
    private static readonly Pen FreezePen = MakeFreezePen();
    private static readonly Brush PageBreakPreviewBrush = MakeBrushAlpha(46, 0, 103, 192);
    private static readonly Brush PageBreakOutsideMaskBrush = MakeBrushAlpha(96, 188, 206, 228);
    private static readonly Brush PageBreakWatermarkBrush = MakeBrushAlpha(92, 0, 103, 192);
    private static readonly Brush PageLayoutPageSurfaceBrush = MakeBrushAlpha(42, 255, 255, 255);
    private static readonly Pen PageBreakPen = MakePageBreakPen();
    private static readonly Pen PageBreakAutomaticPen = MakePageBreakAutomaticPen();
    private static readonly Pen PageBreakPreviewPagePen = MakePageBreakPreviewPagePen();
    private static readonly Pen PageLayoutPen = MakePageLayoutPen();
    private static readonly Pen PageLayoutHeaderFooterCuePen = MakePageLayoutHeaderFooterCuePen();
    private static readonly Pen PageMarginGuidePen = MakePageMarginGuidePen();
    private static readonly Pen PageMarginRulerHandlePen = MakePen(MakeBrush(75, 75, 75), 1);
    private static readonly Brush PageMarginRulerHandleBrush = MakeBrush(238, 238, 238);
    private static readonly Pen SplitPanePen = MakeSplitPanePen();
    private static readonly Brush SplitDividerHandleBrush = MakeBrush(112, 112, 112);
    private static readonly Pen SplitDividerHandlePen = MakePen(SplitDividerHandleBrush, 1);
    private static readonly Brush SplitScrollbarTrackBrush = MakeBrush(244, 244, 244);
    private static readonly Brush SplitScrollbarThumbBrush = MakeBrush(188, 188, 188);
    private static readonly Pen SplitScrollbarPen = MakePen(MakeBrush(196, 196, 196), 1);
    private static readonly Brush FormulaTraceArrowBrush = MakeBrush(0, 102, 204);
    private static readonly Pen FormulaTraceArrowPen = MakeFormulaTraceArrowPen();
    private static readonly Pen ValidationCirclePen = MakePen(MakeBrush(226, 28, 33), 1.5);
    private static readonly Pen[] MarchingAntsBlackPens = CreateMarchingAntsPens(Brushes.Black, 2.5);
    private static readonly Pen[] MarchingAntsCopyOverlayPens = CreateMarchingAntsPens(Brushes.White, 1.5);
    // Excel does not color-differentiate a Cut marquee from a Copy marquee -- both use the same
    // black/white marching ants (R75-render-selection-marquee-4-4). Route Cut through the same
    // white overlay pens as Copy rather than a distinct orange.
    private static readonly Pen[] MarchingAntsCutOverlayPens = MarchingAntsCopyOverlayPens;

    // Per-frame render caches: allocated once and cleared at the start of each render pass
    // to avoid GC pressure from fresh Dictionary allocations on every frame.
    private readonly Dictionary<CellColor, SolidColorBrush> _brushCache = new();
    private readonly Dictionary<CellBorder, Pen> _borderPenCache = new();
    private readonly Dictionary<CellColor, Pen> _fillPatternPenCache = new();
    private readonly Dictionary<CellTypefaceKey, Typeface> _typefaceCache = new();
    private readonly Dictionary<Brush, Pen> _underlinePenCache = new();
    private readonly Dictionary<DefaultTextLayoutKey, FormattedText> _defaultTextLayoutCache = new();
    private readonly Dictionary<DefaultWrappedTextLayoutKey, FormattedText> _defaultWrappedTextLayoutCache = new();
    private readonly Dictionary<CellStyle, bool> _defaultTextLayoutStyleCache = new(CellStyleReferenceComparer.Instance);
    private readonly Dictionary<TextWidthLayoutKey, double> _textWidthLayoutCache = new();
    private readonly Dictionary<ShrinkTextLayoutKey, double> _shrinkTextLayoutCache = new();
    private readonly Dictionary<Rect, RectangleGeometry> _cellClipGeometryCache = new();
    private readonly Dictionary<Rect, Geometry> _commentIndicatorGeometryCache = new();
    private readonly Dictionary<ChartRenderCacheKey, ImageSource> _chartRenderCache = new();
    private RenderCellLookupCache? _renderCellLookupCache;
    private RenderMetricLookupCache? _renderMetricLookupCache;
    private OccupiedCellLookupCache? _occupiedCellLookupCache;
    private PageBreakLookupCache? _rowPageBreakLookupCache;
    private PageBreakLookupCache? _columnPageBreakLookupCache;

    internal static double ToDisplayFontSize(double pointSize) =>
        Math.Max(1.0, pointSize * (96.0 / 72.0));

    private sealed class CellStyleReferenceComparer : IEqualityComparer<CellStyle>
    {
        public static readonly CellStyleReferenceComparer Instance = new();

        private CellStyleReferenceComparer()
        {
        }

        public bool Equals(CellStyle? x, CellStyle? y) => ReferenceEquals(x, y);

        public int GetHashCode(CellStyle obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public static double ResolveShrinkFontSize(
        double requestedFontSize,
        double availableWidth,
        Func<double, double> measureTextWidth,
        double minimumFontSize = 6.0)
    {
        if (requestedFontSize <= minimumFontSize || availableWidth <= 0)
            return Math.Min(requestedFontSize, minimumFontSize);

        var fontSize = requestedFontSize;
        while (fontSize > minimumFontSize && measureTextWidth(fontSize) > availableWidth)
            fontSize = Math.Max(minimumFontSize, fontSize - 1);

        return fontSize;
    }

    public static bool CanOverflowCellText(CellStyle? style, ScalarValue? rawValue, string? displayText, GridRange? merge)
        => CellTextOverflowPlanner.CanOverflowCellText(style, rawValue, displayText, merge);

    public static CellAddress ConstrainAutofillTarget(GridRange source, CellAddress target)
        => GridAutofillPlanner.ConstrainTarget(source, target);

    private static Pen MakeResizeLinePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeAutofillPreviewPen()
    {
        var pen = new Pen(MakeBrush(0, 0, 0), 2.0)
        {
            DashStyle = new DashStyle([4.0, 4.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen[] CreateMarchingAntsPens(Brush brush, double thickness)
    {
        var pens = new Pen[MarchingAntsPhaseCount];
        for (var phase = 0; phase < pens.Length; phase++)
            pens[phase] = MakeMarchingAntsPen(brush, thickness, phase / 2.0);

        return pens;
    }

    private static Pen MakeMarchingAntsPen(Brush brush, double thickness, double offset)
    {
        var pen = new Pen(brush, thickness)
        {
            DashStyle = new DashStyle([4.0, 4.0], offset)
        };
        pen.Freeze();
        return pen;
    }

    private static int GetMarchingAntsPhase(double offset)
    {
        var phase = (int)Math.Round(offset * 2, MidpointRounding.AwayFromZero) % MarchingAntsPhaseCount;
        return phase < 0 ? phase + MarchingAntsPhaseCount : phase;
    }

    private static Pen MakeGridPen()
    {
        return MakePen(GridLineBrush, 1);
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeFreezePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 200)), 2);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageBreakPen()
    {
        // R91-render-frozen-print-titles-5-1: real Excel draws a MANUAL page break as a thick SOLID
        // line, reserving dashed strokes for AUTOMATIC breaks (MakePageBreakAutomaticPen below) -- the
        // opposite convention from what this pen used to apply -- so a manual break stays visually
        // distinct from one Excel inserted on its own.
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 103, 192)), 2);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageBreakAutomaticPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 103, 192)), 1.25)
        {
            DashStyle = new DashStyle([2.0, 3.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageBreakPreviewPagePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 103, 192)), 1.5);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageLayoutPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.5);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageLayoutHeaderFooterCuePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(156, 156, 156)), 1)
        {
            DashStyle = new DashStyle([4.0, 4.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageMarginGuidePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 150, 220)), 1)
        {
            DashStyle = new DashStyle([3.0, 3.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakeSplitPanePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 3);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeFormulaTraceArrowPen()
    {
        var pen = new Pen(FormulaTraceArrowBrush, 1.5);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MakeBrushAlpha(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
