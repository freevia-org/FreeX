using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SharedRibbonIcon = Free.Shared.Ribbon.Wpf.RibbonIcon;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void NormalizeRibbonCommandButtons(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var button in surface.ButtonBases)
        {
            if (button is CheckBox or RadioButton)
                continue;

            if (button.Content is not string label || string.IsNullOrWhiteSpace(label))
                continue;

            var commandName = GetRibbonButtonCommandName(button);
            var layoutKind = GetRibbonCommandLayoutKind(button, commandName, label);
            if (ShouldUsePlannedRibbonCommandWidth(button, commandName, layoutKind))
                button.Width = 0;
            ApplyRibbonCommandSize(button, layoutKind);
            if (layoutKind is RibbonCommandLayoutKind.Small)
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
            var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
            var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24;
            SetRibbonCompactWidths(button, fullWidth, compactWidth);

            button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
            button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Center;
        }
    }

    private void NormalizeRibbonSurface(bool forceLayout = false)
    {
        if (_normalizingRibbonSurface)
            return;

        _normalizingRibbonSurface = true;
        try
        {
            NormalizeStaticRibbonSurfaceForSelectedTabOnce();
            RefreshActiveDeclarativeRibbonLayout(forceLayout);
        }
        finally
        {
            _normalizingRibbonSurface = false;
        }
    }

    private void NormalizeStaticRibbonSurfaceForSelectedTabOnce()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        if (!_normalizedRibbonStaticTabs.Add(tabItem))
            return;

        PrepareRibbonTabForLayout(tabItem, forceLayout: true);
        var root = GetRibbonTabContentRoot(tabItem);
        var surface = CaptureRibbonStaticSurface(root);
        NormalizeRibbonGroupMetadata(surface);
        NormalizeRibbonCommandButtons(surface);
        NormalizeExistingRibbonIconText(surface);
        ConfigureInsertRibbonSurface(surface);
        NormalizeRibbonCommandGroups(surface);
        NormalizeRibbonMenuButtons(surface);
        AlignRibbonIconColumns(surface);
        HideRibbonScrollBars(root, surface);
        ApplyToolbarDropdownWhiteBackgrounds(surface);
        RefreshActiveDeclarativeRibbonLayout(forceLayout: true);
    }

    private void NormalizeRibbonGroupMetadata(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var group in surface.Grids)
        {
            if (!RibbonMetadata.IsRibbonGroup(group) ||
                RibbonMetadata.TryGetGroupName(group, out _))
            {
                continue;
            }

            if (TryFindStaticRibbonGroupLabel(group, out var groupName))
                RibbonMetadata.SetGroupName(group, groupName);
        }
    }

    private static bool TryFindStaticRibbonGroupLabel(Grid group, out string groupName)
    {
        foreach (var border in group.Children.OfType<Border>())
        {
            if (Grid.GetRow(border) == 1 &&
                border.Child is TextBlock groupLabel &&
                !string.IsNullOrWhiteSpace(groupLabel.Text))
            {
                groupName = groupLabel.Text.Trim();
                return true;
            }
        }

        groupName = "";
        return false;
    }

    private void HideRibbonScrollBars(DependencyObject root, RibbonStaticSurfaceSnapshot surface)
    {
        if (root is FrameworkElement element &&
            FindVisualAncestor<ScrollViewer>(element) is { } owningScrollViewer)
        {
            owningScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        }

        foreach (var scrollViewer in surface.ScrollViewers)
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

    private static RibbonCommandLayoutKind GetRibbonCommandLayoutKind(ButtonBase button, string commandName, string label)
    {
        var plannedLayout = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
        if (ShouldPreserveExplicitCompactRibbonButtonHeight(button) &&
            !ShouldPromoteExplicitCompactRibbonButton(button, commandName, plannedLayout))
        {
            return RibbonCommandLayoutKind.Small;
        }

        return plannedLayout;
    }

    private static bool ShouldPreserveExplicitCompactRibbonButtonHeight(ButtonBase button) =>
        button is FrameworkElement { Height: > 0 and <= 34 };

    private static bool ShouldPromoteExplicitCompactRibbonButton(
        ButtonBase button,
        string commandName,
        RibbonCommandLayoutKind plannedLayout)
    {
        if (plannedLayout is not (RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium) ||
            !TryGetRibbonButtonGroupCatalogId(button, out var groupCatalogId))
        {
            return false;
        }

        var normalizedCommandName = NormalizeRibbonCommandName(commandName);
        return groupCatalogId switch
        {
            "InsertTablesGroup" =>
                normalizedCommandName is "pivottable" or
                    "recommended pivottables" or
                    "table",
            "DrawIllustrationsGroup" =>
                normalizedCommandName is "pictures" or
                    "shapes",
            "InsertChartsGroup" =>
                normalizedCommandName is "recommended charts" or
                    "recommended chart",
            "InsertSparklinesGroup" =>
                normalizedCommandName is "line sparkline" or
                    "column sparkline" or
                    "win/loss sparkline",
            "InsertFiltersGroup" =>
                normalizedCommandName is "insert slicer" or
                    "insert timeline",
            "InsertLinksGroup" =>
                normalizedCommandName is "insert link",
            "InsertCommentsGroup" =>
                normalizedCommandName is "comment",
            "InsertTextGroup" =>
                normalizedCommandName is "text box" or
                    "header & footer",
            "InsertSymbolsGroup" =>
                normalizedCommandName is "symbol",
            "ReviewProofingGroup" =>
                normalizedCommandName is "spelling" or "workbook statistics",
            "ReviewAccessibilityGroup" =>
                normalizedCommandName is "check accessibility",
            "ReviewProtectGroup" =>
                normalizedCommandName is "protect sheet" or
                    "protect workbook" or
                    "allow users to edit ranges" or
                    "share workbook",
            "ViewWorkbookViewsGroup" =>
                normalizedCommandName is "normal" or
                    "page break preview" or
                    "page layout" or
                    "custom views",
            "ViewZoomGroup" =>
                normalizedCommandName is "zoom" or "100%" or "zoom to selection",
            "PageLayoutThemesGroup" =>
                normalizedCommandName is "themes" or
                    "theme colors" or
                    "theme fonts" or
                    "theme effects",
            "PageLayoutScaleToFitGroup" =>
                normalizedCommandName is "scale to fit",
            "DrawArrangeGroup" =>
                normalizedCommandName is "bring forward" or
                    "send backward" or
                    "selection pane" or
                    "rotate object" or
                    "object size",
            "FormulasFunctionLibraryGroup" =>
                normalizedCommandName is "insert function" or "autosum",
            "FormulasDefinedNamesGroup" =>
                normalizedCommandName is "name manager" or
                    "define name" or
                    "use in formula" or
                    "create from selection",
            "FormulasCalculationGroup" =>
                normalizedCommandName is "calculate now" or
                    "calculate sheet" or
                    "calculation options",
            "DataGetTransformGroup" =>
                normalizedCommandName is "get data",
            "DataQueriesConnectionsGroup" =>
                normalizedCommandName is "refresh all",
            "DataToolsGroup" =>
                normalizedCommandName is "text to columns" or
                    "flash fill" or
                    "remove duplicates" or
                    "data validation" or
                    "consolidate",
            "DataForecastGroup" =>
                normalizedCommandName is "what-if analysis" or
                    "forecast sheet",
            "DataOutlineGroup" =>
                normalizedCommandName is "group" or
                    "ungroup" or
                    "subtotal" or
                    "collapse group" or
                    "expand group",
            "HelpHelpGroup" =>
                normalizedCommandName is "help online" or
                    "feedback" or
                    "copy diagnostics" or
                    "check for updates" or
                    "about freex" or
                    "legal notices",
            _ => false
        };
    }

    private static bool TryGetRibbonButtonGroupCatalogId(ButtonBase button, out string catalogId)
    {
        var current = button as DependencyObject;
        while (current is not null)
        {
            if (RibbonMetadata.IsRibbonGroup(current) &&
                RibbonMetadata.TryGetCatalogId(current, out catalogId))
            {
                return true;
            }

            current = GetRibbonTreeParent(current);
        }

        catalogId = "";
        return false;
    }

    private static DependencyObject? GetRibbonTreeParent(DependencyObject element) =>
        TryGetVisualParent(element) ?? LogicalTreeHelper.GetParent(element);

    private static bool ShouldUsePlannedRibbonCommandWidth(
        ButtonBase button,
        string commandName,
        RibbonCommandLayoutKind layoutKind) =>
        ShouldPreserveExplicitCompactRibbonButtonHeight(button) &&
        ShouldPromoteExplicitCompactRibbonButton(button, commandName, layoutKind);

    private static DependencyObject? TryGetVisualParent(DependencyObject element)
    {
        try
        {
            return VisualTreeHelper.GetParent(element);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string NormalizeRibbonCommandName(string? commandName) =>
        commandName?.Trim().ToLowerInvariant() ?? "";

    private static IEnumerable<DependencyObject> EnumerateRibbonStaticDescendants(DependencyObject root) =>
        EnumerateVisualDescendants(root)
            .Concat(EnumerateLogicalDescendants(root))
            .Distinct();

    private static RibbonStaticSurfaceSnapshot CaptureRibbonStaticSurface(DependencyObject root)
    {
        var descendants = EnumerateRibbonStaticDescendants(root).ToList();
        return new RibbonStaticSurfaceSnapshot(descendants);
    }

    private sealed class RibbonStaticSurfaceSnapshot
    {
        public RibbonStaticSurfaceSnapshot(IReadOnlyList<DependencyObject> descendants)
        {
            Descendants = descendants;
            ButtonBases = descendants.OfType<ButtonBase>().ToList();
            Buttons = descendants.OfType<Button>().ToList();
            Grids = descendants.OfType<Grid>().ToList();
            StackPanels = descendants.OfType<StackPanel>().ToList();
            ComboBoxes = descendants.OfType<ComboBox>().ToList();
            ScrollViewers = descendants.OfType<ScrollViewer>().ToList();
        }

        public IReadOnlyList<DependencyObject> Descendants { get; }
        public IReadOnlyList<ButtonBase> ButtonBases { get; }
        public IReadOnlyList<Button> Buttons { get; }
        public IReadOnlyList<Grid> Grids { get; }
        public IReadOnlyList<StackPanel> StackPanels { get; }
        public IReadOnlyList<ComboBox> ComboBoxes { get; }
        public IReadOnlyList<ScrollViewer> ScrollViewers { get; }
    }

    private void NormalizeRibbonSurfaceAfterTabSelection()
    {
        PrepareSelectedRibbonTabForLayout();
        NormalizeRibbonSurface(forceLayout: true);
        UpdateActiveRibbonLayoutBeforeFirstFrame();
    }

    private void ChangeRibbonSelectionWithoutTabNormalization(Action changeSelection)
    {
        var previous = _suppressRibbonSelectionChangedNormalization;
        _suppressRibbonSelectionChangedNormalization = true;
        try
        {
            changeSelection();
        }
        finally
        {
            _suppressRibbonSelectionChangedNormalization = previous;
        }
    }

    private void NormalizeRibbonSurfaceAfterResize()
    {
        RefreshActiveDeclarativeRibbonLayout(forceLayout: false);
    }

    private void CompleteRibbonResizeLayout()
    {
        RefreshActiveDeclarativeRibbonLayout(forceLayout: true);
    }

    private void RefreshActiveDeclarativeRibbonLayout(bool forceLayout)
    {
        var panel = GetActiveDeclarativeRibbonPanel();
        if (panel is null)
            return;

        panel.InvalidateMeasure();
        if (forceLayout)
            panel.UpdateLayout();
    }

    private Free.Shared.Ribbon.Wpf.RibbonAdaptivePanel? GetActiveDeclarativeRibbonPanel()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return null;

        var root = GetRibbonTabContentRoot(tabItem);
        return root as Free.Shared.Ribbon.Wpf.RibbonAdaptivePanel ??
            EnumerateVisualDescendants(root)
                .Concat(EnumerateLogicalDescendants(root))
                .OfType<Free.Shared.Ribbon.Wpf.RibbonAdaptivePanel>()
                .FirstOrDefault();
    }

    private void PrepareSelectedRibbonTabForLayout()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        PrepareRibbonTabForLayout(tabItem);
    }

    private static void PrepareRibbonTabForLayout(TabItem tabItem, bool forceLayout = false)
    {
        tabItem.ApplyTemplate();
        if (tabItem.Content is FrameworkElement content)
        {
            content.ApplyTemplate();
            UpdateRibbonLayoutIfNeeded(content, force: forceLayout);
        }
    }

    private static bool UpdateRibbonLayoutIfNeeded(FrameworkElement element, bool force = false)
    {
        if (force ||
            !element.IsMeasureValid ||
            !element.IsArrangeValid ||
            (element.IsVisible && (element.ActualWidth <= 0 || element.ActualHeight <= 0)))
        {
            element.UpdateLayout();
            return true;
        }

        return false;
    }

    private void UpdateActiveRibbonLayoutBeforeFirstFrame()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        if (GetRibbonTabContentRoot(tabItem) is FrameworkElement content)
        {
            content.ApplyTemplate();
            UpdateRibbonLayoutIfNeeded(content);
        }
    }

    private static DependencyObject GetRibbonTabContentRoot(TabItem tabItem) =>
        tabItem.Content as DependencyObject ?? tabItem;

    private static bool IsRibbonCollapsedGroupButton(FrameworkElement element) =>
        RibbonMetadata.IsCollapsedGroupButton(element);

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private void ConfigureInsertRibbonSurface(RibbonStaticSurfaceSnapshot surface)
    {
        if (RibbonTabs?.SelectedItem is not TabItem selectedTab ||
            !string.Equals(selectedTab.Header?.ToString(), "Insert", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var button in surface.Buttons)
        {
            if (RibbonMetadata.IsCollapsedGroupButton(button))
                continue;

            var title = GetRibbonButtonCommandName(button);
            var groupName = FindRibbonOwningGroupName(button);
            if (string.Equals(title, groupName, StringComparison.Ordinal))
                continue;

            if ((string.Equals(groupName, "Charts", StringComparison.Ordinal) &&
                 !RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title)) ||
                RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static string FindRibbonOwningGroupName(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (RibbonMetadata.TryGetGroupName(current, out var groupName))
            {
                return groupName;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return "";
    }

    private static string GetRibbonButtonCommandName(ButtonBase button)
    {
        if (RibbonMetadata.TryGetCommandName(button, out var commandName))
            return commandName;

        return GetRibbonButtonTitleOrLabel(button);
    }

    private static string GetRibbonButtonTitleOrLabel(ButtonBase button)
    {
        var title = RibbonTooltip.GetTitle(button);
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        var label = FindRibbonContentLabel(button.Content);

        return label ?? "";
    }

    private static string GetRibbonButtonDisplayLabel(ButtonBase button)
    {
        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        if (FindRibbonContentLabel(button.Content) is { } label)
            return label;

        var title = RibbonTooltip.GetTitle(button);
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        return RibbonMetadata.TryGetCommandName(button, out var commandName) ? commandName : "";
    }

    private static string? FindRibbonContentLabel(object? content)
    {
        if (content is TextBlock textBlock &&
            RibbonMetadata.IsCommandLabel(textBlock) &&
            !string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return textBlock.Text.Trim();
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (FindRibbonContentLabel(child) is { } label)
                    return label;
            }
        }

        if (content is ContentControl contentControl &&
            !ReferenceEquals(contentControl.Content, content))
        {
            return FindRibbonContentLabel(contentControl.Content);
        }

        return null;
    }

    private static void SetRibbonCommandButtonLabel(ButtonBase button, string label)
    {
        if (button.Content is string)
        {
            button.Content = label;
            return;
        }

        if (TrySetRibbonContentLabel(button.Content, label))
            return;

        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            commandName = label;

        var layoutKind = GetRibbonCommandLayoutKind(button, commandName, label);
        button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
        button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
    }

    private static bool TrySetRibbonContentLabel(object? content, string label)
    {
        switch (content)
        {
            case TextBlock textBlock when RibbonMetadata.IsCommandLabel(textBlock):
                textBlock.Text = label;
                return true;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (TrySetRibbonContentLabel(child, label))
                        return true;
                }

                return false;
            case Decorator decorator:
                return TrySetRibbonContentLabel(decorator.Child, label);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return TrySetRibbonContentLabel(contentControl.Content, label);
            default:
                return false;
        }
    }

    /// <summary>Reads the current label text of a rendered ribbon command button (by CommandName),
    /// handling both plain-string content and the renderer's icon+label content tree.</summary>
    private string? GetRenderedRibbonCommandLabel(string commandName)
    {
        if (FindRenderedRibbonControl(commandName) is not ButtonBase button)
            return null;

        return button.Content as string
            ?? (TryGetRibbonContentLabel(button.Content, out var label) ? label : null);
    }

    private static bool TryGetRibbonContentLabel(object? content, out string label)
    {
        switch (content)
        {
            case TextBlock textBlock when RibbonMetadata.IsCommandLabel(textBlock):
                label = textBlock.Text;
                return true;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (TryGetRibbonContentLabel(child, out label))
                        return true;
                }

                break;
            case Decorator decorator:
                return TryGetRibbonContentLabel(decorator.Child, out label);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return TryGetRibbonContentLabel(contentControl.Content, out label);
        }

        label = string.Empty;
        return false;
    }

    private void AlignRibbonIconColumns(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var stack in surface.StackPanels)
        {
            if (RibbonMetadata.TryGetCommandContentLayout(stack, out _))
                continue;

            if (stack.Orientation != Orientation.Horizontal || stack.Children.Count < 2)
                continue;

            TextBlock? label = null;
            foreach (var child in stack.Children)
            {
                if (child is not TextBlock textBlock || !RibbonMetadata.IsCommandLabel(textBlock))
                    continue;

                label = textBlock;
                break;
            }

            if (label is null)
                continue;

            var labelIndex = stack.Children.IndexOf(label);
            var icon = stack.Children
                .OfType<FrameworkElement>()
                .Take(labelIndex >= 0 ? labelIndex : stack.Children.Count)
                .LastOrDefault(element => !ReferenceEquals(element, label));
            if (icon is null)
                continue;

            if (FindVisualAncestor<ButtonBase>(stack) is null)
                continue;

            if (icon is not Image)
                icon.Width = Math.Max(icon.Width is > 0 ? icon.Width : 0, 18);
            icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, icon.Margin.Top, 4, icon.Margin.Bottom);
            label.MinWidth = Math.Max(label.MinWidth, 84);
            label.FontSize = Math.Max(label.FontSize, 12);
            label.TextTrimming = TextTrimming.None;
            label.TextWrapping = TextWrapping.NoWrap;
            stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }
    }

    private void NormalizeExistingRibbonIconText(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var button in surface.ButtonBases)
        {
            // The Home-panel compact normalizers are dead under the declarative ribbon: Home is now
            // rendered by RibbonWpfRenderer with final sizing, so no live button is a descendant of the
            // (detached, hidden) HomeRibbonPanel backplane stub. Only the static-surface normalizer runs.
            if (TryNormalizeStaticRibbonCommandButton(button))
                continue;

            var tall = button is FrameworkElement element && element.Height >= 46;
            ReplaceRibbonGlyphIcons(button.Content, button, tall);
            NormalizeRibbonButtonSizeForCommandIcons(button, tall);
            foreach (var textBlock in EnumerateRibbonTextContent(button.Content))
            {
                if (RibbonMetadata.IsCommandLabel(textBlock))
                {
                    textBlock.FontSize = 12;
                    textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    if (tall)
                    {
                        textBlock.TextTrimming = TextTrimming.None;
                        textBlock.TextWrapping = TextWrapping.Wrap;
                        textBlock.MaxWidth = Math.Max(textBlock.MaxWidth, 124);
                        textBlock.LineHeight = 14;
                        textBlock.TextAlignment = TextAlignment.Center;
                        textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    }
                    else
                    {
                        textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                    }

                    continue;
                }

                var isIcon = RibbonMetadata.IsCommandIcon(textBlock);
                if (!isIcon)
                    continue;

                RibbonMetadata.SetRole(textBlock, RibbonMetadataRole.CommandIcon);
                textBlock.FontSize = tall ? 22 : Math.Max(12, textBlock.FontSize);
                textBlock.Width = tall ? Math.Max(24, textBlock.Width) : Math.Max(16, textBlock.Width);
                textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                textBlock.TextAlignment = TextAlignment.Center;
            }
        }
    }

    private bool TryNormalizeStaticRibbonCommandButton(ButtonBase button)
    {
        if (RibbonTabs is null ||
            IsRibbonCollapsedGroupButton(button) ||
            IsRibbonCommandContent(button.Content) ||
            (!ContainsUnreplacedRibbonIcon(button.Content) &&
             !ContainsRibbonCommandLabel(button.Content)))
        {
            return false;
        }

        var hadUnreplacedIcon = ContainsUnreplacedRibbonIcon(button.Content);
        var hadRibbonCommandLabel = ContainsRibbonCommandLabel(button.Content);
        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var label = GetRibbonButtonDisplayLabel(button);
        var plannedLayout = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
        var layoutKind = IsFixedHeightIconOnlyRibbonButton(button, hadUnreplacedIcon, hadRibbonCommandLabel) ||
                         (!hadUnreplacedIcon &&
                          hadRibbonCommandLabel &&
                          button.Height is > 0 and <= 34 &&
                          !ShouldPromoteExplicitCompactRibbonButton(button, commandName, plannedLayout))
            ? RibbonCommandLayoutKind.Small
            : plannedLayout;
        if (ShouldUsePlannedRibbonCommandWidth(button, commandName, layoutKind))
            button.Width = 0;
        ApplyRibbonCommandSize(button, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
        {
            button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
            if (!hadUnreplacedIcon && hadRibbonCommandLabel)
                button.Width = Math.Max(button.Width, GetIconLabelRowRibbonCommandWidth(label));
        }
        SetRibbonCompactWidths(
            button,
            button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64),
            layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24);

        button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
        if (!hadUnreplacedIcon && hadRibbonCommandLabel && button.Content is DependencyObject contentRoot)
        {
            foreach (var textBlock in EnumerateVisualDescendants(contentRoot)
                         .Concat(EnumerateLogicalDescendants(contentRoot))
                         .OfType<TextBlock>()
                         .Distinct()
                         .Where(RibbonMetadata.IsCommandLabel))
            {
                textBlock.Uid = "RibbonCompactRowLabel";
                textBlock.FontSize = 12;
            }
        }

        if (layoutKind is RibbonCommandLayoutKind.Small)
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        return true;
    }

    private static bool IsRibbonCommandContent(object? content)
    {
        return content is DependencyObject element &&
               RibbonMetadata.TryGetCommandContentLayout(element, out _);
    }

    private static bool IsFixedHeightIconOnlyRibbonButton(
        ButtonBase button,
        bool hadUnreplacedIcon,
        bool hadRibbonCommandLabel)
    {
        return hadUnreplacedIcon &&
               !hadRibbonCommandLabel &&
               button is FrameworkElement { Height: > 0 and <= 34 };
    }

    private static bool ContainsRibbonCommandLabel(object? content)
    {
        switch (content)
        {
            case TextBlock textBlock:
                return RibbonMetadata.IsCommandLabel(textBlock) &&
                       !string.IsNullOrWhiteSpace(textBlock.Text);
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsRibbonCommandLabel);
            case Decorator decorator:
                return ContainsRibbonCommandLabel(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsRibbonCommandLabel(contentControl.Content);
            default:
                return false;
        }
    }

    private static void NormalizeRibbonButtonSizeForCommandIcons(ButtonBase button, bool tall)
    {
        if (button is not FrameworkElement element || !ContainsRibbonCommandIcon(button.Content))
            return;

        if (tall)
        {
            var tallLabel = GetRibbonButtonDisplayLabel(button);
            element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, GetLargeRibbonCommandWidth(tallLabel));
            element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 76);
            SetRibbonCompactWidths(button, element.Width, 38);
            return;
        }

        var label = FindRibbonContentLabel(button.Content);
        if (string.IsNullOrWhiteSpace(label))
            return;

        var minWidth = label.Length switch
        {
            <= 3 => 58,
            <= 6 => 66,
            <= 10 => 92,
            <= 14 => 126,
            <= 20 => 164,
            <= 28 => 198,
            _ => Math.Min(220, 48 + label.Length * 6.4)
        };

        element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, minWidth);
        element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 24);
        SetRibbonCompactWidths(button, element.Width, 24);
    }

    private static void SetRibbonCompactWidths(ButtonBase button, double fullWidth, double compactWidth)
    {
        RibbonMetadata.SetCompactWidths(button, fullWidth, compactWidth);
    }

    private static bool ContainsRibbonCommandIcon(object? content)
    {
        switch (content)
        {
            case FrameworkElement element when RibbonMetadata.IsCommandIcon(element):
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsRibbonCommandIcon);
            case Decorator decorator:
                return ContainsRibbonCommandIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsRibbonCommandIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static bool ContainsUnreplacedRibbonIcon(object? content)
    {
        switch (content)
        {
            case RibbonIcon or SharedRibbonIcon:
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsUnreplacedRibbonIcon);
            case Decorator decorator:
                return ContainsUnreplacedRibbonIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsUnreplacedRibbonIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static void ReplaceRibbonGlyphIcons(object? content, ButtonBase owner, bool tall)
    {
        switch (content)
        {
            case null:
                return;
            case RibbonIcon ribbonIcon:
                owner.Content = CreateStaticRibbonCommandIcon(owner, ribbonIcon, tall);
                return;
            case SharedRibbonIcon ribbonIcon:
                owner.Content = CreateStaticRibbonCommandIcon(owner, ribbonIcon, tall);
                return;
            case TextBlock textBlock when IsRibbonIconTextBlock(textBlock):
                owner.Content = CreateStaticRibbonVectorIcon(owner, textBlock, tall);
                return;
            case Panel panel:
                for (var i = 0; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is RibbonIcon childRibbonIcon)
                    {
                        var replacement = CreateStaticRibbonCommandIcon(owner, childRibbonIcon, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    if (panel.Children[i] is SharedRibbonIcon childSharedRibbonIcon)
                    {
                        var replacement = CreateStaticRibbonCommandIcon(owner, childSharedRibbonIcon, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    if (panel.Children[i] is TextBlock childText && IsRibbonIconTextBlock(childText))
                    {
                        var replacement = CreateStaticRibbonVectorIcon(owner, childText, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    ReplaceRibbonGlyphIcons(panel.Children[i], owner, tall);
                }

                return;
            case Decorator decorator:
                if (decorator.Child is RibbonIcon decoratorRibbonIcon)
                    decorator.Child = CreateStaticRibbonCommandIcon(owner, decoratorRibbonIcon, tall);
                else if (decorator.Child is SharedRibbonIcon decoratorSharedRibbonIcon)
                    decorator.Child = CreateStaticRibbonCommandIcon(owner, decoratorSharedRibbonIcon, tall);
                else if (decorator.Child is TextBlock decoratorText && IsRibbonIconTextBlock(decoratorText))
                    decorator.Child = CreateStaticRibbonVectorIcon(owner, decoratorText, tall);
                else
                    ReplaceRibbonGlyphIcons(decorator.Child, owner, tall);
                return;
            case ContentControl contentControl when !ReferenceEquals(contentControl, owner):
                if (contentControl.Content is RibbonIcon contentRibbonIcon)
                    contentControl.Content = CreateStaticRibbonCommandIcon(owner, contentRibbonIcon, tall);
                else if (contentControl.Content is SharedRibbonIcon contentSharedRibbonIcon)
                    contentControl.Content = CreateStaticRibbonCommandIcon(owner, contentSharedRibbonIcon, tall);
                else if (contentControl.Content is TextBlock contentText && IsRibbonIconTextBlock(contentText))
                    contentControl.Content = CreateStaticRibbonVectorIcon(owner, contentText, tall);
                else
                    ReplaceRibbonGlyphIcons(contentControl.Content, owner, tall);
                return;
        }
    }

    private static bool IsRibbonIconTextBlock(TextBlock textBlock)
    {
        return RibbonMetadata.IsCommandIcon(textBlock);
    }

    private static FrameworkElement CreateStaticRibbonCommandIcon(ButtonBase owner, RibbonIcon source, bool tall)
    {
        var commandName = !string.IsNullOrWhiteSpace(source.CommandName)
            ? source.CommandName.Trim()
            : source.Kind == RibbonCommandIconKind.Previous
            ? "Back to workbook"
            : GetStaticRibbonIconCommandName(owner, source.Kind.ToString());
        var fallbackIcon = new RibbonCommandIcon(source.Kind);
        var iconSize = IsWhiteBrush(source.Foreground) ? source.IconSize : tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(
            commandName,
            fallbackIcon,
            iconSize,
            source.Foreground ?? owner.Foreground);
        RibbonMetadata.SetRole(commandIcon, RibbonMetadataRole.CommandIcon);
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static FrameworkElement CreateStaticRibbonCommandIcon(ButtonBase owner, SharedRibbonIcon source, bool tall)
    {
        var commandName = !string.IsNullOrWhiteSpace(source.CommandName)
            ? source.CommandName.Trim()
            : source.Kind == RibbonCommandIconKind.Previous
            ? "Back to workbook"
            : GetStaticRibbonIconCommandName(owner, source.Kind.ToString());
        var fallbackIcon = new RibbonCommandIcon(source.Kind);
        var iconSize = IsWhiteBrush(source.Foreground) ? source.IconSize : tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(
            commandName,
            fallbackIcon,
            iconSize,
            source.Foreground ?? owner.Foreground);
        RibbonMetadata.SetRole(commandIcon, RibbonMetadataRole.CommandIcon);
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static FrameworkElement CreateStaticRibbonVectorIcon(ButtonBase owner, TextBlock source, bool tall)
    {
        var commandName = GetStaticRibbonIconCommandName(owner, source.Text);
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var iconSize = tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, source.Foreground);
        RibbonMetadata.SetRole(commandIcon, RibbonMetadataRole.CommandIcon);
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static string GetStaticRibbonIconCommandName(ButtonBase owner, string fallback)
    {
        if (RibbonMetadata.TryGetCommandName(owner, out var commandName))
            return commandName;

        var title = owner is FrameworkElement element
            ? RibbonTooltip.GetTitle(element)
            : null;
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        if (!string.IsNullOrWhiteSpace(owner.Name))
        {
            var name = owner.Name;
            foreach (var suffix in new[] { "Button", "Btn" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            return name;
        }

        return fallback switch
        {
            nameof(RibbonCommandIconKind.WindowMinimize) => "Minimize",
            nameof(RibbonCommandIconKind.WindowMaximize) => "Maximize",
            nameof(RibbonCommandIconKind.WindowClose) => "Close",
            nameof(RibbonCommandIconKind.Previous) => "Back to workbook",
            _ => fallback
        };
    }

    private static bool IsWhiteBrush(Brush brush)
    {
        return brush is SolidColorBrush solid &&
               solid.Color.R >= 245 &&
               solid.Color.G >= 245 &&
               solid.Color.B >= 245;
    }


    private static IEnumerable<TextBlock> EnumerateRibbonTextContent(object? content)
    {
        if (content is TextBlock textBlock)
        {
            yield return textBlock;
            yield break;
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var text in EnumerateRibbonTextContent(child))
                    yield return text;
            }
        }
        else if (content is ContentControl contentControl &&
                 !ReferenceEquals(contentControl.Content, content))
        {
            foreach (var text in EnumerateRibbonTextContent(contentControl.Content))
                yield return text;
        }
        else if (content is Decorator decorator)
        {
            foreach (var text in EnumerateRibbonTextContent(decorator.Child))
                yield return text;
        }
    }

    private void ApplyToolbarDropdownWhiteBackgrounds(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var comboBox in surface.ComboBoxes)
        {
            comboBox.Background = Brushes.White;
            comboBox.Foreground = Brushes.Black;
            comboBox.Resources[SystemColors.WindowBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.ControlBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.MenuBrushKey] = Brushes.White;
            comboBox.DropDownOpened -= ToolbarComboBox_DropDownOpened;
            comboBox.DropDownOpened += ToolbarComboBox_DropDownOpened;
        }
    }

    private static void ToolbarComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        comboBox.Dispatcher.BeginInvoke((Action)(() =>
        {
            comboBox.ApplyTemplate();
            if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup ||
                popup.Child is not DependencyObject popupRoot)
            {
                return;
            }

            ForceDropdownWhite(popupRoot);
        }));
    }

    private static void ForceDropdownWhite(DependencyObject root)
    {
        if (root is Control control)
        {
            control.Background = Brushes.White;
            control.Foreground = Brushes.Black;
        }
        else if (root is Border border)
        {
            border.Background = Brushes.White;
        }
        else if (root is Panel panel)
        {
            panel.Background = Brushes.White;
        }

        if (root is ComboBoxItem item)
        {
            item.Background = Brushes.White;
            item.Foreground = Brushes.Black;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ForceDropdownWhite(VisualTreeHelper.GetChild(root, i));
    }

    private static FrameworkElement CreateRibbonCommandContent(string commandName, string label, RibbonCommandLayoutKind layoutKind)
    {
        var tall = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium;
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconSize = layoutKind == RibbonCommandLayoutKind.Large ? 32 : 22;
        var slotSize = layoutKind == RibbonCommandLayoutKind.Large ? 34 : 24;
        var iconSlot = new Border
        {
            Width = slotSize,
            Height = slotSize,
            CornerRadius = tall ? new CornerRadius(3) : new CornerRadius(2),
            Background = slotBackground,
            BorderBrush = slotBorder,
            BorderThickness = slotBorder is null ? new Thickness(0) : new Thickness(1),
            Child = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush),
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = tall ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 5, 0)
        };
        RibbonMetadata.SetRole(iconSlot, RibbonMetadataRole.CommandIcon);

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = tall ? 124 : double.PositiveInfinity,
            TextTrimming = tall ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            HorizontalAlignment = tall ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextAlignment = tall ? TextAlignment.Center : TextAlignment.Left,
            LineHeight = tall ? 14 : double.NaN
        };
        RibbonMetadata.SetRole(labelBlock, RibbonMetadataRole.CommandLabel);

        var contentLayout = layoutKind == RibbonCommandLayoutKind.Large
            ? RibbonCommandContentLayout.Large
            : layoutKind == RibbonCommandLayoutKind.Medium
                ? RibbonCommandContentLayout.Medium
                : RibbonCommandContentLayout.Small;

        if (tall)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    iconSlot,
                    labelBlock
                }
            };
            RibbonMetadata.SetCommandContentLayout(stack, contentLayout);
            return stack;
        }

        var compactGrid = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RibbonMetadata.SetCommandContentLayout(compactGrid, contentLayout);
        var iconColumn = new ColumnDefinition { Width = new GridLength(slotSize) };
        var spacerColumn = new ColumnDefinition { Width = new GridLength(5) };
        var labelColumn = new ColumnDefinition { Width = GridLength.Auto };
        RibbonMetadata.SetRole(spacerColumn, RibbonMetadataRole.CommandSpacer);
        compactGrid.ColumnDefinitions.Add(iconColumn);
        compactGrid.ColumnDefinitions.Add(spacerColumn);
        compactGrid.ColumnDefinitions.Add(labelColumn);

        iconSlot.Margin = new Thickness(0);
        labelBlock.Margin = new Thickness(0);
        Grid.SetColumn(iconSlot, 0);
        Grid.SetColumn(labelBlock, 2);
        compactGrid.Children.Add(iconSlot);
        compactGrid.Children.Add(labelBlock);
        return compactGrid;
    }

    private static FrameworkElement CreateRibbonIconOnlyContent(string commandName, double iconSize)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (_, _, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconElement = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush);
        RibbonMetadata.SetRole(iconElement, RibbonMetadataRole.CommandIcon);

        var grid = new Grid
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children = { iconElement }
        };
        RibbonMetadata.SetCommandContentLayout(grid, RibbonCommandContentLayout.IconOnly);
        return grid;
    }

    private static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) GetRibbonIconAccentBrushes(
        RibbonCommandIconAccent accent)
    {
        static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) Glyph(byte r, byte g, byte b) =>
            (Brushes.Transparent, null, BrushFromRgb(r, g, b));

        return accent switch
        {
            RibbonCommandIconAccent.Green => Glyph(23, 50, 77),
            RibbonCommandIconAccent.Chart => Glyph(47, 84, 150),
            RibbonCommandIconAccent.Data => Glyph(0, 92, 135),
            RibbonCommandIconAccent.Theme => Glyph(85, 35, 125),
            RibbonCommandIconAccent.Fill => Glyph(116, 88, 0),
            RibbonCommandIconAccent.Color => Glyph(150, 0, 0),
            RibbonCommandIconAccent.Border => Glyph(31, 31, 31),
            RibbonCommandIconAccent.Warning => Glyph(138, 91, 0),
            RibbonCommandIconAccent.Protect => Glyph(23, 50, 77),
            RibbonCommandIconAccent.Help => Glyph(47, 84, 150),
            _ => (Brushes.Transparent, null, Brushes.Black)
        };
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static void ApplyRibbonCommandSize(ButtonBase button, RibbonCommandLayoutKind layoutKind)
    {
        switch (layoutKind)
        {
            case RibbonCommandLayoutKind.Large:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetLargeRibbonCommandWidth(GetRibbonButtonDisplayLabel(button)));
                button.Height = 76;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            case RibbonCommandLayoutKind.Medium:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 74);
                button.Height = 48;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            default:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 72);
                button.Height = Math.Max(button.Height is > 0 ? button.Height : 0, 24);
                button.Padding = new Thickness(4, 2, 4, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                break;
        }
    }

    private void NormalizeRibbonCommandGroups(RibbonStaticSurfaceSnapshot surface)
    {
        NormalizeRibbonCommandColumns(surface);
        NormalizeRibbonGridCommandColumns(surface);
    }

    private void NormalizeRibbonCommandColumns(RibbonStaticSurfaceSnapshot surface)
    {
        var panels = surface.StackPanels
            .Where(panel => panel != HomeRibbonPanel &&
                            panel.Orientation == Orientation.Vertical &&
                            FindVisualAncestor<ButtonBase>(panel) is null)
            .ToList();

        foreach (var panel in panels)
        {
            NormalizeNestedRibbonRowColumns(panel);

            var directButtons = panel.Children.OfType<ButtonBase>().Where(IsVisibleLabeledRibbonCommandButton).ToList();
            if (directButtons.Count < 2)
                continue;

            foreach (var button in directButtons)
                NormalizeDenseRibbonColumnButton(button);

            var columnWidth = directButtons.Max(button => button.Width is > 0 ? button.Width : button.DesiredSize.Width);
            if (directButtons.Count <= 3)
            {
                foreach (var button in directButtons)
                    ApplyDenseRibbonColumnWidth(button, columnWidth);

                continue;
            }

            var parent = VisualTreeHelper.GetParent(panel) ?? LogicalTreeHelper.GetParent(panel);
            if (parent is not Panel parentPanel)
                continue;

            var index = parentPanel.Children.IndexOf(panel);
            if (index < 0)
                continue;

            var row = Grid.GetRow(panel);
            var column = Grid.GetColumn(panel);
            var rowSpan = Grid.GetRowSpan(panel);
            var columnSpan = Grid.GetColumnSpan(panel);
            var margin = panel.Margin;
            var verticalAlignment = panel.VerticalAlignment;
            var horizontalAlignment = panel.HorizontalAlignment;

            panel.Children.Clear();
            var grid = new UniformGrid
            {
                Rows = 3,
                Columns = (int)Math.Ceiling(directButtons.Count / 3.0),
                Margin = margin,
                VerticalAlignment = verticalAlignment,
                HorizontalAlignment = horizontalAlignment
            };

            Grid.SetRow(grid, row);
            Grid.SetColumn(grid, column);
            Grid.SetRowSpan(grid, rowSpan);
            Grid.SetColumnSpan(grid, columnSpan);

            foreach (var button in directButtons)
            {
                ApplyDenseRibbonColumnWidth(button, columnWidth);
                grid.Children.Add(button);
            }

            parentPanel.Children.RemoveAt(index);
            parentPanel.Children.Insert(index, grid);
        }
    }

    private static void NormalizeNestedRibbonRowColumns(StackPanel panel)
    {
        var rows = panel.Children
            .OfType<StackPanel>()
            .Where(row => row.Orientation == Orientation.Horizontal &&
                          FindVisualAncestor<ButtonBase>(row) is null)
            .Select(row => row.Children
                .OfType<ButtonBase>()
                .Where(IsVisibleLabeledRibbonCommandButton)
                .ToList())
            .Where(buttons => buttons.Count >= 2)
            .ToList();
        if (rows.Count < 2)
            return;

        var columnCount = rows.Max(row => row.Count);
        var columnWidths = new double[columnCount];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Count; column++)
            {
                var button = row[column];
                NormalizeDenseRibbonColumnButton(button);
                columnWidths[column] = Math.Max(
                    columnWidths[column],
                    button.Width is > 0 ? button.Width : button.DesiredSize.Width);
            }
        }

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Count; column++)
                ApplyDenseRibbonColumnWidth(row[column], columnWidths[column]);
        }
    }

    private static void NormalizeRibbonGridCommandColumns(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var grid in surface.Grids)
        {
            if (RibbonMetadata.IsRibbonGroup(grid) ||
                RibbonMetadata.TryGetCommandContentLayout(grid, out _) ||
                FindVisualAncestor<ButtonBase>(grid) is not null)
            {
                continue;
            }

            var buttonsByColumn = grid.Children
                .OfType<ButtonBase>()
                .Where(IsVisibleStackedSmallRibbonCommandButton)
                .GroupBy(Grid.GetColumn)
                .Where(group => group.Count() >= 2)
                .ToList();
            foreach (var columnButtons in buttonsByColumn)
            {
                var buttons = columnButtons.ToList();
                foreach (var button in buttons)
                    NormalizeDenseRibbonColumnButton(button);

                var columnWidth = buttons.Max(button => button.Width is > 0 ? button.Width : button.DesiredSize.Width);
                foreach (var button in buttons)
                    ApplyDenseRibbonColumnWidth(button, columnWidth);
            }
        }
    }

    private static void ApplyDenseRibbonColumnWidth(ButtonBase button, double columnWidth)
    {
        button.Width = columnWidth;
        SetRibbonCompactWidths(button, columnWidth, 24);
        if (button.Content is FrameworkElement content)
        {
            content.Width = Math.Max(
                0,
                columnWidth -
                button.Padding.Left -
                button.Padding.Right -
                button.BorderThickness.Left -
                button.BorderThickness.Right);
        }
    }

    private static bool IsVisibleLabeledRibbonCommandButton(ButtonBase button) =>
        button.Visibility == Visibility.Visible &&
        FindRibbonContentLabel(button.Content) is not null;

    private static bool IsVisibleStackedSmallRibbonCommandButton(ButtonBase button)
    {
        if (!IsVisibleLabeledRibbonCommandButton(button))
            return false;

        if (button.Content is DependencyObject content &&
            RibbonMetadata.TryGetCommandContentLayout(content, out var layout))
        {
            return layout == RibbonCommandContentLayout.Small;
        }

        return button is FrameworkElement { Height: > 0 and <= 34 } &&
               ContainsRibbonCommandIcon(button.Content);
    }

    private static void NormalizeDenseRibbonColumnButton(ButtonBase button)
    {
        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return;

        var label = FindRibbonContentLabel(button.Content) ?? commandName;
        button.Height = 24;
        button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
        SetRibbonCompactWidths(button, button.Width, 24);
        button.Padding = new Thickness(4, 2, 4, 2);
        button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        button.Content = CreateRibbonCommandContent(commandName, label, RibbonCommandLayoutKind.Small);
    }

    private static double GetSmallRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 3 => 58,
            <= 6 => 72,
            <= 10 => 98,
            <= 14 => 128,
            <= 20 => 164,
            <= 28 => 198,
            _ => Math.Min(220, 48 + length * 6.4)
        };
    }

    private static double GetIconLabelRowRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return Math.Min(220, 48 + length * 6.2);
    }

    private static double GetLargeRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 5 => 62,
            <= 9 => 76,
            <= 14 => 88,
            <= 20 => 96,
            <= 28 => 112,
            _ => 124
        };
    }
}
