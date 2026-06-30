using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Charts.Editing;
using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog
{
    private Grid CreateSourceListPanel(
        SelectDataSourceListPanelDescriptor descriptor,
        ListBox list,
        IReadOnlyDictionary<SelectDataSourceDialogActionId, RoutedEventHandler> handlers)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var listField = descriptor.ListField;
        var helpText = UiText.Get(listField.HelpResourceKey!);
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = UiText.Get(descriptor.TitleResourceKey), Margin = new Thickness(0, 0, 0, 2) });
        header.Children.Add(CreateInlineHelp(helpText));
        panel.Children.Add(header);
        AutomationProperties.SetName(list, UiText.Get(listField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(list, listField.AutomationId);
        AutomationProperties.SetHelpText(list, helpText);
        Grid.SetRow(list, 1);
        panel.Children.Add(list);

        var buttonPanel = AddEditRemoveButtons(descriptor.Actions, handlers);
        Grid.SetColumn(buttonPanel, 1);
        Grid.SetRowSpan(buttonPanel, 2);
        panel.Children.Add(buttonPanel);
        return panel;
    }

    private StackPanel AddEditRemoveButtons(
        IReadOnlyList<SelectDataSourceDialogActionDescriptor> actions,
        IReadOnlyDictionary<SelectDataSourceDialogActionId, RoutedEventHandler> handlers)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 20, 0, 0) };
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (!handlers.TryGetValue(action.Id, out var handler))
                continue;

            var margin = index == actions.Count - 1
                ? new Thickness()
                : new Thickness(0, 0, 0, 4);
            stack.Children.Add(CreateSeriesButton(action, handler, margin));
        }

        return stack;
    }

    private Button CreateSeriesButton(SelectDataSourceDialogActionDescriptor action, RoutedEventHandler handler, Thickness margin)
    {
        var button = new Button
        {
            Content = UiText.Get(action.LabelResourceKey),
            Width = 92,
            Margin = margin
        };
        AutomationProperties.SetAutomationId(button, action.AutomationId);
        button.Click += handler;
        if (action.Id == SelectDataSourceDialogActionId.EditSeries)
            _editSeriesButton = button;
        else if (action.Id == SelectDataSourceDialogActionId.RemoveSeries)
            _removeSeriesButton = button;
        else if (action.Id == SelectDataSourceDialogActionId.EditAxisLabels)
            _editAxisLabelsButton = button;
        return button;
    }
}
