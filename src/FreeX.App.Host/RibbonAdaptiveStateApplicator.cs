using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeX.App.Host;

internal static class RibbonAdaptiveStateApplicator
{
    public static int ApplyStates(
        IReadOnlyList<MainWindow.RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveGroupState>? previousStates,
        double availableWidth = 0)
    {
        var changedGroupCount = 0;
        for (var i = 0; i < groupSnapshots.Count; i++)
        {
            changedGroupCount += ApplyState(
                groupSnapshots[i],
                collapsedButtons[i],
                plannedStates[i],
                previousStates is not null && i < previousStates.Count ? previousStates[i] : null,
                availableWidth);
        }

        return changedGroupCount;
    }

    public static int ApplyStateAt(
        IReadOnlyList<MainWindow.RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        int index,
        RibbonAdaptiveGroupState plannedState,
        RibbonAdaptiveGroupState? previousState,
        double availableWidth = 0)
    {
        if ((uint)index >= (uint)groupSnapshots.Count || index >= collapsedButtons.Count)
            return 0;

        return ApplyState(
            groupSnapshots[index],
            collapsedButtons[index],
            plannedState,
            previousState,
            availableWidth);
    }

    public static void SetCollapsedButtonFootprint(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprint = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(availableWidth);
        foreach (var button in collapsedButtons)
        {
            SetIfChanged(button, FrameworkElement.WidthProperty, footprint.Width);
            SetIfChanged(button, FrameworkElement.MarginProperty, footprint.Margin);
            SetIfChanged(button, Control.PaddingProperty, footprint.Padding);

            if (TryGetCollapsedRibbonButtonCaption(button, out var caption))
                ApplyCollapsedRibbonButtonCaptionFootprint(caption, footprint);

            if (TryGetCollapsedRibbonButtonTextIcon(button, out var icon))
                SetIfChanged(icon, TextBlock.FontSizeProperty, footprint.IconFontSize);
        }
    }

    public static void ApplyGroup(
        MainWindow.RibbonCompactGroupSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        foreach (var label in snapshot.CommandLabels)
            SetIfChanged(
                label,
                UIElement.VisibilityProperty,
                level == MainWindow.RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible);

        foreach (var buttonSnapshot in snapshot.Buttons)
        {
            if (buttonSnapshot.HasCompactWidths)
            {
                SetIfChanged(
                    buttonSnapshot.Button,
                    FrameworkElement.WidthProperty,
                    level switch
                    {
                        MainWindow.RibbonCompactLevel.Full => buttonSnapshot.FullWidth,
                        MainWindow.RibbonCompactLevel.SmallWithLabels => buttonSnapshot.IsLargeButton ? double.NaN : buttonSnapshot.FullWidth,
                        _ => buttonSnapshot.CompactWidth
                    });
            }

            ApplyButton(buttonSnapshot, level);
        }
    }

    public static bool ShouldUseSmallWithLabelsForIconOnlyGroup(string? catalogId) =>
        catalogId is
            "DataToolsGroup" or
            "FormulasFormulaAuditingGroup" or
            "ReviewCommentsGroup" or
            "ViewWindowGroup" or
            "TableDesignStyleOptionsGroup" or
            "PivotTableAnalyzeCalculationsGroup" or
            "PivotTableDesignStyleOptionsGroup";

    public static bool ShouldUseFullLayoutForIconOnlyGroup(string? catalogId, double availableWidth) =>
        availableWidth > 760 &&
        catalogId is "DataToolsGroup";

