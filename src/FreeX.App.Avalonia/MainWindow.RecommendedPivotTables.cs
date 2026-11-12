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
            // The WPF dialog sizes to content with a 340px OUTER minimum, and the empty state is far
            // shorter than that, so the window opens at the minimum. Avalonia sizes are client sizes:
            // take the Windows caption/frame off so no empty band is left under the button row.
            MinWidth = RecommendedPivotTablesDialogPlanner.Width,
            MaxWidth = RecommendedPivotTablesDialogPlanner.Width,
            Height = RecommendedPivotTablesDialogPlanner.MinHeight - WpfNoResizeFrameHeight,
            MinHeight = RecommendedPivotTablesDialogPlanner.MinHeight - WpfNoResizeFrameHeight,
            MaxHeight = RecommendedPivotTablesDialogPlanner.MinHeight - WpfNoResizeFrameHeight,
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
            FontFamily = FormulaBarFontFamily,
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
        };
        // WPF push buttons here are default-height (20px) system buttons with fixed widths.
        ApplyPivotButtonChrome(blankPivotTable, RecommendedPivotTablesDialogPlanner.BlankPivotTableButtonWidth, isDefault: true);
        blankPivotTable.Padding = new Thickness(1);
        blankPivotTable.Margin = new Thickness(0, 0, 8, 0);
        AutomationProperties.SetAutomationId(blankPivotTable, RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationId);
        AutomationProperties.SetName(blankPivotTable, UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationNameKey));
        AutomationProperties.SetHelpText(blankPivotTable, UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationHelpTextKey));
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
        ApplyPivotButtonChrome(cancel, RecommendedPivotTablesDialogPlanner.CancelButtonWidth);
        cancel.Padding = new Thickness(1);
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
            FontFamily = FormulaBarFontFamily,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.NoRecommendationsBodyKey),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        });

        return new Border
        {
            // WPF SystemColors.ControlDarkBrush (#A0A0A0) on ControlLightLight (white).
            BorderBrush = Brush(160, 160, 160),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = stack
        };
    }
}
