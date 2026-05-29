using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Freexcel.App.Host;

internal static class RibbonMenuItemCloner
{
    public static object? CloneRibbonMenuItem(object source)
    {
        if (source is Separator)
            return new Separator();

        if (source is not MenuItem sourceItem)
            return null;

        var item = new MenuItem
        {
            Header = CloneRibbonMenuContent(sourceItem.Header),
            Icon = CloneRibbonMenuContent(sourceItem.Icon),
            IsEnabled = sourceItem.IsEnabled,
            IsCheckable = sourceItem.IsCheckable,
            IsChecked = sourceItem.IsChecked,
            InputGestureText = sourceItem.InputGestureText
        };

        var keyTip = RibbonTooltip.GetKeyTip(sourceItem);
        if (!string.IsNullOrWhiteSpace(keyTip))
            RibbonTooltip.SetKeyTip(item, keyTip);

        foreach (var child in sourceItem.Items)
        {
            if (CloneRibbonMenuItem(child) is { } childItem)
                item.Items.Add(childItem);
        }

        item.SubmenuOpened += (_, _) =>
        {
            sourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, sourceItem));
            SynchronizeMenuItemState(sourceItem, item);
        };

        item.Click += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, item))
                return;

            if (sourceItem.Items.Count > 0)
                return;

            sourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, sourceItem));
        };

        return item;
    }

    public static void SynchronizeClonedMenuItems(ItemCollection sourceItems, ItemCollection clonedItems)
    {
        var clonedIndex = 0;
        foreach (var source in sourceItems)
        {
            if (source is Separator)
            {
                clonedIndex++;
                continue;
            }

            if (source is not MenuItem sourceItem)
                continue;

            while (clonedIndex < clonedItems.Count && clonedItems[clonedIndex] is not MenuItem)
                clonedIndex++;

            if (clonedIndex >= clonedItems.Count)
                break;

            if (clonedItems[clonedIndex] is MenuItem clonedItem)
                SynchronizeMenuItemState(sourceItem, clonedItem);

            clonedIndex++;
        }
    }

    private static object? CloneRibbonMenuContent(object? content)
    {
        if (content is null or string)
            return content;

        if (content is Image image)
        {
            return new Image
            {
                Source = image.Source,
                Width = image.Width,
                Height = image.Height,
                MaxWidth = image.MaxWidth,
                MaxHeight = image.MaxHeight,
                Stretch = image.Stretch,
                SnapsToDevicePixels = image.SnapsToDevicePixels,
                UseLayoutRounding = image.UseLayoutRounding,
                Margin = image.Margin,
                Opacity = image.Opacity
            };
        }

        if (content is TextBlock textBlock)
        {
            return new TextBlock
            {
                Text = textBlock.Text,
                FontSize = textBlock.FontSize,
                FontWeight = textBlock.FontWeight,
                FontStyle = textBlock.FontStyle,
                Foreground = textBlock.Foreground,
                Margin = textBlock.Margin,
                VerticalAlignment = textBlock.VerticalAlignment,
                HorizontalAlignment = textBlock.HorizontalAlignment
            };
        }

        if (content is FrameworkElement element)
        {
            try
            {
                return System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(element));
            }
            catch (InvalidOperationException)
            {
                return ExtractRibbonMenuText(element) ?? element.ToString();
            }
        }

        return content;
    }

    private static string? ExtractRibbonMenuText(DependencyObject element)
    {
        if (element is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            return textBlock.Text;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (ExtractRibbonMenuText(VisualTreeHelper.GetChild(element, i)) is { } text)
                return text;
        }

        return null;
    }

    private static void SynchronizeMenuItemState(MenuItem sourceItem, MenuItem clonedItem)
    {
        clonedItem.IsEnabled = sourceItem.IsEnabled;
        clonedItem.IsCheckable = sourceItem.IsCheckable;
        clonedItem.IsChecked = sourceItem.IsChecked;

        var keyTip = RibbonTooltip.GetKeyTip(sourceItem);
        RibbonTooltip.SetKeyTip(clonedItem, keyTip ?? "");
        if (string.IsNullOrWhiteSpace(keyTip))
            clonedItem.InputGestureText = sourceItem.InputGestureText;

        SynchronizeClonedMenuItems(sourceItem.Items, clonedItem.Items);
    }
}
