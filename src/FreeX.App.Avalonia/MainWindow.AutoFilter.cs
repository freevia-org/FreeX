using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

using Free.Shared.Shell.Avalonia;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Flyout? _autoFilterFlyout;

    private static AvaloniaCompactDialogChromeStyle AutoFilterDialogChromeStyle => new(FormulaBarFontFamily);

    // AutoFilter button visuals — match WPF GridView.Rendering.AutoFilter.cs constants.
    private static readonly IBrush AutoFilterBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(142, 153, 166));
    private static readonly IBrush AutoFilterGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(45, 55, 65));
    private static readonly IBrush ActiveAutoFilterGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(15, 109, 140));

    /// <summary>
    /// Wraps a header cell's content with an AutoFilter dropdown button when the cell is a filter-button
    /// cell (the active AutoFilter range's header row). The button opens the column's filter flyout. Cells
    /// that are not filter headers are returned unchanged.
    /// </summary>
    private Border DecorateAutoFilterHeaderCell(Border cellBorder, CellAddress address)
    {
        if (!AutoFilterHeaderButtonPlanner.IsFilterButtonCell(_session.ActiveSheet, address.Row, address.Col))
            return cellBorder;

        var content = cellBorder.Child;
        cellBorder.Child = null;

        // Determine per-column active-filter state (mirrors WPF ActiveAutoFilterColumns logic).
        var sheet = _session.ActiveSheet;
        var isActive = AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet) is { } range &&
                       AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, address.Col);

        // Build a crisp drawn chevron button matching WPF's drawn geometry + gradient background.
        // Triangle points mirror WPF DrawAutoFilterGlyph: (cx-3,cy-2)-(cx+3,cy-2)-(cx,cy+2).
        var chevronPath = isActive
            ? new AvaloniaPath
            {
                // Active: funnel/filter icon (wide-top narrowing to a bar, matching WPF).
                Data = Geometry.Parse("M3,2 L12,2 L8.5,6 L8.5,12 L6.5,12 L6.5,6 Z"),
                Fill = ActiveAutoFilterGlyphBrush,
                Stretch = Stretch.None,
            }
            : new AvaloniaPath
            {
                // Inactive: simple filled downward triangle, centered in 15×15 at (7.5, 8.5).
                Data = Geometry.Parse("M4.5,6.5 L10.5,6.5 L7.5,10.5 Z"),
                Fill = AutoFilterGlyphBrush,
                Stretch = Stretch.None,
            };

        var buttonBorder = new Border
        {
            Width = 15,
            MinWidth = 15,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(252, 252, 252), 0),
                    new GradientStop(Color.FromRgb(225, 232, 238), 1),
                }
            },
            BorderBrush = AutoFilterBorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Child = chevronPath,
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
        };

        // Wrap in a Button for click handling and accessibility.
        var button = new Button
        {
            Content = buttonBorder,
            Padding = new Thickness(0),
            MinWidth = 0,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, $"AutoFilterButton_{address.Row}_{address.Col}");
        AutomationProperties.SetName(button, UiText.CreateAutomationName(UiText.Get("PivotFieldFilter_Filter")));
        button.Click += (_, _) => OpenAutoFilterFlyout(button, address);
        // The rendered glyph is the inner Border, and Linux/X11 pointer delivery can terminate at
        // that visual hit target without synthesizing Button.Click. Keep the direct pointer route
        // on the glyph while retaining Button.Click for keyboard and accessibility activation.
        buttonBorder.PointerPressed += (_, e) =>
        {
            if (e.Handled)
                return;

            e.Handled = true;
            OpenAutoFilterFlyout(button, address);
        };

        var grid = new AvaloniaGrid { ClipToBounds = true };
        if (content is Control existing)
            grid.Children.Add(existing);
        grid.Children.Add(button);
        cellBorder.Child = grid;
        return cellBorder;
    }

    /// <summary>
    /// Keyboard fallback for Excel's Alt+Down shortcut (<c>IsOpenActiveDropdownShortcut</c>): when the
    /// active cell has no data-validation dropdown to open, and is instead an AutoFilter header/filter-
    /// button cell, opens that column's filter flyout — mirroring WPF's
    /// <c>OpenActiveDropdown</c> → <c>OpenAutoFilterDropdownForActiveCell</c> fallback
    /// (MainWindow.EditingDropdowns.cs). Returns false (leaving the key unhandled) when the active cell
    /// is not a filter-button cell, so callers can fall through to whatever default behavior applies.
    /// </summary>
    private bool OpenActiveAutoFilterDropdown()
    {
        var address = _session.ActiveCell;
        if (!AutoFilterHeaderButtonPlanner.IsFilterButtonCell(_session.ActiveSheet, address.Row, address.Col))
            return false;

        var anchor = FindAutoFilterHeaderButton(address) ?? (Control?)_activeCellBorder ?? _sheetGridHost;
        OpenAutoFilterFlyout(anchor, address);
        return true;
    }

    private Button? FindAutoFilterHeaderButton(CellAddress address)
    {
        var automationId = $"AutoFilterButton_{address.Row}_{address.Col}";
        return _sheetGridHost
            .GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    automationId,
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// Opens the AutoFilter dropdown for the header cell: Sort A-Z / Sort Z-A, Clear Filter, and a value
    /// checklist. Sorting runs the Core <see cref="SortCommand"/> over the filter range by the clicked
    /// column; applying the checklist (or Clear) runs the Core <see cref="FilterCommand"/> with the chosen
    /// values (an empty set clears the column's filter). The checklist values are the canonical filter text
    /// the engine matches, so selections agree with what is hidden/shown.
    /// </summary>
    private void OpenAutoFilterFlyout(Control anchor, CellAddress headerCell)
    {
        CloseAutoFilterFlyout();

        var sheet = _session.ActiveSheet;
        if (AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet) is not { } range)
            return;

        if (!AutoFilterDropdownMenuPlanner.TryPlan(range, headerCell, out var dropdownPlan))
            return;

        var columnOffset = dropdownPlan.FilterColumnOffset;
        var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            _session.Workbook,
            sheet,
            dropdownPlan,
            AvaloniaPlannerTextResources.AutoFilter,
            AvaloniaPlannerTextResources.AutoFilter.BlankDisplayText);
        var model = AutoFilterMenuPlanner.Build(
            menuPlan,
            AvaloniaPlannerTextResources.AutoFilter);

        var panel = new StackPanel { Spacing = 4, MinWidth = 260, MaxWidth = 340 };
        var allItems = AutoFilterMenuPlanner.CreateDialogItems(model).ToList();
        var visibleItems = new List<AutoFilterDialogItem>();
        var checkBoxes = new List<CheckBox>();
        var checklistPanel = new StackPanel();
        var updatingSelectAll = false;
        Control? initialFocusTarget = null;
        var flyout = new Flyout
        {
            Placement = ToNativeAutoFilterPlacement(AutoFilterPopupPlacementPlanner.PreferredEdge),
            ShowMode = FlyoutShowMode.Standard,
        };
        var searchBox = new TextBox
        {
            PlaceholderText = UiText.Get("AutoFilter_SectionSearch"),
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(searchBox, $"AutoFilterSearchBox_{headerCell.Row}_{headerCell.Col}");
        AutomationProperties.SetName(searchBox, UiText.CreateAutomationName(UiText.Get("AutoFilter_SectionSearch")));
        var addSelectionBox = new CheckBox
        {
            Content = UiText.Get("AutoFilter_AddCurrentSelectionToFilter"),
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        var selectAll = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("AutoFilter_SelectAll2")),
            IsThreeState = true,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        var criteriaBox = new TextBox
        {
            PlaceholderText = UiText.Get("AutoFilter_CustomCriteriaPlaceholder"),
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(criteriaBox, $"AutoFilterCriteriaBox_{headerCell.Row}_{headerCell.Col}");

        void RefreshChecklist()
        {
            var state = AutoFilterMenuPlanner.PlanChecklistState(allItems, searchBox.Text);
            visibleItems = state.VisibleItems.ToList();
            checklistPanel.Children.Clear();
            checkBoxes.Clear();

            foreach (var dialogItem in visibleItems)
            {
                var box = new CheckBox
                {
                    Content = dialogItem.DisplayText,
                    IsChecked = dialogItem.IsSelected,
                    Tag = dialogItem.Value,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                };
                box.IsCheckedChanged += (_, _) =>
                {
                    dialogItem.IsSelected = box.IsChecked == true;
                    selectAll.IsChecked = AutoFilterMenuPlanner.SelectAllState(visibleItems);
                };
                checkBoxes.Add(box);
                checklistPanel.Children.Add(box);
            }

            selectAll.IsEnabled = state.IsChecklistEnabled;
            updatingSelectAll = true;
            try
            {
                selectAll.IsChecked = state.SelectAllState;
            }
            finally
            {
                updatingSelectAll = false;
            }
            addSelectionBox.IsVisible = state.IsAddCurrentSelectionVisible;
            addSelectionBox.IsEnabled = state.IsAddCurrentSelectionEnabled;
        }

        void AddMenuCommand(AutoFilterMenuItem item, Action onClick, bool isEnabled = true)
        {
            var button = CreateAutoFilterActionButton(item, onClick, isEnabled);
            if (initialFocusTarget is null && item.FocusRole == AutoFilterMenuEntryFocusRole.Command)
                initialFocusTarget = button;

            panel.Children.Add(button);
        }

        void ApplySelectionToVisible(bool isSelected)
        {
            var updated = AutoFilterMenuPlanner.SetSelectionForSearch(allItems, searchBox.Text, isSelected);
            allItems.Clear();
            allItems.AddRange(updated);
            RefreshChecklist();
        }

        foreach (var item in model.Items)
        {
            switch (item.Kind)
            {
                case AutoFilterMenuItemKind.SortAscending:
                    AddMenuCommand(item, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterResult(range, columnOffset, new AutoFilterDialogResult(
                            AutoFilterSortDirection.Ascending, [], string.Empty, string.Empty));
                    });
                    break;
                case AutoFilterMenuItemKind.SortDescending:
                    AddMenuCommand(item, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterResult(range, columnOffset, new AutoFilterDialogResult(
                            AutoFilterSortDirection.Descending, [], string.Empty, string.Empty));
                    });
                    break;
                case AutoFilterMenuItemKind.ClearFilter:
                    AddMenuCommand(item, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterResult(range, columnOffset, AutoFilterDialogCriteriaPlanner.CreateClearFilterResult());
                    }, item.IsEnabled);
                    break;
                case AutoFilterMenuItemKind.FilterByColor when model.ColorOptions.Count > 0:
                    panel.Children.Add(CreateAutoFilterColorPanel(model.ColorOptions, option =>
                    {
                        flyout.Hide();
                        RunAutoFilterResult(
                            range,
                            columnOffset,
                            AutoFilterMenuPlanner.BuildResult(
                                allItems,
                                searchBox.Text,
                                criteriaBox.Text,
                                new AutoFilterColorFilter(option.Kind, option.Color),
                                addSelectionBox.IsChecked == true));
                    }));
                    break;
                // R76-render-autofilter-dropdown-4-2: "No Fill" has no single color to sort toward
                // (see AutoFilterDropdownMenuPlanner.CreateSortByColorCommand), so only options with
                // an actual color are offered here -- unlike the Filter-by-Color panel above, which
                // legitimately offers "No Fill" as a filter target.
                case AutoFilterMenuItemKind.SortByColor when model.ColorOptions.Any(option => option.Color is not null):
                    panel.Children.Add(CreateAutoFilterColorPanel(
                        model.ColorOptions.Where(option => option.Color is not null).ToList(),
                        option =>
                        {
                            flyout.Hide();
                            RunAutoFilterResult(
                                range,
                                columnOffset,
                                AutoFilterDialogCriteriaPlanner.BuildSortByColorResult(
                                    new AutoFilterColorFilter(option.Kind, option.Color)));
                        },
                        "AutoFilter_SortByColor"));
                    break;
                case AutoFilterMenuItemKind.FilterFamily:
                    panel.Children.Add(CreateAutoFilterCriteriaPanel(model, criteriaBox));
                    break;
                case AutoFilterMenuItemKind.Search:
                    searchBox.TextChanged += (_, _) => RefreshChecklist();
                    panel.Children.Add(searchBox);
                    panel.Children.Add(addSelectionBox);
                    break;
                case AutoFilterMenuItemKind.SelectAll:
                    selectAll.Content = item.Label;
                    selectAll.IsCheckedChanged += (_, _) =>
                    {
                        if (updatingSelectAll)
                            return;

                        if (selectAll.IsChecked is bool isChecked)
                            ApplySelectionToVisible(isChecked);
                    };
                    panel.Children.Add(selectAll);
                    break;
                case AutoFilterMenuItemKind.Separator:
                    panel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = Brush(0xDA, 0xDC, 0xDF),
                        Margin = new Thickness(0, 2),
                    });
                    break;
            }
        }

        RefreshChecklist();
        panel.Children.Add(new ScrollViewer { Content = checklistPanel, MaxHeight = 220 });

        var okButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            IsDefault = true,
            MinWidth = 72,
        };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, AutoFilterDialogChromeStyle, 72, isDefault: true);
        okButton.Click += (_, _) =>
        {
            flyout.Hide();
            RunAutoFilterResult(
                range,
                columnOffset,
                AutoFilterMenuPlanner.BuildResult(
                    allItems,
                    searchBox.Text,
                    criteriaBox.Text,
                    addCurrentSelectionToFilter: addSelectionBox.IsChecked == true));
        };
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([okButton], new Thickness(0, 6, 0, 0)));

        panel.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                flyout.Hide();
                e.Handled = true;
            }
        };
        flyout.Content = new Border { Padding = new Thickness(8), Child = panel };
        _autoFilterFlyout = flyout;
        RecordOptionalAutoFilterPlacementTarget(AutomationProperties.GetAutomationId(anchor));
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_autoFilterFlyout, flyout))
                _autoFilterFlyout = null;
        };
        flyout.ShowAt(anchor);
        (initialFocusTarget ?? searchBox).Focus();
    }

    private static PlacementMode ToNativeAutoFilterPlacement(AutoFilterPopupPlacementEdge edge) =>
        edge switch
        {
            AutoFilterPopupPlacementEdge.BottomStart => PlacementMode.BottomEdgeAlignedLeft,
            _ => PlacementMode.BottomEdgeAlignedLeft
        };

    private void CloseAutoFilterFlyout()
    {
        if (_autoFilterFlyout is not { } flyout)
            return;

        _autoFilterFlyout = null;
        flyout.Hide();
    }

    private Control CreateAutoFilterCriteriaPanel(AutoFilterMenuModel model, TextBox criteriaBox)
    {
        var options = model.CriteriaOptions;
        var selector = new ComboBox
        {
            ItemsSource = options.Select(option => option.Label).ToArray(),
            SelectedIndex = options.Count > 0 ? 0 : -1,
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(selector, "AutoFilterCriteriaOperatorBox");
        AutomationProperties.SetName(
            selector,
            model.Items.FirstOrDefault(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)?.Label
                ?? UiText.Get("AutoFilter_FiltersAutomationName"));

        var valueBox = new TextBox
        {
            PlaceholderText = UiText.Get("ConditionalFormat_ValueLabel"),
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        var secondValueBox = new TextBox
        {
            PlaceholderText = UiText.Get("ConditionalFormat_MaximumLabel"),
            MinHeight = 26,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        void UpdateCriteria()
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= options.Count)
                return;

            var option = options[selector.SelectedIndex];
            secondValueBox.IsVisible = AutoFilterMenuPlanner.RequiresSecondCriteriaValue(option);
            valueBox.PlaceholderText = AutoFilterMenuPlanner.RequiresCountCriteriaValue(option)
                ? "Count"
                : option.RequiresValue ? "Value" : string.Empty;
            valueBox.IsEnabled = option.RequiresValue;
            criteriaBox.Text = AutoFilterMenuPlanner.BuildCompletedCriteriaText(
                option,
                valueBox.Text,
                secondValueBox.Text);
        }

        selector.SelectionChanged += (_, _) =>
        {
            UpdateCriteria();
        };
        valueBox.TextChanged += (_, _) =>
        {
            UpdateCriteria();
        };
        secondValueBox.TextChanged += (_, _) =>
        {
            UpdateCriteria();
        };
        UpdateCriteria();

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = model.Items.FirstOrDefault(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)?.Label
                ?? UiText.Get("AutoFilter_FiltersAutomationName"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
        });
        panel.Children.Add(selector);
        panel.Children.Add(valueBox);
        panel.Children.Add(secondValueBox);
        panel.Children.Add(criteriaBox);

        if (model.CriteriaSuggestions.Count > 0)
        {
            panel.Children.Add(new ComboBox
            {
                ItemsSource = model.CriteriaSuggestions,
                SelectedIndex = -1,
                MinHeight = 26,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            });
            if (panel.Children[^1] is ComboBox suggestions)
            {
                suggestions.SelectionChanged += (_, _) =>
                {
                    if (suggestions.SelectedItem is string suggestion)
                        criteriaBox.Text = suggestion;
                };
            }
        }

        return panel;
    }

    private Control CreateAutoFilterColorPanel(
        IReadOnlyList<AutoFilterColorOption> options,
        Action<AutoFilterColorOption> apply,
        string headerResourceKey = "AutoFilter_FilterByColor")
    {
        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(headerResourceKey),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
        });

        var swatches = new WrapPanel();
        foreach (var option in options)
        {
            var button = new Button
            {
                Width = 76,
                MinWidth = 76,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 4),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            BorderBrush = Brush(0x80, 0x80, 0x80),
                            BorderThickness = new Thickness(1),
                            Background = option.Color is { } color
                                ? new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
                                : Brushes.White,
                        },
                        new TextBlock
                        {
                            Text = option.Kind == AutoFilterColorFilterKind.NoFill ? UiText.Get("AutoFilter_NoFill") : option.Label,
                            FontSize = 11,
                            FontFamily = FormulaBarFontFamily,
                        },
                    },
                },
            };
            AutomationProperties.SetName(button, option.Label);
            button.Click += (_, _) => apply(option);
            swatches.Children.Add(button);
        }

        root.Children.Add(swatches);
        return root;
    }

    private Button CreateAutoFilterActionButton(AutoFilterMenuItem item, Action onClick, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    AvaloniaRibbonIcons.BuildMonochrome(item.IconKind, 14, null, Brush(0x21, 0x21, 0x21)),
                    new TextBlock
                    {
                        Text = item.Label,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    },
                },
            },
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsEnabled = isEnabled,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    // Worksheet context menu ▸ Sort and Filter ▸ Clear Filter. Unhides every row the active sheet's
    // AutoFilter is currently hiding. FilterCommand with an empty allowed-value set clears the whole
    // range's hidden rows in one undoable step (the same Core command the column dropdown's Clear uses),
    // so this matches Excel's "remove all filters on this AutoFilter" behaviour.
    private void ClearActiveSheetFilters()
    {
        var sheet = _session.ActiveSheet;
        if (AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet) is not { } range)
        {
            RefreshShell(UiText.Get("WTA_ContextFilter_NoFilter"));
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        var plan = _filterWorkflowSession.CreateClearAllPlan(sheet, range);
        var result = _session.ExecuteWorksheetFilterCommand(
            range,
            currentRange => _filterWorkflowSession.CreateClearAllPlan(sheet, currentRange).Command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_FilterFailed"));
            return;
        }

        _filterWorkflowSession.RecordSuccessfulClearAll(plan);
        RecalculateAfterAutoFilterMutation();
        RefreshShell(UiText.Get("ShellLoc_ClearedFilter"));
    }

    private void RunAutoFilter(GridRange range, uint columnOffset, IReadOnlyList<string> allowedValues)
        => RunAutoFilterPlan(_filterWorkflowSession.PlanAllowedValues(
            _session.ActiveSheet.Id,
            range,
            columnOffset,
            allowedValues));

    private void RunAutoFilterResult(GridRange range, uint columnOffset, AutoFilterDialogResult result)
    {
        var plan = _filterWorkflowSession.PlanDialogResult(
            _session.ActiveSheet,
            range,
            columnOffset,
            result);
        if (!plan.Success)
        {
            ShowEditIssue(UiText.Get(WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan)));
            return;
        }

        RunAutoFilterPlan(plan);
    }

    private void RunAutoFilterPlan(WorksheetFilterMutationPlan plan)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteWorksheetFilterMutationPlan(plan);
        if (!result.Success)
        {
            var fallback = UiText.Get(WorksheetFilterMessagePlanner.GetCommandFailureResourceKey(plan.Kind));
            ShowEditIssue(result.ErrorMessage ?? fallback);
            return;
        }

        _filterWorkflowSession.RecordSuccessfulMutation(plan);
        RecalculateAfterAutoFilterMutation();
        RefreshShell(UiText.Get(WorksheetFilterMessagePlanner.GetSuccessResourceKey(plan.Kind)));
    }

    // Filter visibility and sort order are workbook state, but they are not ordinary cell edits.
    // Recalculate explicitly so SUBTOTAL/AGGREGATE formulas that ignore hidden rows update in the
    // same interaction as the corresponding WPF host path.
    private void RecalculateAfterAutoFilterMutation() => _session.RecalculateWorkbook();

}
