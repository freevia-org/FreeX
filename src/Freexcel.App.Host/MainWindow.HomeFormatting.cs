using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    // ── Formatting toolbar handlers ───────────────────────────────────────────

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true));
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true));
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = UnderlineButton.IsChecked == true;
        SetToolbarToggleStates(strike: enabled ? false : null);
        ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled));
    }

    private void StrikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = StrikeButton.IsChecked == true;
        SetToolbarToggleStates(underline: enabled ? false : null);
        ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled));
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignCenterBtn.IsChecked = false;
        AlignRightBtn.IsChecked  = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left));
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked  = false;
        AlignRightBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked   = false;
        AlignCenterBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right));
    }

    private void SetToolbarToggleStates(
        bool? underline = null,
        bool? strike = null,
        bool? top = null,
        bool? middle = null,
        bool? bottom = null)
    {
        _suppressToolbarSync = true;
        try
        {
            if (underline.HasValue) UnderlineButton.IsChecked = underline.Value;
            if (strike.HasValue) StrikeButton.IsChecked = strike.Value;
            if (top.HasValue) AlignTopBtn.IsChecked = top.Value;
            if (middle.HasValue) AlignMiddleBtn.IsChecked = middle.Value;
            if (bottom.HasValue) AlignBottomBtn.IsChecked = bottom.Value;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private void WrapTextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(WrapText: WrapTextBtn.IsChecked == true));
    }

    private void MergeCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Merge & Center",
                range,
                CreateMergeAndCenterCommand,
                out _))
            return;

        UpdateViewport();
    }

    private IWorkbookCommand CreateMergeAndCenterCommand(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (targetSheetIds.Count > 1)
        {
            var commands = targetSheetIds
                .SelectMany(sheetId =>
                {
                    var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
                    return new IWorkbookCommand[]
                    {
                        new MergeCellsCommand(sheetId, sheetRange),
                        new ApplyStyleCommand(sheetId, sheetRange, new StyleDiff(HAlign: CellHAlign.Center))
                    };
                })
                .ToList();
            return new CompositeWorkbookCommand("Merge & Center", commands);
        }

        return new CompositeWorkbookCommand(
            "Merge & Center",
            [
                new MergeCellsCommand(_currentSheetId, range),
                new ApplyStyleCommand(_currentSheetId, range, new StyleDiff(HAlign: CellHAlign.Center))
            ]);
    }

    private void FontNameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (FontNameBox.SelectedItem is string name)
            ApplyStyleDiff(new StyleDiff(FontName: name));
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var text = FontSizeBox.Text;
        if (WorksheetSizeInputParser.TryParsePositiveSize(text, out var size))
            ApplyStyleDiff(new StyleDiff(FontSize: size));
    }

    private void FontColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var initial = GetCurrentCellStyle().FontColor;
        if (TryShowColorPicker("Font Color", initial, allowNoColor: false, out var color) && color is { } selected)
            ApplyStyleDiff(new StyleDiff(FontColor: selected));
    }

    private void FillColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var initial = GetCurrentCellStyle().FillColor;
        if (!TryShowColorPicker("Fill Color", initial, allowNoColor: true, out var color))
            return;

        ApplyStyleDiff(color is { } selected
            ? new StyleDiff(FillColor: selected)
            : new StyleDiff(FillColor: null, ClearFill: true));
    }

    private CellStyle GetCurrentCellStyle()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var address = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        return _workbook.GetStyle(sheet?.GetCell(address)?.StyleId ?? StyleId.Default);
    }

    private bool TryShowColorPicker(string title, CellColor? initialColor, bool allowNoColor, out CellColor? color)
    {
        var dialog = new ColorPickerDialog(initialColor, allowNoColor)
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
        if (NumberFormatBox.SelectedIndex < 0) return;
        if (NumberFormatBox.SelectedIndex < NumberFormatOptions.Length)
            ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatOptions[NumberFormatBox.SelectedIndex].Code));
    }

    // ── Font group additions ─────────────────────────────────────────────────

    private void DoubleUnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var isOn = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        if (isOn)
            SetToolbarToggleStates(underline: false, strike: false);
        ApplyStyleDiff(CellStyleDiffPlanner.DoubleUnderlineDiff(isOn));
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

        var newHeight = FontSizePlanner.EstimateFittingRowHeight(fontSize);
        if (!TryExecuteGroupedSheetCommand("Auto Fit Row Height", sheetId =>
                new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, newHeight)))
            return;

        UpdateViewport();
    }

    // ── Border picker ────────────────────────────────────────────────────────

    private void BorderPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ApplyRangeBorderPreset(Func<GridRange, CellAddress, StyleDiff> createDiff, string title)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        IWorkbookCommand CreateSheetCommand(SheetId sheetId)
        {
            var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
            var commands = sheetRange
                .AllCells()
                .Select(address => (Address: address, Diff: createDiff(sheetRange, address)))
                .Where(plan => BorderShortcutService.HasBorderChanges(plan.Diff))
                .Select(plan => (IWorkbookCommand)new ApplyStyleCommand(
                    sheetId,
                    new GridRange(plan.Address, plan.Address),
                    plan.Diff))
                .ToList();

            return commands.Count == 1
                ? commands[0]
                : new CompositeWorkbookCommand(title, commands);
        }

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var command = targetSheetIds.Count == 1
            ? CreateSheetCommand(_currentSheetId)
            : new CompositeWorkbookCommand(title, targetSheetIds.Select(CreateSheetCommand).ToList());

        if (!TryExecuteCommand(command, title))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void BorderAllMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetAllBorderDiff(_borderPickerStyle, _borderPickerColor));

    private void BorderOutsideMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyRangeBorderPreset(
            (range, address) => BorderShortcutService.GetOutlineBorderDiff(range, address, _borderPickerStyle, _borderPickerColor),
            "Outside Borders");

    private void BorderNoneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
    }

    private void BorderBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, _borderPickerStyle, _borderPickerColor));

    private void BorderTopMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Top, _borderPickerStyle, _borderPickerColor));

    private void BorderLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Left, _borderPickerStyle, _borderPickerColor));

    private void BorderRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Right, _borderPickerStyle, _borderPickerColor));

    private void BorderThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Thick, _borderPickerColor));

    private void BorderBottomDoubleMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Double, _borderPickerColor));

    private void BorderThickBoxMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyRangeBorderPreset((range, address) => BorderShortcutService.GetOutlineBorderDiff(range, address, BorderStyle.Thick, _borderPickerColor), "Thick Box Border");

    private void BorderTopAndBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyRangeBorderPreset(
            (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, _borderPickerStyle, _borderPickerColor),
            "Top and Bottom Border");

    private void BorderTopAndThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyRangeBorderPreset(
            (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, BorderStyle.Thick, _borderPickerColor),
            "Top and Thick Bottom Border");

    private void BorderTopAndDoubleBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyRangeBorderPreset(
            (range, address) => BorderShortcutService.GetTopAndBottomBorderDiff(range, address, _borderPickerStyle, BorderStyle.Double, _borderPickerColor),
            "Top and Double Bottom Border");

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
        SetToolbarToggleStates(top: true, middle: false, bottom: false);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top));
    }

    private void AlignMiddleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        SetToolbarToggleStates(top: false, middle: true, bottom: false);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center));
    }

    private void AlignBottomBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        SetToolbarToggleStates(top: false, middle: false, bottom: true);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom));
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
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientHorizMenuItem_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(TextRotation: 0));
    private void OrientAngleCCWMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: 45));
    private void OrientAngleCWMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: -45));
    private void OrientVertMenuItem_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateUpMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateDownMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: -90));

    // ── Number group additions ───────────────────────────────────────────────

    private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: "$#,##0.00"));
    private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: "0%"));
    private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: "#,##0.00"));

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
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
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
    private void CfDataBarMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Data Bar");
    private void CfColorScaleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Color Scale");
    private void CfIconSetMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Icon Set");
    private void CfNewRuleMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("New Rule");
    private void CfNewFormulaRuleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Formula");
    private void CfClearRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand(
                "Clear Conditional Formatting",
                sheetId => new ClearConditionalFormatsCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))))
            return;
        UpdateViewport();
    }
    private void CfManageRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var dlg = new ManageConditionalFormatsDialog(sheet, SheetGrid.SelectedRange) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null) return;
        var newRules = dlg.ResultRules;
        if (!TryExecuteGroupedSheetCommand(
                "Manage Conditional Formatting Rules",
                sheetId =>
                {
                    var remapped = newRules
                        .Select(r => GroupedSheetRangePlanner.CloneConditionalFormatForSheet(r, sheetId))
                        .ToList();
                    return new ReplaceAllConditionalFormatsCommand(sheetId, remapped);
                }))
            return;
        UpdateViewport();
    }

    private void ShowCfDialog(string ruleType)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = ConditionalFormatDialogFactory.Create(ruleType, range);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || dlg.ResultRule is null) return;
        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, GroupedSheetRangePlanner.CloneConditionalFormatForSheet(dlg.ResultRule, sheetId))))
            return;
        UpdateViewport();
    }

    private void FormatTableBtn_Click(object sender, RoutedEventArgs e)
    {
        PopulateFormatTableGalleryMenu();
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void PopulateFormatTableGalleryMenu()
    {
        if (FormatTableGalleryMenu is null || FormatTableGalleryMenu.Items.Count > 0)
            return;

        var options = TableStyleGalleryPlanner.GetOptions();
        string? currentFamily = null;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var family = option.Label.Split(' ', 2)[0];
            if (!string.Equals(currentFamily, family, StringComparison.Ordinal))
            {
                if (FormatTableGalleryMenu.Items.Count > 0)
                    FormatTableGalleryMenu.Items.Add(new Separator());
                FormatTableGalleryMenu.Items.Add(CreateFormatTableGallerySectionHeader(family));
                currentFamily = family;
            }

            var menuItem = new MenuItem
            {
                Header = CreateFormatTableGalleryHeader(option),
                Tag = index.ToString(CultureInfo.InvariantCulture),
                MinWidth = 176
            };
            RibbonTooltip.SetKeyTip(menuItem, $"{family[0]}{option.Label[(family.Length + 1)..]}");
            menuItem.Click += FormatTableGalleryMenuItem_Click;
            FormatTableGalleryMenu.Items.Add(menuItem);
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

    private static StackPanel CreateFormatTableGalleryHeader(TableStyleGalleryOption option)
    {
        var banding = option.Banding;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        panel.Children.Add(CreateFormatTableGallerySwatch(banding));
        panel.Children.Add(new TextBlock
        {
            Text = option.Label,
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
        var index = sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var parsed)
            ? parsed
            : 0;
        ApplyTableFormat(index);
    }

    private void ApplyTableFormat(int variant)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var tableStyle = TableStyleGalleryPlanner.GetOption(variant);
        var tableStyleName = tableStyle.StyleName;
        var dialog = new CreateTableDialog(_currentSheetId, FormatRangeReference(range.Start, range.End), tableStyleName) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        range = dialog.Result.Range;
        if (!TryExecuteGroupedSheetCommand(
                "Format as Table",
                sheetId => new CreateStyledStructuredTableCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId),
                    dialog.Result.TableStyleName,
                    dialog.Result.FirstRowHasHeaders,
                    tableStyle.Banding)))
            return;
        UpdateViewport();
    }

    private void CellStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
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
}
