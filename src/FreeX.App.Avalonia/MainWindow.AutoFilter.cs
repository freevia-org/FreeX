using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Presentation;
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

    private const double AutoFilterPopupContentWidth = 276;
    private const double AutoFilterColorButtonWidth = 92;
    private const double AutoFilterColorButtonHeight = 24;

    // AutoFilter button visuals — match WPF GridView.Rendering.AutoFilter.cs constants.
    private static readonly IBrush AutoFilterBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(142, 153, 166));
    private static readonly IBrush AutoFilterGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(45, 55, 65));
    private static readonly IBrush ActiveAutoFilterGlyphBrush = new ImmutableSolidColorBrush(Color.FromRgb(15, 109, 140));

    /// <summary>
    /// Wraps a header cell's content with an AutoFilter dropdown button when the cell is a filter-button
    /// cell (the active AutoFilter range's header row). The button opens the column's filter flyout. Cells
    /// that are not filter headers are returned unchanged.
    /// </summary>
    private Border DecorateAutoFilterHeaderCell(Border cellBorder, CellAddress address, double zoomFactor)
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
        // WPF draws this whole button (box + glyph) inside a single logical coordinate space that
        // is then scaled as a unit by the SheetGrid's zoom RenderTransform (see
        // MainWindow.ViewCommands.cs); Avalonia has no such global transform, so every coordinate
        // here must be multiplied by zoomFactor explicitly to stay proportional to the (already
        // zoom-scaled) header cell it sits in.
        var chevronPath = isActive
            ? new AvaloniaPath
            {
                // Active: funnel/filter icon (wide-top narrowing to a bar, matching WPF).
                Data = Geometry.Parse(FormattableString.Invariant(
                    $"M{3 * zoomFactor},{2 * zoomFactor} L{12 * zoomFactor},{2 * zoomFactor} L{8.5 * zoomFactor},{6 * zoomFactor} L{8.5 * zoomFactor},{12 * zoomFactor} L{6.5 * zoomFactor},{12 * zoomFactor} L{6.5 * zoomFactor},{6 * zoomFactor} Z")),
                Fill = ActiveAutoFilterGlyphBrush,
                Stretch = Stretch.None,
            }
            : new AvaloniaPath
            {
                // Inactive: simple filled downward triangle, centered in 15×15 at (7.5, 8.5).
                Data = Geometry.Parse(FormattableString.Invariant(
                    $"M{4.5 * zoomFactor},{6.5 * zoomFactor} L{10.5 * zoomFactor},{6.5 * zoomFactor} L{7.5 * zoomFactor},{10.5 * zoomFactor} Z")),
                Fill = AutoFilterGlyphBrush,
                Stretch = Stretch.None,
            };

        var buttonBorder = new Border
        {
            Width = 15 * zoomFactor,
            MinWidth = 15 * zoomFactor,
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
            Margin = new Thickness(0, 0, 2 * zoomFactor, 0),
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

            var point = e.GetCurrentPoint(buttonBorder);
            if (!point.Properties.IsLeftButtonPressed)
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

        var savedCriteria = AutoFilterDialogCriteriaPlanner.GetSavedCriteria(sheet, dropdownPlan, model.FilterKind);
        var panel = new StackPanel { Spacing = 4 };
        var criteriaValidation = new AutoFilterCriteriaValidation();
        var checklistEdited = false;
        var allItems = AutoFilterMenuPlanner.CreateDialogItems(model).ToList();
        var visibleItems = new List<AutoFilterDialogItem>();
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
        // The shared compact text-box chrome centres the text and placeholder vertically in the box;
        // the bare Fluent padding clipped the placeholder at the top of the field.
        AvaloniaCompactDialogChrome.ApplyTextBox(searchBox, AutoFilterDialogChromeStyle);
        searchBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
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
        AutomationProperties.SetName(criteriaBox, UiText.Get("AutoFilter_CriteriaText"));

        void RefreshChecklist()
        {
            var state = AutoFilterMenuPlanner.PlanChecklistState(allItems, searchBox.Text);
            visibleItems = state.VisibleItems.ToList();
            checklistPanel.Children.Clear();

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
                    checklistEdited = true;
                    dialogItem.IsSelected = box.IsChecked == true;
                    updatingSelectAll = true;
                    try
                    {
                        selectAll.IsChecked = AutoFilterMenuPlanner.SelectAllState(visibleItems);
                    }
                    finally
                    {
                        updatingSelectAll = false;
                    }
                };
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

        void AddUnavailableColorRow(string labelKey)
        {
            var unavailable = new AutoFilterMenuItem(
                labelKey == "AutoFilter_SortByColor"
                    ? AutoFilterMenuItemKind.SortByColor
                    : AutoFilterMenuItemKind.FilterByColor,
                UiText.Get(labelKey),
                IsEnabled: false,
                IconKind: RibbonCommandIconKind.Color,
                ShowsContinuation: true);
            panel.Children.Add(CreateAutoFilterActionButton(unavailable, () => { }, isEnabled: false));
        }

        void ApplySelectionToVisible(bool isSelected)
        {
            checklistEdited = true;
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
                    // WPF AutoFilterDialog: a column with no cell colours still shows a disabled
                    // "Sort by Color" cascade row right after the sort commands.
                    if (model.ColorOptions.Count == 0)
                        AddUnavailableColorRow("AutoFilter_SortByColor");
                    break;
                case AutoFilterMenuItemKind.ClearFilter:
                    AddMenuCommand(item, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterResult(range, columnOffset, AutoFilterDialogCriteriaPlanner.CreateClearFilterResult());
                    }, item.IsEnabled);
                    // ...and a disabled "Filter by Color" cascade row right after Clear Filter.
                    if (model.ColorOptions.Count == 0)
                        AddUnavailableColorRow("AutoFilter_FilterByColor");
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
                    panel.Children.Add(CreateAutoFilterCriteriaPanel(model, criteriaBox, savedCriteria, criteriaValidation));
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
        AutomationProperties.SetAutomationId(okButton, "AutoFilterOkButton");
        AvaloniaCompactDialogChrome.ApplyButton(okButton, AutoFilterDialogChromeStyle, 72, isDefault: true);
        okButton.Click += (_, _) =>
        {
            // WPF AutoFilterDialog.ValidateTypedCriteriaInputs: an incomplete typed criterion keeps the
            // dialog open, warns ("Enter first between value", ...) and refocuses the offending input.
            if (criteriaValidation.Validate() is { } issue)
            {
                RunGuarded(() => ShowAutoFilterCriteriaWarningAsync(
                    UiText.Get(issue.ResourceKey),
                    UiText.Format("AutoFilter_TitleWithHeader", menuPlan.HeaderText),
                    issue.Target));
                return;
            }

            flyout.Hide();
            var criteriaText = criteriaBox.Text?.Trim() ?? string.Empty;
            if (savedCriteria is not null && criteriaText == savedCriteria.CriteriaText)
            {
                if (!checklistEdited && string.IsNullOrWhiteSpace(searchBox.Text))
                    return;

                criteriaText = string.Empty;
            }

            RunAutoFilterResult(
                range,
                columnOffset,
                AutoFilterMenuPlanner.BuildResult(
                    allItems,
                    searchBox.Text,
                    criteriaText,
                    addCurrentSelectionToFilter: addSelectionBox.IsChecked == true));
        };
        var cancelButton = new Button { Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")) };
        AutomationProperties.SetAutomationId(cancelButton, "AutoFilterCancelButton");
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, AutoFilterDialogChromeStyle, 72);
        cancelButton.Click += (_, _) => flyout.Hide();
        // WPF AutoFilterDialog: a 312 outer / 296 client window with a 10px margin, so 276 of content.
        // The popup padding (8) plus the presenter frame stand in for that margin.
        var chrome = new DockPanel { Width = AutoFilterPopupContentWidth };
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 6, 0, 0));
        DockPanel.SetDock(actions, Dock.Bottom);
        chrome.Children.Add(actions);
        // WPF scrolls the whole menu only past its 560 outer MaxHeight (470 of content once the margin and
        // button row come off); the value list scrolls itself. The window-height term only shrinks the
        // menu when the main window is shorter than that.
        chrome.Children.Add(new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Clamp(Bounds.Height - 24, 240, 470),
        });

        chrome.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                flyout.Hide();
                e.Handled = true;
            }
        };
        flyout.Content = new Border { Padding = new Thickness(8), Child = chrome };
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

    private sealed record AutoFilterCriteriaIssue(string ResourceKey, Control Target);

    private sealed class AutoFilterCriteriaValidation
    {
        public Func<AutoFilterCriteriaIssue?> Validate { get; set; } = () => null;
    }

    private async Task ShowAutoFilterCriteriaWarningAsync(string message, string title, Control target)
    {
        await AvaloniaUserMessageDialog.ShowWarningAsync(this, message, title);
        target.Focus();
    }

    private Control CreateAutoFilterCriteriaPanel(
        AutoFilterMenuModel model,
        TextBox criteriaBox,
        AutoFilterSavedCriteria? savedCriteria,
        AutoFilterCriteriaValidation? criteriaValidation = null)
    {
        criteriaValidation ??= new AutoFilterCriteriaValidation();
        var options = model.CriteriaOptions;
        var secondOptions = AutoFilterDialogCriteriaPlanner.GetSecondRowCriteriaOptions(options);
        var familyLabel = model.Items.FirstOrDefault(item => item.Kind == AutoFilterMenuItemKind.FilterFamily)?.Label
            ?? UiText.Get("AutoFilter_FiltersAutomationName");
        var firstIndex = FindCriteriaOption(options, savedCriteria?.First);
        var secondIndex = FindCriteriaOption(secondOptions, savedCriteria?.Second);
        var canEditSavedRows = firstIndex >= 0 && (savedCriteria?.Second is null || secondIndex >= 0);
        var selector = CreateAutoFilterCriteriaSelector(
            options.Select(option => option.Label),
            canEditSavedRows ? firstIndex : options.Count > 0 ? 0 : -1,
            "AutoFilterCriteriaOperatorBox",
            familyLabel);
        var valueLabel = UiText.Get("AutoFilter_FilterValue");
        var valueBox = CreateAutoFilterCriteriaValueBox(
            "AutoFilterCriteriaValueBox",
            valueLabel,
            canEditSavedRows ? savedCriteria?.First?.Value : null);
        var maximumLabel = UiText.Get("AutoFilter_MaximumLabel");
        var maximumBox = CreateAutoFilterCriteriaValueBox("AutoFilterCriteriaMaximumBox", maximumLabel);
        var secondSelector = CreateAutoFilterCriteriaSelector(
            secondOptions.Select(option => option.Label),
            // WPF's second operator box has no "(None)" row: it starts on the first real operator and the
            // second condition only applies once its value is filled in.
            canEditSavedRows && secondIndex >= 0 ? secondIndex : 0,
            "AutoFilterSecondCriteriaOperatorBox",
            UiText.Get("AutoFilter_SecondOperator"));
        var secondValueBox = CreateAutoFilterCriteriaValueBox(
            "AutoFilterSecondCriteriaValueBox",
            UiText.Get("AutoFilter_SecondValue"),
            canEditSavedRows ? savedCriteria?.Second?.Value : null);
        var connector = CreateAutoFilterCriteriaSelector(
            [UiText.Get("AutoFilter_ConnectorAnd"), UiText.Get("AutoFilter_ConnectorOr")],
            canEditSavedRows && savedCriteria is { Second: not null, Connector: "Or" } ? 1 : 0,
            "AutoFilterCriteriaConnectorBox",
            $"{UiText.Get("AutoFilter_ConnectorAnd")} / {UiText.Get("AutoFilter_ConnectorOr")}");
        var secondRow = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 4,
        };
        AvaloniaGrid.SetColumn(secondSelector, 1);
        secondRow.Children.Add(connector);
        secondRow.Children.Add(secondSelector);

        var panel = new StackPanel { Spacing = 4 };
        // WPF lists "Text/Number/Date Filters" as a menu row (filter icon, label, "›" cascade arrow)
        // that opens its operator submenu; here the operator, value and combine controls follow the
        // row inline, so the row is a non-interactive header with the same icon and cascade glyph.
        var familyItem = model.Items.FirstOrDefault(item => item.Kind == AutoFilterMenuItemKind.FilterFamily);
        var familyRow = new Border
        {
            Padding = new Thickness(5, 3),
            Child = CreateAutoFilterRowContent(
                familyItem?.IconKind ?? RibbonCommandIconKind.Filter,
                familyLabel,
                hasCascade: true),
        };
        AutomationProperties.SetName(familyRow, familyLabel);
        panel.Children.Add(familyRow);

        string[] datePresets =
        [
            "Custom", "Today", "Yesterday", "Tomorrow",
            "This Week", "Last Week", "Next Week",
            "This Month", "Last Month", "Next Month",
            "This Year", "Last Year", "Next Year",
        ];
        ComboBox? datePresetSelector = null;
        if (model.FilterKind == AutoFilterMenuFilterKind.Date)
        {
            datePresetSelector = CreateAutoFilterCriteriaSelector(
                datePresets.Select(preset => UiText.Get($"AutoFilter_DatePreset{preset.Replace(" ", string.Empty)}")),
                0,
                "AutoFilterDatePresetBox",
                UiText.Get("AutoFilter_DatePreset"));
            panel.Children.Add(datePresetSelector);
        }

        panel.Children.Add(selector);
        // WPF CreateBetweenCriteriaPanel / CreateTopBottomCriteriaPanel: a horizontal "Minimum [..] Maximum [..]"
        // row for Between and a "Show [10] Items|Percent" row for Top/Bottom; the plain value box fills the row.
        var rangeLabel = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            IsVisible = false,
        };
        var maximumRangeLabel = new TextBlock
        {
            Text = UiText.Get("AutoFilter_MaximumLabel"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            IsVisible = false,
        };
        var unitText = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            IsVisible = false,
        };
        var valueRow = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"),
            ColumnSpacing = 6,
        };
        AvaloniaGrid.SetColumn(rangeLabel, 0);
        AvaloniaGrid.SetColumn(valueBox, 1);
        AvaloniaGrid.SetColumn(maximumRangeLabel, 2);
        AvaloniaGrid.SetColumn(maximumBox, 3);
        AvaloniaGrid.SetColumn(unitText, 2);
        valueRow.Children.Add(rangeLabel);
        valueRow.Children.Add(valueBox);
        valueRow.Children.Add(maximumRangeLabel);
        valueRow.Children.Add(maximumBox);
        valueRow.Children.Add(unitText);
        panel.Children.Add(valueRow);
        panel.Children.Add(secondRow);
        panel.Children.Add(secondValueBox);
        panel.Children.Add(criteriaBox);

        var criteriaTouched = false;
        var wasTopBottom = false;
        void UpdateCriteria(bool writeCriteriaText)
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= options.Count)
                return;

            var firstOption = options[selector.SelectedIndex];
            // WPF's Top/Bottom "Show" box starts at 10 (items/percent); leaving it clears that default.
            var nowTopBottom = AutoFilterDialogCriteriaPlanner.IsTopBottomOption(firstOption);
            if (nowTopBottom != wasTopBottom)
            {
                wasTopBottom = nowTopBottom;
                if (nowTopBottom && string.IsNullOrWhiteSpace(valueBox.Text))
                    valueBox.Text = "10";
                else if (!nowTopBottom && valueBox.Text == "10")
                    valueBox.Text = null;
            }

            var secondOption = secondSelector.SelectedIndex >= 0 && secondSelector.SelectedIndex < secondOptions.Count
                ? secondOptions[secondSelector.SelectedIndex]
                : null;
            var preset = datePresetSelector is { SelectedIndex: > 0 }
                ? datePresets[datePresetSelector.SelectedIndex]
                : null;
            var isBetween = AutoFilterDialogCriteriaPlanner.IsBetweenOption(firstOption);
            var isTopBottom = AutoFilterDialogCriteriaPlanner.IsTopBottomOption(firstOption);
            var canCombine = preset is null && !isBetween && !isTopBottom &&
                !AutoFilterDialogCriteriaPlanner.IsAverageOption(firstOption);

            selector.IsEnabled = preset is null;
            valueBox.IsEnabled = preset is null && firstOption.RequiresValue;
            var showMinimumMaximum = isBetween && preset is null;
            var showTopBottom = isTopBottom && preset is null;
            valueBox.PlaceholderText = showMinimumMaximum ? UiText.Get("AutoFilter_MinimumLabel") : valueLabel;
            valueBox.Width = showTopBottom ? 54 : double.NaN;
            valueBox.HorizontalAlignment = showTopBottom ? AvaloniaHorizontalAlignment.Left : AvaloniaHorizontalAlignment.Stretch;
            AvaloniaGrid.SetColumnSpan(valueBox, showMinimumMaximum || showTopBottom ? 1 : 4);
            rangeLabel.IsVisible = showMinimumMaximum || showTopBottom;
            rangeLabel.Text = UiText.Get(showTopBottom ? "AutoFilter_ShowLabel" : "AutoFilter_MinimumLabel");
            maximumRangeLabel.IsVisible = showMinimumMaximum;
            maximumBox.IsVisible = showMinimumMaximum;
            unitText.IsVisible = showTopBottom;
            unitText.Text = UiText.Get(firstOption.CriteriaPrefix.Contains("percent", StringComparison.OrdinalIgnoreCase)
                ? "AutoFilter_Percent"
                : "AutoFilter_Items");
            secondRow.IsVisible = canCombine && secondOptions.Count > 0;
            secondValueBox.IsVisible = canCombine && secondOption?.RequiresValue == true;

            if (!writeCriteriaText)
                return;

            if (preset is not null)
            {
                criteriaBox.Text = AutoFilterDialogCriteriaPlanner.BuildDatePresetCriteriaText(preset, DateTime.Today);
                return;
            }

            var firstCriteria = AutoFilterMenuPlanner.BuildCompletedCriteriaText(firstOption, valueBox.Text, maximumBox.Text);
            var secondCriteria = canCombine && secondOption is not null &&
                (!secondOption.RequiresValue || !string.IsNullOrWhiteSpace(secondValueBox.Text))
                ? AutoFilterMenuPlanner.BuildCompletedCriteriaText(secondOption, secondValueBox.Text, null)
                : string.Empty;
            criteriaBox.Text = AutoFilterDialogCriteriaPlanner.BuildCompositeCriteriaText(
                firstCriteria,
                connector.SelectedIndex == 1 ? "Or" : "And",
                secondCriteria);
        }

        UpdateCriteria(writeCriteriaText: savedCriteria is null);
        if (savedCriteria is not null)
            criteriaBox.Text = savedCriteria.CriteriaText;

        // The criteria rows are always present here (WPF shows its custom panel only after a Filters
        // submenu pick), so validate only once the user has edited an operator/value/connector.
        criteriaValidation.Validate = () =>
        {
            if (!criteriaTouched || selector.SelectedIndex < 0 || selector.SelectedIndex >= options.Count)
                return null;

            if (datePresetSelector is { SelectedIndex: > 0 })
                return null;

            var firstOption = options[selector.SelectedIndex];
            var issue = AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(
                firstOption,
                valueBox.Text,
                valueBox.Text,
                maximumBox.Text,
                valueBox.Text);
            if (issue is null)
                return null;

            Control target = issue.Field == AutoFilterTypedCriteriaField.Maximum ? maximumBox : valueBox;
            return new AutoFilterCriteriaIssue(issue.ResourceKey, target);
        };

        selector.SelectionChanged += (_, _) =>
        {
            criteriaTouched = true;
            UpdateCriteria(writeCriteriaText: true);
        };
        secondSelector.SelectionChanged += (_, _) =>
        {
            criteriaTouched = true;
            UpdateCriteria(writeCriteriaText: true);
        };
        connector.SelectionChanged += (_, _) =>
        {
            criteriaTouched = true;
            UpdateCriteria(writeCriteriaText: true);
        };
        if (datePresetSelector is not null)
            datePresetSelector.SelectionChanged += (_, _) =>
            {
                criteriaTouched = true;
                UpdateCriteria(writeCriteriaText: true);
            };

        foreach (var input in new[] { valueBox, maximumBox, secondValueBox })
        {
            input.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                {
                    criteriaTouched = true;
                    UpdateCriteria(writeCriteriaText: true);
                }
            };
        }

        if (model.CriteriaSuggestions.Count > 0)
        {
            var suggestions = CreateAutoFilterCriteriaSelector(
                model.CriteriaSuggestions,
                -1,
                "AutoFilterCriteriaSuggestionsBox",
                UiText.Get("AutoFilter_CriteriaTemplate"));
            suggestions.SelectionChanged += (_, _) =>
            {
                if (suggestions.SelectedItem is string suggestion)
                    criteriaBox.Text = suggestion;
            };
            panel.Children.Add(suggestions);
        }

        return panel;
    }

    private static int FindCriteriaOption(IReadOnlyList<AutoFilterCriteriaOption> options, AutoFilterSavedCondition? condition)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].CriteriaPrefix == condition?.CriteriaPrefix)
                return i;
        }

        return -1;
    }

    private static ComboBox CreateAutoFilterCriteriaSelector(
        IEnumerable<string> labels,
        int selectedIndex,
        string automationId,
        string automationName)
    {
        var selector = new ComboBox
        {
            ItemsSource = labels.ToArray(),
            SelectedIndex = selectedIndex,
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(selector, automationId);
        AutomationProperties.SetName(selector, automationName);
        return selector;
    }

    private static TextBox CreateAutoFilterCriteriaValueBox(string automationId, string label, string? value = null)
    {
        var input = new TextBox
        {
            Text = value,
            PlaceholderText = label,
            MinHeight = 26,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(input, automationId);
        AutomationProperties.SetName(input, label);
        return input;
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

        // WPF PopulateColorChoices: one section per kind ("Font Color" / "Cell Color" header, 0/8 top and 4
        // bottom margin) over a wrap panel of 92x24 buttons (0,0,6,6 margin) showing a 14x14 swatch and the
        // hex value ("No Fill" for the no-fill choice), with the colour label as tooltip.
        var firstSection = true;
        foreach (var section in options.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor
                     ? UiText.Get("AutoFilter_FontColor")
                     : UiText.Get("AutoFilter_CellColor")))
        {
            root.Children.Add(new TextBlock
            {
                Text = section.Key,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Foreground = HeaderForeground,
                // The panel's 4px spacing supplies WPF's 4px bottom margin; the extra 4 completes its 8px top gap.
                Margin = new Thickness(0, firstSection ? 0 : 4, 0, 0),
            });
            firstSection = false;

            var swatches = new WrapPanel();
            foreach (var option in section)
            {
                var button = new Button
                {
                    Width = AutoFilterColorButtonWidth,
                    MinWidth = AutoFilterColorButtonWidth,
                    Height = AutoFilterColorButtonHeight,
                    Padding = new Thickness(8, 0),
                    Margin = new Thickness(0, 0, 6, 6),
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
                                Text = option.Kind == AutoFilterColorFilterKind.NoFill
                                    ? UiText.Get("AutoFilter_NoFill")
                                    : ColorInputParser.FormatHexColor(option.Color!.Value),
                                FontSize = 11,
                                FontFamily = FormulaBarFontFamily,
                                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                            },
                        },
                    },
                };
                AutomationProperties.SetName(button, option.Label);
                ToolTip.SetTip(button, option.Label);
                button.Click += (_, _) => apply(option);
                swatches.Children.Add(button);
            }

            root.Children.Add(swatches);
        }
        return root;
    }

    /// <summary>
    /// WPF <c>SetMenuCommandButtonContent</c>: icon, access-text label and, for rows that open a
    /// submenu (Sort/Filter by Color, Text/Number/Date Filters), a trailing "›" cascade glyph.
    /// </summary>
    private static Control CreateAutoFilterRowContent(RibbonCommandIconKind iconKind, string label, bool hasCascade)
    {
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        var icon = AvaloniaRibbonIcons.BuildMonochrome(iconKind, 14, null, Brush(0x21, 0x21, 0x21));
        AvaloniaGrid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // WPF hosts the label in an AccessText, so the access-key marker is consumed.
        var text = new AccessText
        {
            Text = label,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(7, 0, hasCascade ? 8 : 0, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        AvaloniaGrid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (hasCascade)
        {
            var cascade = new TextBlock
            {
                Text = "›",
                FontSize = 18,
                Margin = new Thickness(0, -2, 1, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            AvaloniaGrid.SetColumn(cascade, 2);
            grid.Children.Add(cascade);
        }

        return grid;
    }

    private Button CreateAutoFilterActionButton(AutoFilterMenuItem item, Action onClick, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = CreateAutoFilterRowContent(item.IconKind, item.Label, item.ShowsContinuation),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsEnabled = isEnabled,
        };
        // WPF dims only the text of a disabled menu row; Fluent also fills the disabled button face
        // (this popup sits outside the scoped dialog resources), which shaded the unavailable rows.
        button.Resources["ButtonBackgroundDisabled"] = Brushes.Transparent;
        button.Resources["ButtonBorderBrushDisabled"] = Brushes.Transparent;
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
