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
            var plannedState = NormalizePlannedState(groupSnapshots[i], plannedStates[i]);
            var previousState = previousStates is not null && i < previousStates.Count
                ? NormalizePlannedState(groupSnapshots[i], previousStates[i])
                : (RibbonAdaptiveGroupState?)null;

            if (previousState is not null &&
                previousState == plannedState &&
                !ShouldKeepRibbonGroupLabelsAtIconWidth(groupSnapshots[i], plannedState, availableWidth))
            {
                continue;
            }

            changedGroupCount++;
            collapsedButtons[i].Visibility = Visibility.Collapsed;
            groupSnapshots[i].Group.Visibility = Visibility.Visible;

            switch (plannedState)
            {
                case RibbonAdaptiveGroupState.Full:
                    ApplyGroup(groupSnapshots[i], MainWindow.RibbonCompactLevel.Full);
                    break;
                case RibbonAdaptiveGroupState.SmallWithLabels:
                    ApplyGroup(groupSnapshots[i], MainWindow.RibbonCompactLevel.SmallWithLabels);
                    break;
                case RibbonAdaptiveGroupState.IconOnly:
                    ApplyGroup(
                        groupSnapshots[i],
                        ShouldKeepRibbonGroupLabelsAtIconWidth(groupSnapshots[i], plannedStates[i], availableWidth)
                            ? MainWindow.RibbonCompactLevel.SmallWithLabels
                            : MainWindow.RibbonCompactLevel.IconOnly);
                    break;
                case RibbonAdaptiveGroupState.Collapsed:
                    groupSnapshots[i].Group.Visibility = Visibility.Collapsed;
                    collapsedButtons[i].Visibility = Visibility.Visible;
                    break;
            }
        }

        return changedGroupCount;
    }

    public static void SetCollapsedButtonFootprint(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprint = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(availableWidth);
        foreach (var button in collapsedButtons)
        {
            button.Width = footprint.Width;
            button.Margin = footprint.Margin;
            button.Padding = footprint.Padding;

            if (TryGetCollapsedRibbonButtonCaption(button, out var caption))
                ApplyCollapsedRibbonButtonCaptionFootprint(caption, footprint);

            if (TryGetCollapsedRibbonButtonTextIcon(button, out var icon))
                icon.FontSize = footprint.IconFontSize;
        }
    }

    public static void ApplyGroup(
        MainWindow.RibbonCompactGroupSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        foreach (var label in snapshot.CommandLabels)
            label.Visibility = level == MainWindow.RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;

        foreach (var buttonSnapshot in snapshot.Buttons)
        {
            if (buttonSnapshot.HasCompactWidths)
            {
                buttonSnapshot.Button.Width = level switch
                {
                    MainWindow.RibbonCompactLevel.Full => buttonSnapshot.FullWidth,
                    MainWindow.RibbonCompactLevel.SmallWithLabels => buttonSnapshot.IsLargeButton ? double.NaN : buttonSnapshot.FullWidth,
                    _ => buttonSnapshot.CompactWidth
                };
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

    public static void ApplyButton(
        MainWindow.RibbonCompactButtonSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        if (snapshot.IsCheckOrRadioButton)
        {
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (snapshot.Content is not null)
                snapshot.Content.HorizontalAlignment = HorizontalAlignment.Left;
            return;
        }

        foreach (var label in snapshot.Labels)
            label.Visibility = level == MainWindow.RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;

        var isSmallOrMedium = snapshot.ContentLayout is RibbonCommandContentLayout.Small or RibbonCommandContentLayout.Medium;
        if (snapshot.HasContentLayout &&
            snapshot.ContentLayout == RibbonCommandContentLayout.Small &&
            snapshot.SmallGrid is not null)
        {
            ApplySmallButtonLayout(snapshot, level);
        }

        if (!isSmallOrMedium)
        {
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Center;

            if (snapshot.Content is not null)
                snapshot.Content.HorizontalAlignment = HorizontalAlignment.Center;

            foreach (var stack in snapshot.HorizontalStacks)
                stack.HorizontalAlignment = HorizontalAlignment.Center;
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
        RibbonAdaptiveGroupState plannedState) =>
        plannedState == RibbonAdaptiveGroupState.IconOnly &&
        RibbonMetadata.TryGetCatalogId(snapshot.Group, out var catalogId) &&
        ShouldUseSmallWithLabelsForIconOnlyGroup(catalogId)
            ? RibbonAdaptiveGroupState.SmallWithLabels
            : plannedState;

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
        caption.Visibility = footprint.CaptionVisibility;
        caption.FontSize = footprint.CaptionFontSize;
        caption.MaxWidth = footprint.CaptionMaxWidth;
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.TextAlignment = TextAlignment.Center;
    }

    private static void ApplySmallButtonLayout(
        MainWindow.RibbonCompactButtonSnapshot snapshot,
        MainWindow.RibbonCompactLevel level)
    {
        snapshot.Button.HorizontalAlignment = HorizontalAlignment.Left;

        if (snapshot.SmallSpacerColumn is not null)
        {
            snapshot.SmallSpacerColumn.Width = level == MainWindow.RibbonCompactLevel.IconOnly
                ? new GridLength(0)
                : new GridLength(5);
        }

        if (level == MainWindow.RibbonCompactLevel.IconOnly)
        {
            snapshot.SmallGrid!.HorizontalAlignment = HorizontalAlignment.Center;
            // Keep the command icon aligned with 24px icon-only peers while the menu lane extends to the right.
            snapshot.SmallGrid.Margin = GetSmallButtonDropdownColumn(snapshot.SmallGrid) is { } dropdownColumn
                ? new Thickness(-dropdownColumn.Width.Value, 0, 0, 0)
                : new Thickness(0);
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            snapshot.SmallGrid!.HorizontalAlignment = HorizontalAlignment.Left;
            snapshot.SmallGrid.Margin = new Thickness(0);
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Left;
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
            snapshot.LargeStack.Orientation = Orientation.Vertical;
            snapshot.LargeStack.HorizontalAlignment = HorizontalAlignment.Center;
            snapshot.Button.Height = 76;
            snapshot.LargeIconSlot.Width = 34;
            snapshot.LargeIconSlot.Height = 34;
            snapshot.LargeIconSlot.Margin = new Thickness(0, 0, 0, 2);
            if (snapshot.LargeIconChild is not null)
            {
                snapshot.LargeIconChild.Width = 32;
                snapshot.LargeIconChild.Height = 32;
            }
            snapshot.LargeLabelBlock.TextWrapping = TextWrapping.Wrap;
            snapshot.LargeLabelBlock.MaxWidth = 96;
            snapshot.LargeLabelBlock.TextTrimming = TextTrimming.None;
            snapshot.LargeLabelBlock.HorizontalAlignment = HorizontalAlignment.Center;
            snapshot.LargeLabelBlock.TextAlignment = TextAlignment.Center;
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            snapshot.LargeStack.Orientation = Orientation.Horizontal;
            snapshot.LargeStack.HorizontalAlignment = HorizontalAlignment.Left;
            snapshot.Button.Height = 48;
            snapshot.LargeIconSlot.Width = 24;
            snapshot.LargeIconSlot.Height = 24;
            snapshot.LargeIconSlot.Margin = new Thickness(0, 0, 5, 0);
            if (snapshot.LargeIconChild is not null)
            {
                snapshot.LargeIconChild.Width = 24;
                snapshot.LargeIconChild.Height = 24;
            }
            snapshot.LargeLabelBlock.TextWrapping = TextWrapping.NoWrap;
            snapshot.LargeLabelBlock.MaxWidth = 90;
            snapshot.LargeLabelBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            snapshot.LargeLabelBlock.HorizontalAlignment = HorizontalAlignment.Left;
            snapshot.LargeLabelBlock.TextAlignment = TextAlignment.Left;
            snapshot.Button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
    }

    private static string GetRibbonGroupName(FrameworkElement group) =>
        RibbonMetadata.TryGetGroupName(group, out var groupName) ? groupName : "Commands";
}
