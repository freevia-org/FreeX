using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed partial class SelectDataSourceDialog
{
    private static Grid CreateSourceListPanel(
        string title,
        string automationName,
        string helpText,
        ListBox list,
        ((string Label, RoutedEventHandler Handler) Add, (string Label, RoutedEventHandler Handler)? Edit, (string Label, RoutedEventHandler Handler)? Remove) buttons)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 2) });
        header.Children.Add(CreateInlineHelp(helpText));
        panel.Children.Add(header);
        AutomationProperties.SetName(list, automationName);
        AutomationProperties.SetHelpText(list, helpText);
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        var buttonPanel = AddEditRemoveButtons(buttons);
        Grid.SetColumn(buttonPanel, 1);
        Grid.SetRowSpan(buttonPanel, 2);
        panel.Children.Add(buttonPanel);
        return panel;
    }

    private static StackPanel AddEditRemoveButtons(
        ((string Label, RoutedEventHandler Handler) Add, (string Label, RoutedEventHandler Handler)? Edit, (string Label, RoutedEventHandler Handler)? Remove) labels)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 20, 0, 0) };
        stack.Children.Add(CreateSeriesButton(labels.Add.Label, labels.Add.Handler, new Thickness(0, 0, 0, 4)));
        if (labels.Edit is not null)
            stack.Children.Add(CreateSeriesButton(labels.Edit.Value.Label, labels.Edit.Value.Handler, new Thickness(0, 0, 0, 4)));
        if (labels.Remove is not null)
            stack.Children.Add(CreateSeriesButton(labels.Remove.Value.Label, labels.Remove.Value.Handler, new Thickness()));
        return stack;
    }

    private static Button CreateSeriesButton(string content, RoutedEventHandler handler, Thickness margin)
    {
        var button = new Button
        {
            Content = content,
            Width = 92,
            Margin = margin
        };
        button.Click += handler;
        return button;
    }
}
