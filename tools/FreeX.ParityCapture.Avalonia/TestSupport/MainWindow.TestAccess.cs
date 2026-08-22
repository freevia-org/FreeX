using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.DataTools;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.Rendering;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Presentation.Sparklines;
using FreeX.App.Presentation.TextToColumns;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.App.Services.Updates;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;
using AutoFilterDropdownMenuPlanner = FreeX.App.Presentation.Filtering.AutoFilterDropdownMenuPlanner;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal static readonly global::Avalonia.Media.Color ChromeSurfaceColor =
        AvaloniaThemeResourceResolver.ResolveOr(
            ThemeResources.Color("ChromeSurface"),
            global::Avalonia.Media.Color.FromRgb(0xF7, 0xF8, 0xF8));

    private int _sheetGridBuildCount;
    private int _sheetTabsBuildCount;

    /// <summary>
    /// Test-only accessor for the active <see cref="WorkbookSession"/> so headless regression tests
    /// (e.g. Format Cells number-format seeding) can set up cell state before driving dialog methods
    /// directly. Not used by production code paths.
    /// </summary>
    internal WorkbookSession Session => _session;

    internal static AvaloniaWorkbookWindowRegistry WindowRegistryForTest => WindowRegistry;

    internal RibbonContextState RibbonContextStateForTest => _ribbonContextSource.Current;

    internal static IReadOnlySet<string> InteractiveValidationKeyboardShortcutScenarioIds { get; } =
        FreeX.App.Presentation.InteractionValidation.InteractiveValidationInventory.KeyboardShortcuts
            .Select(scenario => scenario.Id)
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Test-only seam that rebuilds the worksheet grid (as <see cref="RefreshShell"/> does on every
    /// scroll/edit/zoom) and returns the resulting visual, so headless regression tests can mutate
    /// <see cref="Session"/> state (merges, freeze panes, selection, viewport origin) and then inspect
    /// the actual rendered <see cref="Control"/> tree rather than only asserting on source text. Not
    /// used by production code paths.
    /// </summary>
    internal Control RebuildSheetGridForTest() => BuildSheetGrid();

    internal int SheetGridBuildCountForTest => _sheetGridBuildCount;

    /// <summary>Test-only counter of <see cref="BuildSheetTabs"/> calls (see RefreshShellForViewportPan).</summary>
    internal int SheetTabsBuildCountForTest => _sheetTabsBuildCount;

    partial void RecordSheetTabsBuilt() => _sheetTabsBuildCount++;

    partial void RecordSheetGridBuilt() => _sheetGridBuildCount++;

    /// <summary>
    /// Test-only driver that reproduces a full mouse cell-selection drag from <paramref name="anchor"/>
    /// (the pressed cell) to <paramref name="cursor"/>, then the release. It mirrors the real gesture:
    /// <c>BeginCellSelectionDrag</c> records the pressed cell as the transient anchor,
    /// <c>ContinueCellSelectionDrag</c> extends to the cursor via <c>SelectRangeFromAnchor</c>, and
    /// <c>EndCellSelectionDragAsync</c> clears the transient anchor/cursor fields -- the exact sequence
    /// that used to discard the true anchor before View &gt; Split / Freeze Panes could read it. Not
    /// used by production code paths.
    /// </summary>
    internal void RaiseCellSelectionDragForTest(CellAddress anchor, CellAddress cursor)
    {
        _cellDragSelectionAnchor = anchor;
        SelectRangeFromAnchor(anchor, cursor);
        _cellDragSelectionAnchor = null;
        _selectionExtensionAnchor = null;
        _selectionExtensionCursor = null;
    }

    /// <summary>
    /// Test-only entry point for the View &gt; Split ribbon command (<c>SplitPanesAtActiveCell</c>),
    /// which is otherwise reached only through the ribbon command map (<c>["view.split"]</c>). Not used
    /// by production code paths.
    /// </summary>
    internal void InvokeSplitPanesAtActiveCellForTest() => SplitPanesAtActiveCell();

    /// <summary>
    /// Test-only accessor for the persistent worksheet grid host (survives RefreshShell/
    /// BuildSheetGrid rebuilds — see its field comment), so headless accessibility regression tests
    /// can move real keyboard focus onto it before driving navigation, exactly as a screen-reader
    /// user tabbing into the grid would. Not used by production code paths.
    /// </summary>
    internal Control SheetGridHostForTest => _sheetGridHost;

    /// <summary>
    /// R119-avalonia-drag-preview: test-only accessor for the sheet grid host's CURRENT hosted
    /// content (as last assigned by RefreshShell/RefreshShellForViewportPan/RefreshShellForGridPreview/
    /// BuildSheetGrid), distinct from <see cref="RebuildSheetGridForTest"/> (which forces a brand
    /// new rebuild). Lets a test prove that a given call -- e.g. ContinueAutofillDrag -- already
    /// rebuilt the hosted content itself, without the test performing its own separate rebuild that
    /// would mask a missing rebuild call in production code. Not used by production code paths.
    /// </summary>
    internal Control? SheetGridHostContentForTest => _sheetGridHost.Content as Control;

    /// <summary>
    /// Test-only accessor for the active cell's real Border control (see
    /// <see cref="_activeCellBorder"/>/<see cref="MoveFocusToActiveCellBorder"/>), so headless
    /// accessibility regression tests can assert which control keyboard focus actually lands on
    /// after grid navigation, and read its AutomationProperties Name/AutomationId. Not used by
    /// production code paths.
    /// </summary>
    internal Control? ActiveCellBorderForTest => _activeCellBorder;

    internal bool DataValidationDropdownOpenForTest =>
        _activeDataValidationDropdown?.IsDropDownOpen == true;

    /// <summary>
    /// Test-only accessor for the Name Box's current text (K23 regression coverage), so headless
    /// tests can seed typed input before driving <see cref="RaiseCellAddressBoxKeyDownForTest"/> and
    /// assert the box's resulting displayed text. Not used by production code paths.
    /// </summary>
    internal string? CellAddressBoxTextForTest
    {
        get => _cellAddressText.Text;
        set => _cellAddressText.Text = value;
    }

    /// <summary>
    /// Test-only seam that drives the real Name Box KeyDown handling (Enter-to-navigate,
    /// define-name-by-typing, Escape-to-restore) with a caller-supplied <see cref="KeyEventArgs"/>,
    /// so assertions can run against the resulting <see cref="Session"/>/box-text state rather than
    /// only a source-string proxy. Not used by production code paths.
    /// </summary>
    internal void RaiseCellAddressBoxKeyDownForTest(KeyEventArgs e) => CellAddressBox_KeyDown(_cellAddressText, e);

    /// <summary>
    /// Test-only seam exposing the Name Box's basic-autocomplete name list (the same list the
    /// dropdown chevron's flyout populates on open), so headless tests can assert its contents
    /// without needing to open a real Avalonia flyout. Not used by production code paths.
    /// </summary>
    internal IReadOnlyList<string> CellAddressAutocompleteNamesForTest() => BuildCellAddressAutocompleteNames();

    internal bool CellAddressAutocompleteOpenForTest => _cellAddressAutocompletePopup?.IsOpen == true;

    internal IReadOnlyList<string> CellAddressAutocompleteRenderedNamesForTest()
    {
        ShowCellAddressAutocompletePopup();
        return _cellAddressAutocompleteListBox!.Items
            .OfType<NameBoxNavigationItem>()
            .Select(item => item.Name)
            .ToArray();
    }

    internal bool CellAddressBoxHasPendingEditForTest => _cellAddressBoxHasPendingEdit;

    internal SelectionPaneObjectKind? SelectedDrawingObjectKindForTest => _selectedDrawingObjectKind;

    internal Guid? SelectedDrawingObjectIdForTest => _selectedDrawingObjectId;

    internal bool SelectCellAddressBoxItemForTest(NameBoxNavigationItem item) =>
        SelectCellAddressBoxItem(item);

    internal NameBoxNavigationItem? SelectCellAddressAutocompleteKeyboardForTest(params Key[] keys)
    {
        ShowCellAddressAutocompletePopup();
        foreach (var key in keys)
        {
            var item = key == Key.Enter
                ? GetSelectedCellAddressAutocompleteItem()
                : null;
            _cellAddressAutocompleteListBox!.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                Source = _cellAddressAutocompleteListBox,
                Handled = key == Key.Enter,
            });
            if (key == Key.Enter)
                return item;
        }

        return GetSelectedCellAddressAutocompleteItem();
    }

    /// <summary>
    /// Test-only accessor for the Formula Bar's current text, so headless tests can seed typed
    /// input before driving <see cref="RaiseFormulaBoxKeyDownForTest"/>. Not used by production
    /// code paths.
    /// </summary>
    internal string? FormulaBoxTextForTest
    {
        get => _formulaBox.Text;
        set => _formulaBox.Text = value;
    }

    /// <summary>
    /// Test-only seam that drives the real Formula Bar KeyDown handling (Enter/Tab commit-and-move,
    /// line-break insertion) with a caller-supplied <see cref="KeyEventArgs"/>, so assertions can run
    /// against the resulting <see cref="Session"/> state rather than only a source-string proxy. Not
    /// used by production code paths.
    /// </summary>
    internal void RaiseFormulaBoxKeyDownForTest(KeyEventArgs e) => FormulaBox_KeyDown(_formulaBox, e);

    internal void BeginFormulaEditForTest(CellAddress address, string? initialText = null) =>
        BeginFormulaEdit(address, initialText);

    /// <summary>
    /// Test-only seam that starts a Formula Bar edit through the same first-character path as a
    /// user typing <c>=</c>, then replaces the still-live formula text with the requested suffix.
    /// This keeps point mode active for tests that need to exercise worksheet reference selection.
    /// </summary>
    internal void BeginFormulaPointModeEditForTest(CellAddress address, string formulaText)
    {
        if (!_formulaRangeEditingSession.IsFormulaText(formulaText))
            throw new ArgumentException("Formula point-mode text must start with '='.", nameof(formulaText));

        BeginFormulaEdit(address, "=");
        _formulaBox.Text = formulaText;
        MoveFormulaBoxCaretToEnd();
    }

    internal int FormulaReferenceGripCountForTest => _formulaReferenceGripVisuals.Count;

    internal bool RaiseFormulaReferenceGripDragForTest(int highlightIndex, CellAddress target)
    {
        var editor = GetFormulaReferenceHighlightEditor();
        IReadOnlyList<FormulaReferenceHighlight> highlights = editor is null
            ? []
            : GetFormulaReferenceHighlights(editor.Text ?? "");
        if (editor is null || highlightIndex < 0 || highlightIndex >= highlights.Count ||
            highlights[highlightIndex].Range is not { } originalRange ||
            originalRange.Start.Sheet != target.Sheet)
        {
            return false;
        }

        var newRange = _formulaRangeEditingSession.PlanReferenceDrag(highlights[highlightIndex], target);
        return newRange is { } range &&
            TryApplyFormulaReferenceResize(editor, highlights[highlightIndex], range);
    }

    /// <summary>
    /// Test-only seam for the production sheet-tab route. Existing formulas are edited in Edit
    /// mode, so this intentionally exercises <see cref="SelectSheet"/> rather than the lower-level
    /// session transition directly.
    /// </summary>
    internal bool SelectFormulaReferenceSheetForTest(SheetId sheetId)
    {
        if (GetFormulaReferenceHighlightEditor() is null)
            return false;

        SelectSheet(sheetId, selectRange: false, toggle: false);
        return _session.ActiveSheet.Id == sheetId && _session.FormulaEditAddress is not null;
    }

    internal bool FormulaPointModeForTest => _formulaRangeEditingSession.PointMode;

    internal ExcelSelectionMode FormulaRangeEntrySelectionModeForTest =>
        _formulaRangeEditingSession.SelectionMode;

    /// <summary>
    /// R92-meta-2 test seam: whether the function-name AutoComplete popup is currently open, and the
    /// candidate list it is showing. Not used by production code paths.
    /// </summary>
    internal bool FunctionAutocompleteOpenForTest => FunctionAutocompleteIsOpen;

    internal IReadOnlyList<string> FunctionAutocompleteCandidatesForTest =>
        _formulaRangeEditingSession.FunctionAutocompleteCandidates;

    /// <summary>
    /// R93-formula-editing-assist-5-2 test seam: whether the live argument-signature tooltip is
    /// currently open, and the text it renders (function name plus its bracketed-optional argument
    /// list, e.g. "VLOOKUP(lookup_value, table_array, col_index_num, [range_lookup])"). Not used by
    /// production code paths.
    /// </summary>
    internal bool SignatureHelpOpenForTest => _signatureHelpPopup?.IsOpen == true;

    internal string SignatureHelpTextForTest => _signatureHelpTextBlock?.Inlines is { } inlines
        ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
        : "";

    /// <summary>
    /// R93-formula-editing-assist-5-2 test seam: the 0-based index (within
    /// <see cref="SignatureHelpTextForTest"/>'s argument list) of the run currently rendered bold
    /// (the argument the caret sits inside), or -1 when the tooltip is closed.
    /// </summary>
    internal int SignatureHelpBoldArgumentIndexForTest
    {
        get
        {
            if (_signatureHelpTextBlock?.Inlines is not { } inlines)
                return -1;

            var argumentIndex = -1;
            foreach (var run in inlines.OfType<Run>().Skip(1))
            {
                var text = run.Text ?? "";
                if (text is ", " or ")")
                    continue;

                argumentIndex++;
                if (run.FontWeight == FontWeight.Bold)
                    return argumentIndex;
            }

            return -1;
        }
    }

    /// <summary>
    /// R92-meta-2 test seam: drives the Formula Bar's real TextChanged handling with the Text and
    /// CaretIndex already at their post-keystroke values (native Avalonia TextBox typing -- the
    /// Formula Bar has no custom TextInput interception -- updates both atomically before raising
    /// TextChanged, unlike a bare property assignment), so headless tests can exercise
    /// RefreshFormulaFunctionAutocomplete via the exact FormulaBox_TextChanged production path
    /// instead of only calling it directly. Not used by production code paths.
    /// </summary>
    internal void SimulateFormulaBoxTypedTextForTest(string text, int caretIndex)
    {
        _formulaBox.Text = text;
        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        _formulaBox.CaretIndex = clamped;
        _formulaBox.SelectionStart = clamped;
        _formulaBox.SelectionEnd = clamped;
        FormulaBox_TextChanged(_formulaBox, null!);
    }

    /// <summary>
    /// R92-meta-2 test seam: drives the in-cell inline editor's real TextInput handling
    /// (<see cref="InlineCellEditor_TextInput"/> -&gt; TryApplyInlineCellTextInput -&gt;
    /// ApplyTextBoxEdit -&gt; RefreshFormulaFunctionAutocomplete), the same method a genuine keystroke
    /// invokes, so headless tests exercise the production entry point rather than a source-string
    /// proxy. Not used by production code paths.
    /// </summary>
    internal void RaiseInlineCellEditorTextInputForTest(string text)
    {
        if (_inlineCellEditor is not { } editor || _inlineCellEditAddress is not { } address)
            throw new InvalidOperationException("No inline cell editor is active.");

        InlineCellEditor_TextInput(
            address,
            editor,
            new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Source = editor, Text = text });
    }

    internal string? InlineCellEditorTextForTest => _inlineCellEditor?.Text ?? _inlineCellEditText;

    /// <summary>
    /// R78-render-inplace-editor-5-2 test seam: whether the in-cell editor's own reference-highlight
    /// overlay (mirroring the formula bar's <c>_formulaReferenceTextOverlay</c>) is currently showing
    /// colored reference runs over the cell being edited in place.
    /// </summary>
    internal bool InlineCellReferenceOverlayVisibleForTest => _inlineCellReferenceTextOverlay?.IsVisible ?? false;

    internal int InlineCellReferenceOverlayRunCountForTest => _inlineCellReferenceTextOverlay?.Inlines?.Count ?? 0;

    internal IBrush? InlineCellEditorForegroundForTest => _inlineCellEditor?.Foreground;

    internal void BeginInlineCellEditForTest(CellAddress address, string text, int caretIndex)
    {
        BeginInlineCellEdit(address, text, caretIndex);
        if (_inlineCellEditor is { } editor)
        {
            var caret = Math.Clamp(caretIndex, 0, editor.Text?.Length ?? 0);
            editor.CaretIndex = caret;
            editor.SelectionStart = caret;
            editor.SelectionEnd = caret;
            _pendingInlineCellCaretIndex = null;
        }
    }

    internal void RaiseInlineCellEditorKeyDownForTest(KeyEventArgs e)
    {
        if (_inlineCellEditor is not { } editor || _inlineCellEditAddress is not { } address)
            throw new InvalidOperationException("No inline cell editor is active.");

        InlineCellEditor_KeyDown(address, editor, e);
    }

    internal void SetFormulaBoxSelectionForTest(int start, int length)
    {
        var textLength = _formulaBox.Text?.Length ?? 0;
        _formulaBox.SelectionStart = Math.Clamp(start, 0, textLength);
        _formulaBox.SelectionEnd = Math.Clamp(start + Math.Max(0, length), 0, textLength);
        _formulaBox.CaretIndex = _formulaBox.SelectionEnd;
    }

    /// <summary>
    /// Test-only seam that drives the real worksheet pointer-wheel handling (row/column panning,
    /// Ctrl+wheel zoom) with a caller-supplied <see cref="PointerWheelEventArgs"/>, so assertions can
    /// run against the resulting <see cref="Session"/> state rather than only a source-string proxy.
    /// Not used by production code paths.
    /// </summary>
    internal void RaisePointerWheelChangedForTest(PointerWheelEventArgs e) => SheetScrollViewer_PointerWheelChanged(_sheetGridHost, e);

    /// <summary>
    /// Test-only seam for the production drag auto-scroll application path. The pointer lifecycle
    /// calls <see cref="RequestCellDragAutoScroll"/>; this seam lets headless tests exercise the
    /// same scrollbar, viewport-origin, and render-refresh behavior without fabricating native
    /// pointer-capture state.
    /// </summary>
    internal void RaiseCellDragAutoScrollForTest(GridAutoScrollRequest request) =>
        ApplyCellDragAutoScroll(request);

    /// <summary>
    /// Test-only seam that drives the real fill-handle drag CONTINUATION path
    /// (ContinueAutofillDragCore) directly, bypassing pointer capture, so headless tests can
    /// assert the live drag-preview overlay appears mid-drag -- distinct from the pre-existing
    /// <see cref="RaiseAutofillDragForTest"/>, which only exercises the post-release commit path.
    /// Not used by production code paths.
    /// </summary>
    internal void RaiseContinueAutofillDragForTest(GridRange source, CellAddress target)
    {
        _autofillDragging = true;
        _autofillSourceRange = source;
        ContinueAutofillDragCore(target);
    }

    /// <summary>
    /// Test-only seam that drives the real fill-handle drag commit path (source/target set directly
    /// instead of via pointer capture), so headless tests can assert on the resulting cell
    /// values/formulas without simulating pointer input. Not used by production code paths.
    /// </summary>
    internal void RaiseAutofillDragForTest(GridRange source, CellAddress target, bool ctrlHeld = false)
    {
        _autofillSourceRange = source;
        _autofillTarget = target;
        CommitAutofillDrag(ctrlHeld);
    }

    /// <summary>
    /// Test-only seam that drives the real fill-handle double-click commit path directly (instead
    /// of simulating a double-click <see cref="PointerPressedEventArgs"/>), so headless tests can
    /// assert on the resulting cell values without constructing pointer input. Not used by
    /// production code paths.
    /// </summary>
    internal void RaiseAutofillHandleDoubleClickForTest(GridRange source) =>
        CommitAutofillHandleDoubleClick(source);

    /// <summary>
    /// Test-only seam that drives the real selection-border-move drag CONTINUATION path
    /// (ContinueSelectionMoveDragCore) directly, bypassing pointer capture, so headless tests can
    /// assert the live drag-preview overlay appears mid-drag -- distinct from the pre-existing
    /// <see cref="RaiseSelectionMoveDragForTest"/>, which only exercises the post-release commit
    /// path. Not used by production code paths.
    /// </summary>
    internal void RaiseContinueSelectionMoveDragForTest(GridRange source, CellAddress startCell, CellAddress target)
    {
        _selectionMoveDragging = true;
        _selectionMoveSourceRange = source;
        _selectionMoveStartCell = startCell;
        _selectionMovePreviewRange = source;
        ContinueSelectionMoveDragCore(target);
    }

    /// <summary>
    /// Test-only seam that drives the real border-drag-move commit path (source/target range set
    /// directly instead of via pointer capture), so headless tests can assert on the resulting cell
    /// values and on whether the overwrite confirmation was consulted. Not used by production code
    /// paths.
    /// </summary>
    internal Task RaiseSelectionMoveDragForTest(GridRange source, GridRange target, bool ctrlHeld = false)
    {
        _selectionMoveSourceRange = source;
        _selectionMovePreviewRange = target;
        return CommitSelectionMoveDragAsync(ctrlHeld);
    }

    /// <summary>Test-visible read of the shared read-only workbook session.</summary>
    internal bool IsWorkbookReadOnlyForTest => _workbookReadOnlySession.IsReadOnly;

    /// <summary>
    /// Test-only seam for <see cref="ApplyWorkbookReadOnlyOpenPolicy"/> -- mirrors the
    /// <c>RaiseKeyDownForTest</c>/<c>RaiseAutofillHandleDoubleClickForTest</c> convention of driving
    /// the real production method directly instead of a source-string proxy. Not used by production
    /// code paths.
    /// </summary>
    internal WorkbookReadOnlyOpenOutcome ApplyWorkbookReadOnlyOpenPolicyForTest(Workbook workbook) =>
        ApplyWorkbookReadOnlyOpenPolicy(workbook);

    /// <summary>Test-only seam for the shared read-only session -- sets the state directly instead
    /// of driving it through <see cref="ApplyWorkbookReadOnlyOpenPolicy"/>,
    /// so Save-enforcement tests (R83-services-doc-recovery-props-5-1) don't need FileSharing metadata
    /// and a prompt override just to reach the read-only state. Not used by production code paths.
    /// </summary>
    internal void SetWorkbookReadOnlyForTest(bool value) =>
        _workbookReadOnlySession.ApplyPromptDecision(value);

    /// <summary>Test-only seam for <see cref="ResolveExistingSaveTarget"/> -- see its declaration.
    /// Not used by production code paths.</summary>
    internal FileSaveTarget? ResolveExistingSaveTargetForTest() => ResolveExistingSaveTarget();

    /// <summary>
    /// True while a save is still in flight. The write runs off the UI thread, so pumping the
    /// dispatcher alone does not guarantee the file on disk is complete -- a test that reads the saved
    /// file straight after Ctrl+S is racing the writer.
    /// </summary>
    internal bool IsSavingForTest => _isSaving;

    /// <summary>
    /// Test-only seam onto the static reference-coloring decision logic below (which cell/overlay
    /// pair receives which run gets exercised in production via <see cref="RefreshFormulaReferenceHighlights"/>
    /// for both the formula bar and, since R78-render-inplace-editor-5-2, the in-cell editor).
    /// </summary>
    internal static void ApplyFormulaReferenceTextOverlayForTest(
        TextBox editor,
        TextBlock overlay,
        IBrush plainBrush,
        string text,
        IReadOnlyList<FormulaReferenceHighlight> highlights) =>
        ApplyFormulaReferenceTextOverlay(editor, overlay, plainBrush, text, highlights);

    /// <summary>Test-only forwarder for <see cref="FormatHyperlinkTooltip"/>.</summary>
    internal static string? FormatHyperlinkTooltipForTest(Sheet? sheet, CellAddress address) =>
        FormatHyperlinkTooltip(sheet, address);

    /// <summary>Test-only forwarder for <see cref="ResolveOrientedWrapMeasureWidth"/>.</summary>
    internal static double ResolveOrientedWrapMeasureWidthForTest(double cellWidth, double indentPixels, TextWrapping textWrapping) =>
        ResolveOrientedWrapMeasureWidth(cellWidth, indentPixels, textWrapping);

    /// <summary>
    /// Test-only forwarder exposing the private <see cref="CreateOrientedCellContent"/> so
    /// regression tests can inspect the resulting Canvas-left/top of a rotated cell's TextBlock
    /// without spinning up a full MainWindow/viewport.
    /// </summary>
    internal static AvaloniaGrid CreateOrientedCellContentForTest(
        TextBlock textBlock,
        double cellWidth,
        double cellHeight,
        CellHAlign horizontalAlignment,
        CellVAlign? verticalAlignment,
        bool isNumeric,
        double indentPixels,
        int textRotation,
        TextWrapping textWrapping,
        CellStyle? style,
        CellBorderNeighborEdges borderNeighbors = default,
        bool isEffectivelyRightToLeft = false,
        double zoomFactor = 1) =>
        CreateOrientedCellContent(
            textBlock,
            cellWidth,
            cellHeight,
            horizontalAlignment,
            verticalAlignment,
            isNumeric,
            indentPixels,
            textRotation,
            textWrapping,
            style,
            borderNeighbors,
            isEffectivelyRightToLeft,
            zoomFactor);

    /// <summary>Test-only seam: exercises <see cref="SetClipboardMarquee"/> without the real OS
    /// clipboard write CopySelectedRangeToClipboardAsync/CutSelectedRangeToClipboardAsync require
    /// (Avalonia's IClipboard is [NotClientImplementable] in a headless test, matching the existing
    /// R66/R68 clipboard test rationale in this project).</summary>
    internal void SetClipboardMarqueeForTest(GridRange? range, bool isCut = false) =>
        SetClipboardMarquee(range, isCut);

    internal GridRange? ClipboardMarqueeRangeForTest => _clipboardMarqueeRange;

    internal bool ClipboardMarqueeIsCutForTest => _clipboardMarqueeIsCut;

    internal static bool TryResolveWorkbookShortcutRouteForTest(
        Key key,
        KeyModifiers modifiers,
        out WorkbookShortcutRoute route) =>
        TryGetWorkbookShortcutRoute(key, modifiers, out route);

    /// <summary>
    /// Test-only seam that drives the real worksheet-grid key-handling logic (F9/Ctrl+Space/
    /// Ctrl+Arrow/etc.) with a caller-supplied <see cref="KeyEventArgs"/>, awaiting completion so
    /// assertions can run against the resulting <see cref="Session"/> state. Not used by production
    /// code paths (the real <c>KeyDown</c> subscription goes through the <c>async void</c> wrapper
    /// above, as Avalonia event handlers require).
    /// </summary>
    internal Task RaiseKeyDownForTest(KeyEventArgs e) => MainWindow_KeyDownAsync(e);

    /// <summary>
    /// Lets the headless parity-capture coordinator close the window without the dirty-workbook
    /// save prompt. The capture seeds/edits the workbook (so it is dirty by the end); without this
    /// the <see cref="MainWindow_Closing"/> handler would cancel the close and pop a modal that
    /// never gets answered under Xvfb, hanging the capture process. Mirrors the WPF host's
    /// <c>SuppressNextClosePrompt</c>.
    /// </summary>
    internal void AllowCloseWithoutDirtyPromptForParityCapture()
    {
        _allowCloseWithoutDirtyPrompt = true;
        WindowRegistry.Unregister(this);
    }

    /// <summary>
    /// Test-only seam driving the exact same private method the real Quit path invokes
    /// (<c>_quitMenuItem.Click += async (_, _) => await TryQuitApplicationAsync();</c>). Headless
    /// tests cannot raise a native OS menu's <c>Click</c> (it is platform chrome, not an Avalonia
    /// routed event), so -- mirroring this file's established convention for other native-menu-gated
    /// actions like <see cref="OpenWorkbookFromTargetAsyncForTest"/> and
    /// <see cref="SaveWorkbookToTargetAsyncForTest"/> -- this calls straight through to the shared
    /// method body, exercising the real dirty-gate/sibling-propagation/shutdown logic rather than any
    /// test-only shortcut around it.
    /// </summary>
    internal Task TryQuitApplicationAsyncForTest() => TryQuitApplicationAsync();

    /// <summary>Test-only seam for <see cref="_currentFileSourceLastWriteTimeUtc"/> (see its
    /// declaration) -- not used by production code paths.</summary>
    internal DateTime? CurrentFileSourceLastWriteTimeUtcForTest => _currentFileSourceLastWriteTimeUtc;

    /// <summary>Test-only seam driving <see cref="OpenWorkbookFromTargetAsync"/> directly (mirrors
    /// <see cref="ApplyWorkbookReadOnlyOpenPolicyForTest"/>'s convention) -- lets a test open a
    /// REAL file through the real production open path without going through the OS file picker.
    /// Not used by production code paths.</summary>
    internal Task OpenWorkbookFromTargetAsyncForTest(WorkbookOpenTarget target) =>
        OpenWorkbookFromTargetAsync(target);

    /// <summary>Test-only seam driving <see cref="SaveWorkbookToTargetAsync"/> directly. Not used by
    /// production code paths.</summary>
    internal Task<bool> SaveWorkbookToTargetAsyncForTest(FileSaveTarget target) =>
        SaveWorkbookToTargetAsync(target);

    /// <summary>Test-only view of the shared cancellation session's active state.</summary>
    internal bool FileOperationCancellationActiveForTest =>
        _fileOperationCancellationSession.IsActive;

    /// <summary>Test-only seam for the status-bar Cancel button's visibility
    /// (R119-avalonia-file-op-cancel) -- not used by production code paths.</summary>
    internal bool FileOperationCancelButtonVisibleForTest => _fileOperationCancelButton.IsVisible;

    /// <summary>Test-only seam for the status-bar Cancel button's enabled state
    /// (R119-avalonia-file-op-cancel) -- not used by production code paths.</summary>
    internal bool FileOperationCancelButtonEnabledForTest => _fileOperationCancelButton.IsEnabled;

    /// <summary>Test-only seam driving the real <see cref="FileOperationCancelButton_Click"/> handler
    /// directly (R119-avalonia-file-op-cancel), so a test exercises the exact same code path a real
    /// pointer click on the status-bar Cancel button would. Not used by production code paths.</summary>
    internal void RaiseFileOperationCancelButtonClickForTest() =>
        FileOperationCancelButton_Click(_fileOperationCancelButton, new RoutedEventArgs());

    /// <summary>Test-only seam driving the real <see cref="ShowEditIssue"/> production code path.</summary>
    internal void InvokeShowEditIssueForTest(string message) => ShowEditIssue(message);

    /// <summary>
    /// Test-only seam driving the real private <c>SortSelectedRange(bool)</c> handler that ribbon
    /// Sort Ascending/Descending (and the Sort A-Z/Z-A context-menu items) call, so a test exercises
    /// the exact code path a real click drives -- including the app's own
    /// <see cref="SortAdjacentDataPromptResolver"/> wiring -- rather than calling
    /// <c>Session.SortSelectedRange</c> directly. Not used by production code paths.
    /// </summary>
    internal void SortSelectedRangeForTest(bool ascending) => SortSelectedRange(ascending);

    /// <summary>Test-only seam exposing <see cref="_statusText"/> for accessibility assertions.</summary>
    internal TextBlock StatusTextForTest => _statusText;

    /// <summary>Test-only seam for toggling File ▸ Options ▸ Formulas ▸ "R1C1 reference style"
    /// (freex-freeze-headers F1) directly on the live options session, without driving the full
    /// Options dialog UI -- reads/writes the exact same <see cref="_optionsRuntimeSession"/> state
    /// the real <see cref="UseR1C1ReferenceStyle"/> property consults. Not used by production code
    /// paths.</summary>
    internal bool UseR1C1ReferenceStyleForTest
    {
        get => _optionsRuntimeSession.LiveOptions.UseR1C1ReferenceStyle;
        set => _optionsRuntimeSession.LiveOptions.UseR1C1ReferenceStyle = value;
    }

    /// <summary>Test-only seam for the status-bar progress bar's visibility (shared-progress-
    /// reporting F2) -- not used by production code paths.</summary>
    internal bool FileOperationProgressBarVisibleForTest => _fileOperationProgressBar.IsVisible;

    /// <summary>Test-only seam for the status-bar progress bar's indeterminate state
    /// (shared-progress-reporting F2) -- not used by production code paths.</summary>
    internal bool FileOperationProgressBarIsIndeterminateForTest => _fileOperationProgressBar.IsIndeterminate;

    /// <summary>Test-only seam for the status-bar progress bar's current value
    /// (shared-progress-reporting F2) -- not used by production code paths.</summary>
    internal double FileOperationProgressBarValueForTest => _fileOperationProgressBar.Value;

    internal Func<Task<bool>>? ConfirmSelectionMoveOverwriteOverrideForTest;

    internal Func<DataValidationPromptRequest, UserMessageResult>? DataValidationPromptOverrideForTest;

    internal Func<SortAdjacentDataPromptRequest, UserMessageResult>? SortAdjacentDataPromptOverrideForTest;

    internal Func<string, UserMessageResult>? ReadOnlyRecommendedPromptOverrideForTest;

    internal Func<string, string?>? ReservationPasswordPromptOverrideForTest;

    internal Action? ReservationPasswordIncorrectNoticeOverrideForTest;

    internal Func<string, UserMessageResult>? ExternallyModifiedFileOverwriteConfirmOverrideForTest;

    internal Func<string, UserMessageResult>? LossyFormatFeatureLossConfirmOverrideForTest;

    partial void ResolveSelectionMoveOverwriteConfirmationHandler(ref Func<Task<bool>>? handler) =>
        handler = ConfirmSelectionMoveOverwriteOverrideForTest;

    partial void ResolveDataValidationPromptHandler(
        ref Func<DataValidationPromptRequest, UserMessageResult>? handler) =>
        handler = DataValidationPromptOverrideForTest;

    partial void ResolveSortAdjacentDataPromptHandler(
        ref Func<SortAdjacentDataPromptRequest, UserMessageResult>? handler) =>
        handler = SortAdjacentDataPromptOverrideForTest;

    partial void ResolveReadOnlyRecommendedPromptHandler(
        ref Func<string, UserMessageResult>? handler) =>
        handler = ReadOnlyRecommendedPromptOverrideForTest;

    partial void ResolveReservationPasswordPromptHandler(ref Func<string, string?>? handler) =>
        handler = ReservationPasswordPromptOverrideForTest;

    partial void ResolveReservationPasswordIncorrectNoticeHandler(ref Action? handler) =>
        handler = ReservationPasswordIncorrectNoticeOverrideForTest;

    partial void ResolveExternallyModifiedFileOverwriteConfirmHandler(
        ref Func<string, UserMessageResult>? handler) =>
        handler = ExternallyModifiedFileOverwriteConfirmOverrideForTest;

    partial void ResolveLossyFormatFeatureLossConfirmHandler(
        ref Func<string, UserMessageResult>? handler) =>
        handler = LossyFormatFeatureLossConfirmOverrideForTest;

    internal static IReadOnlyList<(TextToColumnsColumnFormat Format, string Label)>
        TextToColumnsFormatChoicesForTest => TextToColumnsFormatChoices;

    internal void InsertSheetRowsForTest() => InsertSheetRows();

    internal void InsertSheetColumnsForTest() => InsertSheetColumns();

    internal void DeleteSheetRowsForTest() => DeleteSheetRows();

    internal void DeleteSheetColumnsForTest() => DeleteSheetColumns();

    internal void DeleteActiveSheetForTest() => DeleteActiveSheet();

    internal void DuplicateActiveSheetForTest() => DuplicateActiveSheet();

    internal void MoveActiveSheetLeftForTest() => MoveActiveSheetLeft();

    internal void MoveActiveSheetRightForTest() => MoveActiveSheetRight();

    internal void UndoLastEditForTest() => UndoLastEdit();

    internal void RedoLastEditForTest() => RedoLastEdit();

    internal void ClearSelectedRangeContentsForTest() => ClearSelectedRangeContents();

    internal void ClearSelectionAndEditForTest() => ClearSelectionAndEdit();

    internal void SetCalculationModeAutomaticForTest() => SetCalculationModeAutomatic();

    internal void SetCalculationModeAutomaticExceptDataTablesForTest() =>
        SetCalculationModeAutomaticExceptDataTables();

    internal void DeleteActiveCellNoteForTest() => DeleteActiveCellNote();

    internal void DeleteActiveCellThreadedCommentForTest() => DeleteActiveCellThreadedComment();

    internal Task MergeAndCenterSelectedRangeForTestAsync() => MergeAndCenterSelectedRangeAsync();

    internal Task MergeSelectedRangeForTestAsync() => MergeSelectedRangeAsync();

    internal Task MergeAcrossSelectedRangeForTestAsync() => MergeAcrossSelectedRangeAsync();

    internal Task ShowInsertCellsDialogForTestAsync() => ShowInsertCellsDialogAsync();

    internal Task ShowDeleteCellsDialogForTestAsync() => ShowDeleteCellsDialogAsync();

    internal Task ShowFormatCellsDialogForTestAsync(int initialTabIndex = 0) =>
        ShowFormatCellsDialogAsync(initialTabIndex);

    internal Task ShowFillSeriesDialogForTestAsync() => ShowFillSeriesDialogAsync();

    internal Task ShowFindDialogForTestAsync() => ShowFindDialogAsync();

    internal Task ShowReplaceDialogForTestAsync() => ShowReplaceDialogAsync();

    internal Task ShowOptionsDialogForTestAsync() => ShowOptionsDialogAsync();

    internal Task ShowHeaderFooterDialogForTestAsync() => ShowHeaderFooterDialogAsync();

    internal Task ShowPageSetupDialogForTestAsync() => ShowPageSetupDialogAsync(default, false);

    internal Task ShowSymbolPickerForTestAsync() => ShowSymbolPickerAsync();

    internal async Task ShowAdvancedFilterInputDialogForTestAsync() =>
        _ = await ShowAdvancedFilterInputDialogAsync();

    internal Task ShowCommentsListForTestAsync() => ShowCommentsListAsync();

    internal Task ShowWatchWindowDialogForTestAsync() => ShowWatchWindowDialogAsync();

    internal Task ShowErrorCheckingParityDialogForTestAsync() => ShowErrorCheckingParityDialogAsync();

    internal Task ShowSelectionPaneParityDialogForTestAsync() => ShowSelectionPaneParityDialogAsync();

    internal Task ShowSpellCheckParityDialogForTestAsync() => ShowSpellCheckParityDialogAsync();

    internal Task ShowTextToColumnsParityDialogForTestAsync() => ShowTextToColumnsParityDialogAsync();

    internal Task ShowDataValidationInputDialogForTestAsync() => ShowDataValidationInputDialogAsync();

    internal Task ShowFindReplaceTabbedDialogForTestAsync(bool replaceMode = false) =>
        ShowFindReplaceTabbedDialogAsync(replaceMode);

    internal async Task ShowFormatCellsInputDialogForTestAsync(int initialTabIndex = 0) =>
        _ = await ShowFormatCellsInputDialogAsync(initialTabIndex);

    internal static void ConfigureDialogCancelOnEscapeForTest(Window dialog, Button cancelButton) =>
        ConfigureDialogCancelOnEscape(dialog, cancelButton);

    internal static void ConfigureDialogTabCycleForTest(Window dialog, Control root) =>
        ConfigureDialogTabCycle(dialog, root);

    internal static void ConfigureChartDialogKeyboardLifecycleForTest(Window dialog, Control initialFocus) =>
        ConfigureChartDialogKeyboardLifecycle(dialog, initialFocus);

    internal static void ConfigureLegalNoticesDialogKeyboardForTest(
        Window dialog,
        TabControl tabControl,
        Button closeButton) =>
        ConfigureLegalNoticesDialogKeyboard(dialog, tabControl, closeButton);

    internal static void ConfigurePivotDialogLifecycleForTest(
        Window dialog,
        Control initialFocus,
        bool selectAllText = false) =>
        ConfigurePivotDialogLifecycle(dialog, initialFocus, selectAllText);

    internal static Task SettleDialogRangeInteractionBoundaryForTestAsync(Window dialog) =>
        SettleDialogRangeInteractionBoundaryAsync(dialog);

    internal void RaiseDialogRangeValidationKeyForTest(Key key) =>
        RaiseDialogRangeValidationKey(key);

    internal static int CountDialogTabStopsForTest(Window dialog) => CountDialogTabStops(dialog);

    internal static Task<string> ExerciseTabCycleForTestAsync(
        Window dialog,
        bool reverse,
        int tabStops) =>
        ExerciseTabCycleAsync(dialog, reverse, tabStops);

}
