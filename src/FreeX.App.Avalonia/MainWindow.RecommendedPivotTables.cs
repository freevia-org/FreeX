using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task<RecommendedPivotTablesDialogResult> ShowRecommendedPivotTablesDialogAsync()
    {
        var result = RecommendedPivotTablesDialogResult.None;
        var dialog = new Window
        {
            Title = UiText.Get(RecommendedPivotTablesDialogPlanner.TitleKey),
            Width = RecommendedPivotTablesDialogPlanner.Width,
            // Match the Windows dialog's compact proportions: size to the content's natural height
            // (a short left list of layouts + a preview area) instead of stretching tall. The fixed
            // height keeps the empty-state preview from leaving a large blank region below it.
            Height = RecommendedPivotTablesDialogPlanner.MinHeight,
            MinHeight = RecommendedPivotTablesDialogPlanner.MinHeight,
            MaxHeight = RecommendedPivotTablesDialogPlanner.MinHeight,
            SizeToContent = SizeToContent.Manual,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, RecommendedPivotTablesDialogPlanner.DialogAutomationId);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.TitleKey),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var emptyState = CreateRecommendedPivotTablesEmptyState();
        Grid.SetRow(emptyState, 1);
        root.Children.Add(emptyState);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var blankPivotTable = new Button
        {
            Content = UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableKey),
            Width = RecommendedPivotTablesDialogPlanner.BlankPivotTableButtonWidth,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(blankPivotTable, RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationId);
        AutomationProperties.SetName(blankPivotTable, UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationNameKey));
        blankPivotTable.Click += (_, _) =>
        {
            result = RecommendedPivotTablesDialogResult.BlankPivotTable;
            dialog.Close();
        };

        var cancel = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            Width = RecommendedPivotTablesDialogPlanner.CancelButtonWidth,
            IsCancel = true
        };
        buttons.Children.Add(blankPivotTable);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Opened += (_, _) => blankPivotTable.Focus();
        await dialog.ShowDialog(this);
        return result;
    }

    private static Border CreateRecommendedPivotTablesEmptyState()
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.NoRecommendationsHeadingKey),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.NoRecommendationsBodyKey),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        });

        return new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = stack
        };
    }
}
