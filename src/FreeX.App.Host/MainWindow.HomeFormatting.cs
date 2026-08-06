using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private enum MergeCellsWarningChoice
    {
        Cancel,
        KeepFirstCell,
        ConcatenateAllCells
    }

    private enum RibbonBorderPreset
    {
        All,
        Outside,
        Inside,
        None,
        Bottom,
        Top,
        Left,
        Right,
        ThickBottom,
        BottomDouble,
        ThickBox,
        TopAndBottom,
        TopAndThickBottom,
        TopAndDoubleBottom
    }

    // ── Formatting toolbar handlers ───────────────────────────────────────────

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Bold: IsRibbonCommandChecked("Bold")));
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Italic: IsRibbonCommandChecked("Italic")));
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = IsRibbonCommandChecked("Underline");
        SetToolbarToggleStates(strike: enabled ? false : null);
        ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled));
    }

    private void UnderlineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetToolbarToggleStates(underline: true, strike: false);
        ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(true));
    }

    private void StrikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = IsRibbonCommandChecked("Strikethrough");
        SetToolbarToggleStates(underline: enabled ? false : null);
        ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled));
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyHorizontalAlignment(CellHAlign.Left);
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyHorizontalAlignment(CellHAlign.Center);
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyHorizontalAlignment(CellHAlign.Right);
    }

    private void ApplyHorizontalAlignment(CellHAlign alignment)
    {
        switch (alignment)
        {
            case CellHAlign.Left:
                SetToolbarToggleStates(center: false, right: false);
                ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left));
                break;

            case CellHAlign.Center:
                SetToolbarToggleStates(left: false, right: false);
                ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
                break;

            case CellHAlign.Right:
                SetToolbarToggleStates(left: false, center: false);
                ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right));
                break;
        }
    }

    private void SetToolbarToggleStates(
        bool? underline = null,
        bool? strike = null,
        bool? left = null,
        bool? center = null,
        bool? right = null,
        bool? top = null,
        bool? middle = null,
        bool? bottom = null)
    {
        _suppressToolbarSync = true;
        try
        {
            if (underline.HasValue) _ribbonState.SetChecked("Underline", underline.Value);
            if (strike.HasValue) _ribbonState.SetChecked("Strikethrough", strike.Value);
            if (left.HasValue) _ribbonState.SetChecked("Align Left", left.Value);
            if (center.HasValue) _ribbonState.SetChecked("Center", center.Value);
            if (right.HasValue) _ribbonState.SetChecked("Align Right", right.Value);
            if (top.HasValue) _ribbonState.SetChecked("Top Align", top.Value);
            if (middle.HasValue) _ribbonState.SetChecked("Middle Align", middle.Value);
            if (bottom.HasValue) _ribbonState.SetChecked("Bottom Align", bottom.Value);
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private void WrapTextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        // Routed through ApplyStyleDiffWithWrapGrowth (not the generic ApplyStyleDiff) so that
        // enabling Wrap Text auto-grows an auto-height row to fit, matching Excel and the Avalonia
        // shell's WorkbookSession.SetSelectedRangeWrapText (see MainWindow.CellsCommands.cs).
        ApplyStyleDiffWithWrapGrowth(new StyleDiff(WrapText: IsRibbonCommandChecked("Wrap Text")));
    }

    private void MergeCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryResolveMergeContentResolution(range, out var contentResolution)) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Merge & Center",
                range,
                currentRange => CreateMergeAndCenterCommand(currentRange, contentResolution),
                out _))
            return;

        UpdateViewport();
    }

    private void MergeCenterMenuItem_Click(object sender, RoutedEventArgs e) => MergeCenterBtn_Click(sender, e);

    private void MergeCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryResolveMergeContentResolution(range, out var contentResolution)) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Merge Cells",
                sheetId => CreateMergeCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    contentResolution)))
            return;

        UpdateViewport();
    }

    private void MergeAcrossMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        // A single-column selection would build one per-row range with ColCount==1 (CellCount<=1),
        // which CellMergePlanner.CreateMergeCommands treats as a no-op merge. Reject up front, matching
        // the Avalonia shell's MergeAcrossSelectedRangeAsync (MainWindow.MergePaste.cs), instead of
        // silently dirtying the workbook and pushing a phantom undo entry for a composite of no-ops.
        if (range.ColCount <= 1) return;
        if (!TryResolveMergeContentResolution(range, out var contentResolution, perRow: true)) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Merge Across",
                sheetId =>
                {
                    var currentRange = GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    var commands = new List<IWorkbookCommand>();
                    for (var row = currentRange.Start.Row; row <= currentRange.End.Row; row++)
                    {
                        commands.Add(CreateMergeCellsCommand(
                            sheetId,
                            new GridRange(
                                new CellAddress(sheetId, row, currentRange.Start.Col),
                                new CellAddress(sheetId, row, currentRange.End.Col)),
                            contentResolution,
                            allowUnmergeToggle: false));
                    }

                    return commands.Count == 1
                        ? commands[0]
                        : new CompositeWorkbookCommand("Merge Across", commands);
                }))
            return;

        UpdateViewport();
    }

    private void UnmergeCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Unmerge Cells",
                sheetId => CreateUnmergeCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId))))
            return;

        UpdateViewport();
    }

    // The selection is NOT auto-expanded to cover whole merges before this runs (SelectedRange may be a
    // single cell inside a larger merge, or a block spanning several merges), so an exact-range
    // UnmergeCellsCommand(range) would need SelectedRange to equal a stored merged region verbatim and
    // would silently no-op otherwise. Mirror the Avalonia shell / Format-Cells dialog path
    // (CellMergePlanner.CreateUnmergeCommands / WorkbookSession.UnmergeSelectedRange): unmerge every
    // merged region that OVERLAPS the selection, one UnmergeCellsCommand per region.
    private IWorkbookCommand CreateUnmergeCellsCommand(SheetId sheetId, GridRange range)
    {
        if (_workbook.GetSheet(sheetId) is not { } sheet)
            return new UnmergeCellsCommand(sheetId, range);

        var commands = CellMergePlanner.CreateUnmergeCommands(sheet, sheetId, range);
        return commands.Count switch
        {
            0 => NoOpWorkbookCommand.Instance,
            1 => commands[0],
            _ => new CompositeWorkbookCommand("Unmerge Cells", commands)
        };
    }

    private IWorkbookCommand CreateMergeAndCenterCommand(
        GridRange range,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * 3);
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = _workbook.GetSheet(sheetId);
            var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
            commands.AddRange(CellMergePlanner.CreateMergeAndCenterCommands(
                sheet,
                sheetId,
                sheetRange,
                contentResolution));
        }

        return new CompositeWorkbookCommand("Merge & Center", commands);
    }

    private IWorkbookCommand CreateMergeCellsCommand(
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell,
        bool allowUnmergeToggle = true)
    {
        if (_workbook.GetSheet(sheetId) is not { } sheet)
            return new MergeCellsCommand(sheetId, range);

        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet,
            sheetId,
            range,
            mergeCells: true,
            contentResolution,
            allowUnmergeToggle);

        return commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Merge Cells", commands);
    }

    private bool TryResolveMergeContentResolution(
        GridRange range,
        out MergeCellContentResolution contentResolution,
        bool perRow = false)
    {
        contentResolution = MergeCellContentResolution.KeepFirstCell;
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return true;

        var contentPlan = perRow
            ? CellMergePlanner.AnalyzeContent(sheet, range, perRow: true)
            : CellMergePlanner.AnalyzeContent(sheet, range);
        if (!contentPlan.WouldLoseContent)
            return true;

        var choice = ShowMergeCellsContentWarningDialog(contentPlan);
        if (choice == MergeCellsWarningChoice.Cancel)
            return false;

        contentResolution = choice == MergeCellsWarningChoice.ConcatenateAllCells
            ? MergeCellContentResolution.ConcatenateAllCells
            : MergeCellContentResolution.KeepFirstCell;
        return true;
    }

    private MergeCellsWarningChoice ShowMergeCellsContentWarningDialog(MergeCellContentPlan contentPlan)
    {
        var choice = MergeCellsWarningChoice.Cancel;
        var dialog = new Window
        {
            Title = "Merge Cells",
            Width = 460,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this
        };
        AutomationProperties.SetAutomationId(dialog, "MergeCellsContentWarningDialog");

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Orientation = Orientation.Vertical
        };

        root.Children.Add(new TextBlock
        {
            Text = "Merging cells can discard cell contents.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Choose how to handle the selected cell contents.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var preview = string.Join(", ", contentPlan.Entries
            .Select(entry => entry.DisplayText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(4));
        if (!string.IsNullOrWhiteSpace(preview))
        {
            root.Children.Add(new TextBlock
            {
                Text = preview,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var keepFirstButton = new Button
        {
            Content = "Keep only first cell",
            MinWidth = 136,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        AutomationProperties.SetAutomationId(keepFirstButton, "MergeCellsKeepFirstButton");
        keepFirstButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.KeepFirstCell;
            dialog.DialogResult = true;
        };

        var concatenateButton = new Button
        {
            Content = "Concatenate all cells",
            MinWidth = 136,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(concatenateButton, "MergeCellsConcatenateButton");
        concatenateButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.ConcatenateAllCells;
            dialog.DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 82,
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(cancelButton, "MergeCellsCancelButton");
        cancelButton.Click += (_, _) =>
        {
            choice = MergeCellsWarningChoice.Cancel;
            dialog.DialogResult = false;
        };

        buttonRow.Children.Add(keepFirstButton);
        buttonRow.Children.Add(concatenateButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.ShowDialog();
        return choice;
    }

    private void FontNameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if ((sender as ComboBox)?.SelectedItem is string name)
            ApplyStyleDiff(new StyleDiff(FontName: name));
    }

    private void FontNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_suppressToolbarSync) return;

        CommitFontNameBoxText(sender as ComboBox);
        e.Handled = true;
    }

    private void FontNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitFontNameBoxText(sender as ComboBox);
    }

    private void CommitFontNameBoxText(ComboBox? combo)
    {
        var name = combo?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            ApplyStyleDiff(new StyleDiff(FontName: name));
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitFontSizeBoxText(sender as ComboBox, preferSelectedItem: true);
    }

    private void FontSizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_suppressToolbarSync) return;

        CommitFontSizeBoxText(sender as ComboBox);
        e.Handled = true;
    }

    private void FontSizeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        CommitFontSizeBoxText(sender as ComboBox);
    }

    private void CommitFontSizeBoxText(ComboBox? combo, bool preferSelectedItem = false)
    {
        var text = preferSelectedItem ? GetSelectedFontSizeText(combo) : combo?.Text;
        if (text is not null && WorksheetSizeInputParser.TryParsePositiveSize(text, out var size))
            ApplyFontSizeAndFitRows(size);
    }

    private static string? GetSelectedFontSizeText(ComboBox? combo) =>
        combo?.SelectedItem as string ?? combo?.Text;

    private void FontColorBtn_Click(object sender, RoutedEventArgs e) => ApplySelectedFontColor();

    private void FontColorPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!TryShowColorPicker("Font Color", _selectedFontColor, allowNoColor: false, out var color) ||
            color is not { } selected)
        {
            return;
        }

        _selectedFontColor = selected;
        UpdateFontColorButtonSwatch();
        ApplySelectedFontColor();
    }

    private void ApplySelectedFontColor()
    {
        ApplyStyleDiff(new StyleDiff(FontColor: _selectedFontColor));
    }

    private void FillColorBtn_Click(object sender, RoutedEventArgs e) => ApplySelectedFillColor();

    private void FillColorPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!TryShowColorPicker("Fill Color", _selectedFillColor, allowNoColor: true, out var color))
            return;

        _selectedFillColor = color;
        UpdateFillColorButtonSwatch();
        ApplySelectedFillColor();
    }

    private void ApplySelectedFillColor()
    {
        ApplyStyleDiff(_selectedFillColor is { } selected
            ? new StyleDiff(FillColor: selected)
            : new StyleDiff(FillColor: null, ClearFill: true));
    }

    private void UpdateFontColorButtonSwatch()
    {
        // The declarative ribbon renders the Font Color command as an icon without the old swatch bar.
    }

    private void UpdateFillColorButtonSwatch()
    {
        // The declarative ribbon renders the Fill Color command as an icon without the old swatch bar.
    }

    private static SolidColorBrush CreateCellColorBrush(CellColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private bool TryShowColorPicker(
        string title,
        CellColor? initialColor,
        bool allowNoColor,
        out CellColor? color,
        string? noColorButtonText = null)
    {
        var dialog = new ColorPickerDialog(initialColor, allowNoColor, noColorButtonText)
        {
            Owner = this,
            Title = title
        };

        if (dialog.ShowDialog() == true)
        {
            color = dialog.SelectedColor;
            return true;
        }

        color = null;
        return false;
    }

    private void NumberFormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (sender is not ComboBox combo) return;
        var selectedIndex = combo.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= HomeNumberFormatDropdownPlanner.Options.Count) return;

        var option = HomeNumberFormatDropdownPlanner.Options[selectedIndex];
        if (option.OpensFormatCellsDialog)
        {
            ResetNumberFormatBoxSelection(combo);
            OpenFormatCellsDialog(FormatCellsDialogTab.Number);
            return;
        }

        if (option.Code is { } code)
            ApplyStyleDiff(new StyleDiff(NumberFormat: code));
    }

    private void ResetNumberFormatBoxSelection(ComboBox combo)
    {
        _suppressToolbarSync = true;
        try
        {
            combo.SelectedIndex = HomeNumberFormatDropdownPlanner.DefaultSelectionIndex;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    // ── Font group additions ─────────────────────────────────────────────────

    private void DoubleUnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var isOn = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        if (isOn)
            SetToolbarToggleStates(underline: false, strike: false);
        ApplyStyleDiff(CellStyleDiffPlanner.DoubleUnderlineDiff(isOn));
    }

    private void DoubleUnderlineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetToolbarToggleStates(underline: false, strike: false);
        ApplyStyleDiff(CellStyleDiffPlanner.DoubleUnderlineDiff(true));
    }

    private void IncreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyFontSizeAndFitRows(FontSizePlanner.Increase(style.FontSize));
    }

    private void DecreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyFontSizeAndFitRows(FontSizePlanner.Decrease(style.FontSize));
    }

    private void ApplyFontSizeAndFitRows(double fontSize)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyStyleDiff(new StyleDiff(FontSize: fontSize));

        var newHeight = Math.Min(AutoFitSizingService.MaximumRowHeight, FontSizePlanner.EstimateFittingRowHeight(fontSize));
        var ranges = GetCurrentSelectionRanges(range);
        var command = SelectionStyleCommandPlanner.CreateRangeCommand(
            CurrentGroupedEditSheetIds(),
            ranges,
            (sheetId, currentRange) => new SetRowHeightCommand(
                sheetId,
                currentRange.Start.Row,
                currentRange.End.Row,
                newHeight),
            "Auto Fit Row Height");
        if (!TryExecuteCommand(command, "Auto Fit Row Height"))
            return;

        UpdateViewport();
        RefreshToolbar();
    }

    // ── Border picker ────────────────────────────────────────────────────────

    private void BorderPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedBorderPreset();
    }

    private void ApplyRangeBorderPreset(Func<GridRange, CellAddress, StyleDiff> createDiff, string title)
    {
        var ranges = GetCurrentSelectionRanges();
        if (ranges.Count == 0) return;

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var command = SelectionStyleCommandPlanner.CreatePerCellStyleCommand(
            targetSheetIds,
            ranges,
            createDiff,
            title,
            _workbook);

        if (!TryExecuteCommand(command, title))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void ApplySelectedBorderPreset()
    {
        switch (_selectedBorderPreset)
        {
            case RibbonBorderPreset.All:
                ApplyStyleDiff(BorderShortcutService.GetAllBorderDiff(_borderPickerStyle, _borderPickerColor));
                break;

            case RibbonBorderPreset.Outside:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetOutlineBorderDiff(range, address, _borderPickerStyle, _borderPickerColor),
                    "Outside Borders");
                break;

            case RibbonBorderPreset.Inside:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetInsideBorderDiff(range, address, _borderPickerStyle, _borderPickerColor),
                    "Inside Borders");
                break;

            case RibbonBorderPreset.None:
                ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
                break;

            case RibbonBorderPreset.Bottom:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, _borderPickerStyle, _borderPickerColor));
                break;

            case RibbonBorderPreset.Top:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Top, _borderPickerStyle, _borderPickerColor));
                break;

            case RibbonBorderPreset.Left:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Left, _borderPickerStyle, _borderPickerColor));
                break;

            case RibbonBorderPreset.Right:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Right, _borderPickerStyle, _borderPickerColor));
                break;

            case RibbonBorderPreset.ThickBottom:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Thick, _borderPickerColor));
                break;

            case RibbonBorderPreset.BottomDouble:
                ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Double, _borderPickerColor));
                break;

            case RibbonBorderPreset.ThickBox:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetOutlineBorderDiff(range, address, BorderStyle.Thick, _borderPickerColor),
                    "Thick Outside Borders");
                break;

            case RibbonBorderPreset.TopAndBottom:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, _borderPickerStyle, _borderPickerColor),
                    "Top and Bottom Border");
                break;

            case RibbonBorderPreset.TopAndThickBottom:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, BorderStyle.Thick, _borderPickerColor),
                    "Top and Thick Bottom Border");
                break;

            case RibbonBorderPreset.TopAndDoubleBottom:
                ApplyRangeBorderPreset(
                    (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, BorderStyle.Double, _borderPickerColor),
                    "Top and Double Bottom Border");
                break;
        }
    }

    private void ApplyBorderPreset(RibbonBorderPreset preset)
    {
        _selectedBorderPreset = preset;
        ApplySelectedBorderPreset();
    }

    private void BorderAllMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.All);

    private void BorderOutsideMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Outside);

    private void BorderInsideMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Inside);

    private void BorderNoneMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.None);

    private void BorderBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Bottom);

    private void BorderTopMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Top);

    private void BorderLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Left);

    private void BorderRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.Right);

    private void BorderThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.ThickBottom);

    private void BorderBottomDoubleMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.BottomDouble);

    private void BorderThickBoxMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.ThickBox);

    private void BorderTopAndBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.TopAndBottom);

    private void BorderTopAndThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.TopAndThickBottom);

    private void BorderTopAndDoubleBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyBorderPreset(RibbonBorderPreset.TopAndDoubleBottom);

    private void BorderDrawMenuItem_Click(object sender, RoutedEventArgs e)
        => BeginBorderDrawMode(BorderDrawMode.Draw);

    private void BorderDrawGridMenuItem_Click(object sender, RoutedEventArgs e)
        => BeginBorderDrawMode(BorderDrawMode.DrawGrid);

    private void BorderEraseMenuItem_Click(object sender, RoutedEventArgs e)
        => BeginBorderDrawMode(BorderDrawMode.Erase);

    private void BeginBorderDrawMode(BorderDrawMode mode)
    {
        _borderDrawMode = mode;
        CancelFormatPainter();
        FocusSheetGridIfNeeded();
    }

    private void ApplyBorderDrawMode(GridRange range)
    {
        if (_borderDrawMode == BorderDrawMode.None)
            return;

        var mode = _borderDrawMode;
        var style = _borderPickerStyle;
        var color = _borderPickerColor;
        _borderDrawMode = BorderDrawMode.None;
        SheetGrid.SelectedRange = range;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                BorderDrawPlanner.CommandTitle(mode),
                sheetId => BorderDrawPlanner.CreateCommand(
                    sheetId,
                    SheetGrid.SelectedRange ?? range,
                    mode,
                    style,
                    color)))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void BorderLineColorBlackMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerColor = CellColor.Black;

    private void BorderLineColorGrayMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerColor = new CellColor(128, 128, 128);

    private void BorderLineColorAccent1MenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerColor = _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1);

    private void BorderLineColorAccent2MenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerColor = _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent2);

    private void BorderLineStyleThinMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Thin;

    private void BorderLineStyleMediumMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Medium;

    private void BorderLineStyleThickMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Thick;

    private void BorderLineStyleDashedMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Dashed;

    private void BorderLineStyleDottedMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Dotted;

    private void BorderLineStyleDoubleMenuItem_Click(object sender, RoutedEventArgs e)
        => _borderPickerStyle = BorderStyle.Double;

    private void BorderMoreMenuItem_Click(object sender, RoutedEventArgs e)
        => OpenFormatCellsDialog(FormatCellsDialogTab.Border);

    // ── Alignment group additions ────────────────────────────────────────────

    private void AlignTopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyVerticalAlignment(CellVAlign.Top);
    }

    private void AlignMiddleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyVerticalAlignment(CellVAlign.Center);
    }

    private void AlignBottomBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyVerticalAlignment(CellVAlign.Bottom);
    }

    private void ApplyVerticalAlignment(CellVAlign alignment)
    {
        switch (alignment)
        {
            case CellVAlign.Top:
                SetToolbarToggleStates(top: true, middle: false, bottom: false);
                ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top));
                break;

            case CellVAlign.Center:
                SetToolbarToggleStates(top: false, middle: true, bottom: false);
                ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center));
                break;

            case CellVAlign.Bottom:
                SetToolbarToggleStates(top: false, middle: false, bottom: true);
                ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom));
                break;
        }
    }

    private void IndentIncBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)));
    }
    private void IndentDecBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)));
    }

    private void OrientationPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void OrientHorizMenuItem_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(TextRotation: 0));
    private void OrientAngleCCWMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: 45));
    private void OrientAngleCWMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: -45));
    private void OrientVertMenuItem_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(TextRotation: 255));
    private void OrientRotateUpMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateDownMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: -90));

    // ── Number group additions ───────────────────────────────────────────────

    private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode));
    private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: "0%"));
    private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.CommaStyleNumberFormatCode));

    private void AccountingSymbolMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var symbol = (sender as MenuItem)?.Tag?.ToString();
        var option = HomeNumberFormatDropdownPlanner.AccountingSymbolOptions.FirstOrDefault(candidate =>
            string.Equals(candidate.CommandId, symbol, StringComparison.Ordinal) ||
            string.Equals(candidate.Label, symbol, StringComparison.Ordinal));
        symbol = option?.Symbol ?? symbol;
        if (string.IsNullOrEmpty(symbol))
            symbol = "$";

        ApplyStyleDiff(new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.ResolveAccountingNumberFormatCode(symbol)));
    }

    private void MoreAccountingFormatsMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenFormatCellsDialog(FormatCellsDialogTab.Number);

    private void IncDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatDecimalAdjuster.AddDecimalPlace(style.NumberFormat)));
    }
    private void DecDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatDecimalAdjuster.RemoveDecimalPlace(style.NumberFormat)));
    }

    // ── Styles group ─────────────────────────────────────────────────────────

    private void CfPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void CfGtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Greater Than");
    private void CfLtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Less Than");
    private void CfBetweenMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Between");
    private void CfEqMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Equal To");
    private void CfTextMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Text Contains");
    private void CfDateMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Date Occurring");
    private void CfDuplicateMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Duplicate Values");
    private void CfTop10MenuItem_Click(object sender, RoutedEventArgs e)    => ShowCfDialog("Top 10 Items");
    private void CfTop10PercentMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Top 10%");
    private void CfBottom10MenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Bottom 10 Items");
    private void CfBottom10PercentMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Bottom 10%");
    private void CfAboveAvgMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Above Average");
    private void CfBelowAvgMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Below Average");
    private void CfDataBarsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
            PopulateConditionalFormatDataBarGallery(menuItem);
    }

    private void CfColorScalesMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
            PopulateConditionalFormatColorScaleGallery(menuItem);
    }

    private void CfDataBarMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Data Bar");
    private void CfColorScaleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Color Scale");
    private void CfDataBarPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string style })
            return;

        ApplyDataBarPreset(style);
    }

    private void CfColorScalePresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string style })
            return;

        ApplyColorScalePreset(style);
    }

    private void CfIconSetMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Icon Set");
    private void CfIconSetPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string style })
            return;

        style = ConditionalFormatPresetFactory.IconSetStyleForMenuId(style) ?? style;
        ApplyIconSetPreset(style);
    }
    private void CfNewRuleMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("New Rule");
    private void CfNewFormulaRuleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Formula");
    private void CfClearRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        IWorkbookCommand CreateCommand() =>
            ConditionalFormatCommandPlanner.PlanClear(
                CurrentGroupedEditSheetIds(),
                GetCurrentSelectionRanges(range)).Command;

        if (!TryExecuteRepeatableCommand(
                CreateCommand,
                ConditionalFormatCommandPlanner.ClearRulesCommandLabel,
                out _))
            return;
        ApplyConditionalFormatRefresh(ConditionalFormatStateRefreshPolicy.WorksheetVisualState);
    }
    private void CfManageRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        ManageConditionalFormatsDialog? dlg = null;
        dlg = new ManageConditionalFormatsDialog(
            sheet,
            SheetGrid.SelectedRange,
            requestAppliesToRangeSelection: request => ApplyConditionalFormatAppliesToRangeSelection(dlg, request),
            applyRules: ApplyManagedConditionalFormatRules) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null) return;
        ApplyManagedConditionalFormatRules(dlg.ResultRules);
    }

    private void ApplyConditionalFormatAppliesToRangeSelection(
        ManageConditionalFormatsDialog? dialog,
        ConditionalFormatAppliesToRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyAppliesToRangeSelection(request.RuleId, selectedRange));
    }

    private void ApplyManagedConditionalFormatRules(IReadOnlyList<ConditionalFormat> newRules)
    {
        var plan = ConditionalFormatCommandPlanner.PlanReplaceAll(
            CurrentGroupedEditSheetIds(),
            _currentSheetId,
            newRules);
        if (!TryExecuteCommand(plan.Command, plan.CommandLabel))
            return;
        ApplyConditionalFormatRefresh(plan.RefreshPolicy);
    }

    private void ShowCfDialog(string ruleType)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = ConditionalFormatDialogFactory.Create(ruleType, range);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || dlg.ResultRule is null) return;
        ApplyConditionalFormatPreset(dlg.ResultRule);
    }

    private void ApplyDataBarPreset(string style)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var rule = ConditionalFormatPresetGalleryPlanner.CreateDataBarRule(style, range);
        if (rule is null)
            return;

        ApplyConditionalFormatPreset(rule);
    }

    private void ApplyColorScalePreset(string style)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var rule = ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule(style, range);
        if (rule is null)
            return;

        ApplyConditionalFormatPreset(rule);
    }

    private void ApplyIconSetPreset(string style)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var rule = ConditionalFormatIconSetCatalog.CreateRule(style, range);
        if (rule is null)
            return;

        ApplyConditionalFormatPreset(rule);
    }

    private void ApplyConditionalFormatPreset(ConditionalFormat rule)
    {
        var ranges = GetCurrentSelectionRanges(rule.AppliesTo);
        if (ranges.Count == 0)
            return;

        var plan = ConditionalFormatCommandPlanner.PlanApplyRule(
            CurrentGroupedEditSheetIds(),
            ranges,
            rule);
        if (!TryExecuteCommand(
                plan.Command,
                plan.CommandLabel,
                out _))
            return;

        ApplyConditionalFormatRefresh(plan.RefreshPolicy);
    }

    private void ApplyConditionalFormatRefresh(ConditionalFormatStateRefreshPolicy policy)
    {
        if (policy == ConditionalFormatStateRefreshPolicy.WorksheetVisualState)
            UpdateViewport();
    }

    private void PopulateConditionalFormatDataBarGallery(MenuItem menuItem)
    {
        if (menuItem.Items.Count > 0)
            return;

        foreach (var group in ConditionalFormatPresetGalleryPlanner.DataBarGroups)
        {
            AddConditionalFormatGalleryHeader(menuItem, UiText.Get(group.CategoryKey));
            foreach (var option in group.Options)
            {
                var label = UiText.Get(option.LabelKey);
                var item = CreateConditionalFormatPresetMenuItem(
                    label,
                    option.Style,
                    option.KeyTip,
                    CreateDataBarPresetSwatch(option.Color, option.Gradient));
                item.Click += CfDataBarPresetMenuItem_Click;
                menuItem.Items.Add(item);
            }
        }

        AddConditionalFormatMoreRulesItem(menuItem, "DM", CfDataBarMenuItem_Click);
    }

    private void PopulateConditionalFormatColorScaleGallery(MenuItem menuItem)
    {
        if (menuItem.Items.Count > 0)
            return;

        foreach (var group in ConditionalFormatPresetGalleryPlanner.ColorScaleGroups)
        {
            AddConditionalFormatGalleryHeader(menuItem, UiText.Get(group.CategoryKey));
            foreach (var option in group.Options)
            {
                var label = UiText.Get(option.LabelKey);
                var item = CreateConditionalFormatPresetMenuItem(
                    label,
                    option.Style,
                    option.KeyTip,
                    CreateColorScalePresetSwatch(option.MinColor, option.MidColor, option.MaxColor));
                item.Click += CfColorScalePresetMenuItem_Click;
                menuItem.Items.Add(item);
            }
        }

        AddConditionalFormatMoreRulesItem(menuItem, "CM", CfColorScaleMenuItem_Click);
    }

    private static void AddConditionalFormatMoreRulesItem(MenuItem menuItem, string keyTip, RoutedEventHandler clickHandler)
    {
        menuItem.Items.Add(new Separator());
        var moreRules = new MenuItem
        {
            Header = UiText.Get("MainWindow_Header_MoreRules"),
            MinWidth = 224
        };
        RibbonTooltip.SetKeyTip(moreRules, keyTip);
        RibbonMetadata.SetCommandName(moreRules, "More Rules");
        moreRules.Click += clickHandler;
        menuItem.Items.Add(moreRules);
    }

    private static void AddConditionalFormatGalleryHeader(MenuItem menuItem, string header)
    {
        if (menuItem.Items.Count > 0)
            menuItem.Items.Add(new Separator());

        menuItem.Items.Add(new MenuItem
        {
            Header = new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(2, 4, 2, 2)
            },
            IsEnabled = false
        });
    }

    private static MenuItem CreateConditionalFormatPresetMenuItem(
        string label,
        string style,
        string keyTip,
        UIElement swatch)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        header.Children.Add(swatch);
        header.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        var item = new MenuItem
        {
            Header = header,
            Tag = style,
            MinWidth = 224
        };
        RibbonTooltip.SetKeyTip(item, keyTip);
        RibbonMetadata.SetCommandName(item, label);
        return item;
    }

    private static Border CreateDataBarPresetSwatch(RgbColor color, bool gradient)
    {
        Brush fill = gradient
            ? new LinearGradientBrush(
                Colors.White,
                Color.FromRgb(color.R, color.G, color.B),
                0)
            : new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

        return new Border
        {
            Width = 46,
            Height = 14,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = fill,
            Margin = new Thickness(0, 1, 0, 1)
        };
    }

    private static Grid CreateColorScalePresetSwatch(RgbColor minColor, RgbColor? midColor, RgbColor maxColor)
    {
        var grid = new Grid
        {
            Width = 46,
            Height = 14,
            Margin = new Thickness(0, 1, 0, 1)
        };
        var colors = midColor is { } middle
            ? new[] { minColor, middle, maxColor }
            : new[] { minColor, maxColor };
        foreach (var _ in colors)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(index == 0 ? 1 : 0, 1, 1, 1)
            };
            Grid.SetColumn(border, index);
            grid.Children.Add(border);
        }

        return grid;
    }

    private void FormatTableBtn_Click(object sender, RoutedEventArgs e)
    {
        PopulateFormatTableGalleryMenu();
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    /// <summary>The Home ▸ Format as Table gallery context menu, built imperatively from
    /// <see cref="TableStyleGalleryPlanner"/>. Attached to the rendered declarative "Format as Table"
    /// button once the ribbon is built (see <see cref="AttachFormatTableGalleryContextMenu"/>); the
    /// rendered button's click handler (<see cref="FormatTableBtn_Click"/>) opens it.</summary>
    private ContextMenu? _formatTableGalleryMenu;

    private void PopulateFormatTableGalleryMenu()
    {
        if (_formatTableGalleryMenu is { Items.Count: > 0 })
        {
            AttachFormatTableGalleryContextMenu();
            return;
        }

        var menu = _formatTableGalleryMenu ??= new ContextMenu();
        var surface = TableStyleGalleryPlanner.GetSurface(_workbook.Theme);
        foreach (var group in surface.Groups)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(CreateFormatTableGallerySectionHeader(group.Family));

            foreach (var item in group.Items)
            {
                var menuItem = new MenuItem
                {
                    Header = CreateFormatTableGalleryHeader(item),
                    Tag = item,
                    MinWidth = 176
                };
                RibbonTooltip.SetKeyTip(menuItem, item.KeyTip);
                menuItem.Click += FormatTableGalleryMenuItem_Click;
                menu.Items.Add(menuItem);
            }
        }

        AttachFormatTableGalleryContextMenu();
    }

    /// <summary>Attaches the imperatively-built Format as Table gallery menu to the rendered declarative
    /// "Format as Table" button. No-op until both the menu and the rendered button exist; the rendered
    /// button's click runs <see cref="FormatTableBtn_Click"/>, which opens this menu.</summary>
    private void AttachFormatTableGalleryContextMenu()
    {
        if (_formatTableGalleryMenu is { } menu &&
            FindRenderedRibbonControl("Format as Table") is System.Windows.Controls.Primitives.ButtonBase formatTableBtn)
        {
            formatTableBtn.ContextMenu = menu;
        }
    }

    private static MenuItem CreateFormatTableGallerySectionHeader(string family) =>
        new()
        {
            Header = new TextBlock
            {
                Text = family,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(2, 4, 2, 2)
            },
            IsEnabled = false
        };

    private static StackPanel CreateFormatTableGalleryHeader(TableStyleGallerySurfaceItem item)
        => CreateFormatTableGalleryHeader(item.Label, item.Banding);

    private static StackPanel CreateFormatTableGalleryHeader(TableStyleGalleryOption option)
        => CreateFormatTableGalleryHeader(option.Label, option.Banding);

    private static StackPanel CreateFormatTableGalleryHeader(string label, StructuredTableStyleBanding banding)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        panel.Children.Add(CreateFormatTableGallerySwatch(banding));
        panel.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });
        return panel;
    }

    private static Grid CreateFormatTableGallerySwatch(StructuredTableStyleBanding banding)
    {
        var swatch = new Grid
        {
            Width = 54,
            Height = 22,
            SnapsToDevicePixels = true
        };
        swatch.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        swatch.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
        swatch.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });

        AddSwatchBand(swatch, banding.HeaderFill, 0);
        AddSwatchBand(swatch, banding.OddRowFill, 1);
        AddSwatchBand(swatch, banding.EvenRowFill, 2);
        swatch.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1) });
        return swatch;
    }

    private static void AddSwatchBand(Grid swatch, CellColor color, int row)
    {
        var band = new Border { Background = ToBrush(color) };
        Grid.SetRow(band, row);
        swatch.Children.Add(band);
    }

    private static SolidColorBrush ToBrush(CellColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));

    private void FormatTableGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var item = sender is MenuItem { Tag: TableStyleGallerySurfaceItem tagged }
            ? tagged
            : TableStyleGalleryPlanner.GetSurfaceItem(TableStyleGalleryPlanner.GetSurface(_workbook.Theme), 0);
        ApplyTableFormat(item.Option);
    }

    private void ApplyTableFormat(int variant)
    {
        var surface = TableStyleGalleryPlanner.GetSurface(_workbook.Theme);
        ApplyTableFormat(TableStyleGalleryPlanner.GetSurfaceItem(surface, variant).Option);
    }

    private void ApplyTableFormat(TableStyleGalleryOption tableStyle)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var sourceRange = TableCreationPlanner.PlanSourceRange(sheet, range);
        var tableStyleName = tableStyle.StyleName;
        CreateTableDialog? dialog = null;
        dialog = new CreateTableDialog(
            _currentSheetId,
            FormatRangeReference(sourceRange.Start, sourceRange.End),
            tableStyleName,
            request => ApplyCreateTableRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        range = dialog.Result.Range;
        if (!TryExecuteGroupedSheetCommand(
                "Format as Table",
                sheetId => TableCreationPlanner.BuildStyledCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId),
                    dialog.Result.TableStyleName,
                    dialog.Result.FirstRowHasHeaders,
                    tableStyle.Banding)))
            return;
        UpdateViewport();
    }

    private void ApplyCreateTableRangeSelection(
        CreateTableDialog? dialog,
        CreateTableRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End)));
    }

    private void CellStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void ApplyCellStylePreset(CellStylePreset preset)
        => ApplyStyleDiff(CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme));
    private void CellStyleNormalMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Normal);
    private void CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Good);
    private void CellStyleBadMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Bad);
    private void CellStyleNeutralMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Neutral);
    private void CellStyleInputMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Input);
    private void CellStyleOutputMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Output);
    private void CellStyleCalculationMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Calculation);
    private void CellStyleCheckCellMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.CheckCell);
    private void CellStyleLinkedCellMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.LinkedCell);
    private void CellStyleExplanatoryTextMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.ExplanatoryText);
    private void CellStyleH1MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Heading1);
    private void CellStyleH2MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Heading2);
    private void CellStyleNoteMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Note);
    private void CellStyleWarningMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.WarningText);
    private void CellStyleTotalMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Total);
    private void CellStyleAccent1_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent1_20);
    private void CellStyleAccent2_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent2_20);
    private void CellStyleAccent3_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent3_20);
    private void CellStyleAccent4_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent4_20);
    private void CellStyleAccent5_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent5_20);
    private void CellStyleAccent6_20MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent6_20);
    private void CellStyleAccent1_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent1_40);
    private void CellStyleAccent2_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent2_40);
    private void CellStyleAccent3_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent3_40);
    private void CellStyleAccent4_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent4_40);
    private void CellStyleAccent5_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent5_40);
    private void CellStyleAccent6_40MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent6_40);
    private void CellStyleAccent1_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent1_60);
    private void CellStyleAccent2_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent2_60);
    private void CellStyleAccent3_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent3_60);
    private void CellStyleAccent4_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent4_60);
    private void CellStyleAccent5_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent5_60);
    private void CellStyleAccent6_60MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyCellStylePreset(CellStylePreset.Accent6_60);
}
