using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Pivot header dropdown visuals — match WPF GridView.Rendering.AutoFilter.cs constants.
    // Background and border mirror WPF's PivotHeaderDropdownButtonBrush (228,233,240) / BorderPen (132,141,154).
    private static readonly IBrush PivotDropdownButtonBg = new ImmutableSolidColorBrush(Color.FromRgb(228, 233, 240));
    private static readonly IBrush PivotDropdownButtonBorder = new ImmutableSolidColorBrush(Color.FromRgb(132, 141, 154));
    private static readonly IBrush PivotDropdownGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(45, 55, 65));
    private static readonly IBrush PivotActiveDropdownGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(15, 109, 140));

    // Padding reserved in the cell text for expand/collapse buttons (matches WPF PivotExpandCollapseButtonReserve).
    internal const double PivotExpandCollapseButtonSize = 8;
    internal const double PivotExpandCollapseButtonReserve = PivotExpandCollapseButtonSize + 6;

    /// <summary>
    /// Rebuilds <see cref="_pivotHeaderDropdownTargets"/> and <see cref="_pivotRowLabelAdornments"/>
    /// for the current sheet. Called once at the start of <see cref="BuildSheetGrid"/> so every cell
    /// decorated in the same pass uses a consistent snapshot.
    /// </summary>
    private void BuildPivotAdornmentLookups(Workbook workbook, Sheet sheet)
    {
        var headerTargets = PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet);
        _pivotHeaderDropdownTargets = new Dictionary<(uint, uint), PivotHeaderDropdownTargetModel>(headerTargets.Count);
        foreach (var target in headerTargets)
            _pivotHeaderDropdownTargets[(target.HeaderCell.Row, target.HeaderCell.Col)] = target.MenuTarget;

        _pivotRowLabelAdornments = PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet);
    }

    /// <summary>
    /// Wraps a pivot header cell's content with a dropdown button when the cell is identified as a
    /// pivot header dropdown target. Mirrors <see cref="DecorateAutoFilterHeaderCell"/> but for pivot
    /// field-header cells (Row Labels, Column Labels, Page field dropdowns).
    /// </summary>
    private Border DecoratePivotHeaderCell(Border cellBorder, CellAddress address)
    {
        if (!_pivotHeaderDropdownTargets.TryGetValue((address.Row, address.Col), out var menuTarget))
            return cellBorder;

        var isActive = menuTarget.IsActive;

        var content = cellBorder.Child;
        cellBorder.Child = null;

        // Downward-triangle glyph — same geometry as the AutoFilter chevron.
        // Active: funnel icon (blue); inactive: simple filled triangle (dark grey).
        var chevronPath = isActive
            ? new AvaloniaPath
            {
                Data = Geometry.Parse("M3,2 L12,2 L8.5,6 L8.5,12 L6.5,12 L6.5,6 Z"),
                Fill = PivotActiveDropdownGlyphBrush,
                Stretch = Stretch.None,
            }
            : new AvaloniaPath
            {
                Data = Geometry.Parse("M4.5,6.5 L10.5,6.5 L7.5,10.5 Z"),
                Fill = PivotDropdownGlyphBrush,
                Stretch = Stretch.None,
            };

        // WPF uses a solid light-blue-grey background (PivotHeaderDropdownButtonBrush = RGB 228,233,240).
        // Use a styled Border+PointerPressed rather than Button so the chevron renders in headless
        // captures (Avalonia Button needs its ContentPresenter template to display its Content in
        // headless-platform mode; the autofilter Button works because the live-app theme is loaded,
        // but in --parity-grid headless captures only the intrinsic layout/render pipeline runs).
        var buttonBorder = new Border
        {
            Width = 15,
            MinWidth = 15,
            Background = PivotDropdownButtonBg,
            BorderBrush = PivotDropdownButtonBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Child = chevronPath,
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(buttonBorder, $"PivotDropdown_{address.Row}_{address.Col}");
        AutomationProperties.SetName(buttonBorder, UiText.Get("PivotLoc_FieldDropdownAutomationName"));

        // Capture menuTarget for the click lambda (avoids repeated dictionary lookup).
        var capturedTarget = menuTarget;
        buttonBorder.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(buttonBorder);
            if (point.Properties.IsLeftButtonPressed)
            {
                OpenPivotHeaderDropdownMenuFromCell(buttonBorder, capturedTarget);
                args.Handled = true;
            }
        };
        buttonBorder.Focusable = true;
        AttachPivotChartHeaderContextMenu(buttonBorder, capturedTarget);

        var grid = new AvaloniaGrid { ClipToBounds = true };
        if (content is Control existing)
            grid.Children.Add(existing);
        grid.Children.Add(buttonBorder);
        cellBorder.Child = grid;
        return cellBorder;
    }

    /// <summary>
    /// Returns the extra leading text padding (in logical pixels) that should be reserved for a
    /// pivot row-label cell with an expand/collapse adornment. Returns 0 for non-adornment cells.
    /// </summary>
    private double GetPivotRowLabelTextPadding(uint row, uint col)
    {
        foreach (var adornment in _pivotRowLabelAdornments)
        {
            if (adornment.Cell.Row == row && adornment.Cell.Col == col && adornment.ReserveTextPadding)
                return PivotExpandCollapseButtonReserve;
        }
        return 0;
    }

    /// <summary>
    /// Opens the pivot field header dropdown menu when the inline dropdown button is clicked.
    /// Reuses <see cref="ShowPivotHeaderDropdownFromTarget"/> which renders the same context menu
    /// as the pivot pane's header area dropdowns.
    /// </summary>
    private void OpenPivotHeaderDropdownMenuFromCell(Control anchor, PivotHeaderDropdownTargetModel target)
    {
        // Resolve the pivot table by name so the menu builder can inspect its current state.
        var sheet = _session.ActiveSheet;
        PivotTableModel? pivot = null;
        foreach (var pt in sheet.PivotTables)
        {
            if (string.Equals(pt.Name, target.PivotTableName, StringComparison.OrdinalIgnoreCase))
            {
                pivot = pt;
                break;
            }
        }
        if (pivot is null)
            return;

        ShowPivotHeaderDropdownFromTarget(pivot, target, anchor);
    }
}