    public static void ApplyButton(
        MainWindow.RibbonCompactButtonSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        if (snapshot.IsCheckOrRadioButton)
        {
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
            if (snapshot.Content is not null)
                SetIfChanged(snapshot.Content, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            return;
        }

        foreach (var label in snapshot.Labels)
        {
            SetIfChanged(
                label,
                UIElement.VisibilityProperty,
                level == MainWindow.RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible);
        }

        var isSmallOrMedium = snapshot.ContentLayout is RibbonCommandContentLayout.Small or RibbonCommandContentLayout.Medium;
        if (snapshot.HasContentLayout &&
            snapshot.ContentLayout == RibbonCommandContentLayout.Small &&
            snapshot.SmallGrid is not null)
        {
            ApplySmallButtonLayout(snapshot, level);
        }

        if (!isSmallOrMedium)
        {
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);

            if (snapshot.Content is not null)
                SetIfChanged(snapshot.Content, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            foreach (var stack in snapshot.HorizontalStacks)
                SetIfChanged(stack, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        }

        if (snapshot.HasContentLayout &&
            snapshot.ContentLayout == RibbonCommandContentLayout.Large &&
            snapshot.LargeStack is not null)
        {
            ApplyLargeButtonLayout(snapshot, level);
        }
    }

    public static ColumnDefinition? GetSmallButtonSpacerColumn(Grid? contentGrid)
    {
        if (contentGrid is null)
            return null;

        foreach (ColumnDefinition column in contentGrid.ColumnDefinitions)
        {
            if (RibbonMetadata.IsCommandSpacer(column))
                return column;
        }

        return contentGrid.ColumnDefinitions.Count >= 2
            ? contentGrid.ColumnDefinitions[1]
            : null;
    }

    public static void ApplySmallButtonLayout(
        Grid contentGrid,
        ButtonBase button,
        MainWindow.RibbonCompactLevel level) =>
        ApplySmallButtonLayout(
            new MainWindow.RibbonCompactButtonSnapshot(
                button,
                button is CheckBox or RadioButton,
                contentGrid,
                hasContentLayout: true,
                RibbonCommandContentLayout.Small,
                isLargeButton: false,
                hasCompactWidths: false,
                fullWidth: 0,
                compactWidth: 0,
                [],
                [],
                contentGrid,
                GetSmallButtonSpacerColumn(contentGrid),
                null,
                null,
                null,
                null),
            level);

    public static void ApplyLargeButtonLayout(
        StackPanel contentStack,
        ButtonBase button,
        MainWindow.RibbonCompactLevel level)
    {
        Border? iconSlot = null;
        TextBlock? labelBlock = null;
        foreach (var child in contentStack.Children)
        {
            if (iconSlot is null &&
                child is Border border &&
                RibbonMetadata.IsCommandIcon(border))
            {
                iconSlot = border;
            }
            else if (labelBlock is null &&
                     child is TextBlock textBlock &&
                     RibbonMetadata.IsCommandLabel(textBlock))
            {
                labelBlock = textBlock;
            }

            if (iconSlot is not null && labelBlock is not null)
                break;
        }

        ApplyLargeButtonLayout(
            new MainWindow.RibbonCompactButtonSnapshot(
                button,
                button is CheckBox or RadioButton,
                contentStack,
                hasContentLayout: true,
                RibbonCommandContentLayout.Large,
                isLargeButton: true,
                hasCompactWidths: false,
                fullWidth: 0,
                compactWidth: 0,
                [],
                [],
                null,
                null,
                contentStack,
                iconSlot,
                iconSlot?.Child as FrameworkElement,
                labelBlock),
            level);
    }

    private static bool ShouldKeepRibbonGroupLabelsAtIconWidth(
        MainWindow.RibbonCompactGroupSnapshot snapshot,
        RibbonAdaptiveGroupState plannedState,
        double availableWidth) =>
        plannedState == RibbonAdaptiveGroupState.IconOnly &&
        availableWidth > 820 &&
        string.Equals(GetRibbonGroupName(snapshot.Group), "Tables", StringComparison.Ordinal);

    private static RibbonAdaptiveGroupState NormalizePlannedState(
        MainWindow.RibbonCompactGroupSnapshot snapshot,
        RibbonAdaptiveGroupState plannedState,
        double availableWidth)
    {
        if (plannedState != RibbonAdaptiveGroupState.IconOnly ||
            !RibbonMetadata.TryGetCatalogId(snapshot.Group, out var catalogId))
        {
            return plannedState;
        }

        if (ShouldCollapseIconOnlyGroup(catalogId, availableWidth))
            return RibbonAdaptiveGroupState.Collapsed;

        if (ShouldUseFullLayoutForIconOnlyGroup(catalogId, availableWidth))
            return RibbonAdaptiveGroupState.Full;

        return ShouldUseSmallWithLabelsForIconOnlyGroup(catalogId)
            ? RibbonAdaptiveGroupState.SmallWithLabels
            : plannedState;
    }

    private static int ApplyState(
        MainWindow.RibbonCompactGroupSnapshot groupSnapshot,
        Button collapsedButton,
        RibbonAdaptiveGroupState plannedState,
        RibbonAdaptiveGroupState? previousState,
        double availableWidth)
    {
        var normalizedPlannedState = NormalizePlannedState(groupSnapshot, plannedState, availableWidth);
        var normalizedPreviousState = previousState is not null
            ? NormalizePlannedState(groupSnapshot, previousState.Value, availableWidth)
            : (RibbonAdaptiveGroupState?)null;

        if (normalizedPreviousState is not null &&
            normalizedPreviousState == normalizedPlannedState &&
            !ShouldKeepRibbonGroupLabelsAtIconWidth(groupSnapshot, normalizedPlannedState, availableWidth))
        {
            return 0;
        }

        SetIfChanged(collapsedButton, UIElement.VisibilityProperty, Visibility.Collapsed);
        SetIfChanged(groupSnapshot.Group, UIElement.VisibilityProperty, Visibility.Visible);

        switch (normalizedPlannedState)
        {
            case RibbonAdaptiveGroupState.Full:
                ApplyGroup(groupSnapshot, MainWindow.RibbonCompactLevel.Full);
                break;
            case RibbonAdaptiveGroupState.SmallWithLabels:
                ApplyGroup(groupSnapshot, MainWindow.RibbonCompactLevel.SmallWithLabels);
                break;
            case RibbonAdaptiveGroupState.IconOnly:
                ApplyGroup(
                    groupSnapshot,
                    ShouldKeepRibbonGroupLabelsAtIconWidth(groupSnapshot, normalizedPlannedState, availableWidth)
                        ? MainWindow.RibbonCompactLevel.SmallWithLabels
                        : MainWindow.RibbonCompactLevel.IconOnly);
                break;
            case RibbonAdaptiveGroupState.Collapsed:
                SetIfChanged(groupSnapshot.Group, UIElement.VisibilityProperty, Visibility.Collapsed);
                SetIfChanged(collapsedButton, UIElement.VisibilityProperty, Visibility.Visible);
                break;
        }

        return 1;
    }

    private static bool ShouldCollapseIconOnlyGroup(string? catalogId, double availableWidth) =>
        availableWidth <= 1300 &&
        catalogId is "DataSortFilterGroup";

    private static bool TryGetCollapsedRibbonButtonCaption(Button button, out TextBlock caption)
    {
        caption = null!;
        if (button.Content is not Panel content)
            return false;

        foreach (var child in content.Children)
        {
            if (child is TextBlock textBlock &&
                RibbonMetadata.IsCommandLabel(textBlock))
            {
                caption = textBlock;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCollapsedRibbonButtonTextIcon(Button button, out TextBlock icon)
    {
        icon = null!;
        if (button.Content is not Panel content)
            return false;

        foreach (var child in content.Children)
        {
            if (child is TextBlock textBlock &&
                RibbonMetadata.IsCommandIcon(textBlock) &&
                !RibbonMetadata.IsCollapsedChevron(textBlock))
            {
                icon = textBlock;
                return true;
            }
        }

        foreach (var child in content.Children)
        {
            if (child is Border { Child: TextBlock textBlock } &&
                RibbonMetadata.IsCommandIcon(textBlock) &&
                !RibbonMetadata.IsCollapsedChevron(textBlock))
            {
                icon = textBlock;
                return true;
            }
        }

        return false;
    }

    private static void ApplyCollapsedRibbonButtonCaptionFootprint(
        TextBlock caption,
        RibbonCollapsedGroupFootprint footprint)
    {
        SetIfChanged(caption, UIElement.VisibilityProperty, footprint.CaptionVisibility);
        SetIfChanged(caption, TextBlock.FontSizeProperty, footprint.CaptionFontSize);
        SetIfChanged(caption, FrameworkElement.MaxWidthProperty, footprint.CaptionMaxWidth);
        SetIfChanged(caption, TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        SetIfChanged(caption, TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        SetIfChanged(caption, TextBlock.TextAlignmentProperty, TextAlignment.Center);
    }

    private static void ApplySmallButtonLayout(
        MainWindow.RibbonCompactButtonSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        SetIfChanged(snapshot.Button, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        if (snapshot.SmallSpacerColumn is not null)
        {
            SetIfChanged(
                snapshot.SmallSpacerColumn,
                ColumnDefinition.WidthProperty,
                level == MainWindow.RibbonCompactLevel.IconOnly
                    ? new GridLength(0)
                    : new GridLength(5));
        }

        var smallGrid = snapshot.SmallGrid!;
        if (level == MainWindow.RibbonCompactLevel.IconOnly)
        {
            SetIfChanged(smallGrid, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            // Keep the command icon aligned with 24px icon-only peers while the menu lane extends to the right.
            SetIfChanged(
                smallGrid,
                FrameworkElement.MarginProperty,
                GetSmallButtonDropdownColumn(smallGrid) is { } dropdownColumn
                    ? new Thickness(-dropdownColumn.Width.Value, 0, 0, 0)
                    : new Thickness(0));
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        }
        else
        {
            SetIfChanged(smallGrid, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            SetIfChanged(smallGrid, FrameworkElement.MarginProperty, new Thickness(0));
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        }
    }

    private static ColumnDefinition? GetSmallButtonDropdownColumn(Grid? contentGrid)
    {
        if (contentGrid is null)
            return null;

        foreach (var child in contentGrid.Children)
        {
            if (child is not FrameworkElement chevron ||
                !RibbonMetadata.IsDropdownChevron(chevron))
            {
                continue;
            }

            var columnIndex = Grid.GetColumn(chevron);
            if (columnIndex >= 0 && columnIndex < contentGrid.ColumnDefinitions.Count)
                return contentGrid.ColumnDefinitions[columnIndex];
        }

        return null;
    }

    private static void ApplyLargeButtonLayout(
        MainWindow.RibbonCompactButtonSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        if (snapshot.LargeStack is null ||
            snapshot.LargeIconSlot is null ||
            snapshot.LargeLabelBlock is null)
        {
            return;
        }

        if (level == MainWindow.RibbonCompactLevel.Full)
        {
            SetIfChanged(snapshot.LargeStack, StackPanel.OrientationProperty, Orientation.Vertical);
            SetIfChanged(snapshot.LargeStack, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            SetIfChanged(snapshot.Button, FrameworkElement.HeightProperty, 76d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.WidthProperty, 34d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.HeightProperty, 34d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));
            if (snapshot.LargeIconChild is not null)
            {
                SetIfChanged(snapshot.LargeIconChild, FrameworkElement.WidthProperty, 32d);
                SetIfChanged(snapshot.LargeIconChild, FrameworkElement.HeightProperty, 32d);
            }
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            SetIfChanged(snapshot.LargeLabelBlock, FrameworkElement.MaxWidthProperty, 96d);
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextTrimmingProperty, TextTrimming.None);
            SetIfChanged(snapshot.LargeLabelBlock, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextAlignmentProperty, TextAlignment.Center);
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        }
        else
        {
            SetIfChanged(snapshot.LargeStack, StackPanel.OrientationProperty, Orientation.Horizontal);
            SetIfChanged(snapshot.LargeStack, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            SetIfChanged(snapshot.Button, FrameworkElement.HeightProperty, 48d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.WidthProperty, 24d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.HeightProperty, 24d);
            SetIfChanged(snapshot.LargeIconSlot, FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0));
            if (snapshot.LargeIconChild is not null)
            {
                SetIfChanged(snapshot.LargeIconChild, FrameworkElement.WidthProperty, 24d);
                SetIfChanged(snapshot.LargeIconChild, FrameworkElement.HeightProperty, 24d);
            }
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            SetIfChanged(snapshot.LargeLabelBlock, FrameworkElement.MaxWidthProperty, 90d);
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            SetIfChanged(snapshot.LargeLabelBlock, FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            SetIfChanged(snapshot.LargeLabelBlock, TextBlock.TextAlignmentProperty, TextAlignment.Left);
            SetIfChanged(snapshot.Button, Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        }
    }

    private static void SetIfChanged<T>(DependencyObject target, DependencyProperty property, T value)
    {
        if (!EqualityComparer<T>.Default.Equals((T)target.GetValue(property), value))
            target.SetValue(property, value);
    }

    private static string GetRibbonGroupName(FrameworkElement group) =>
        RibbonMetadata.TryGetGroupName(group, out var groupName) ? groupName : "Commands";
}
