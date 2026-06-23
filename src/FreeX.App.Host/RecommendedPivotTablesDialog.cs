using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Services;

namespace FreeX.App.Host;
public sealed class RecommendedPivotTablesDialog : Window
{
    private readonly Button _blankPivotTableButton = new();

    public RecommendedPivotTablesDialogResult Result { get; private set; }

    public RecommendedPivotTablesDialog()
    {
        Result = RecommendedPivotTablesDialogResult.None;
        Title = UiText.Get(RecommendedPivotTablesDialogPlanner.TitleKey);
        DialogSizing.ApplyContentHeight(
            this,
            width: RecommendedPivotTablesDialogPlanner.Width,
            minHeight: RecommendedPivotTablesDialogPlanner.MinHeight);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, RecommendedPivotTablesDialogPlanner.DialogAutomationId);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.TitleKey),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(title);

        var emptyState = CreateEmptyRecommendationsPanel();
        Grid.SetRow(emptyState, 1);
        root.Children.Add(emptyState);

        var buttons = CreateButtonRow();
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static Border CreateEmptyRecommendationsPanel()
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };

        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get(RecommendedPivotTablesDialogPlanner.NoRecommendationsHeadingKey),
            FontWeight = FontWeights.SemiBold,
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
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            Background = SystemColors.ControlLightLightBrush,
            Child = stack
        };
    }

    private StackPanel CreateButtonRow()
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        _blankPivotTableButton.Content = UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableKey);
        _blankPivotTableButton.Width = RecommendedPivotTablesDialogPlanner.BlankPivotTableButtonWidth;
        _blankPivotTableButton.Margin = new Thickness(0, 0, 8, 0);
        _blankPivotTableButton.IsDefault = true;
        AutomationProperties.SetAutomationId(
            _blankPivotTableButton,
            RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationId);
        AutomationProperties.SetName(
            _blankPivotTableButton,
            UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationNameKey));
        AutomationProperties.SetHelpText(
            _blankPivotTableButton,
            UiText.Get(RecommendedPivotTablesDialogPlanner.BlankPivotTableAutomationHelpTextKey));
        _blankPivotTableButton.Click += (_, _) =>
        {
            Result = RecommendedPivotTablesDialogResult.BlankPivotTable;
            DialogResult = true;
        };

        var cancel = new Button
        {
            Content = UiText.Cancel,
            Width = RecommendedPivotTablesDialogPlanner.CancelButtonWidth,
            IsCancel = true
        };

        buttons.Children.Add(_blankPivotTableButton);
        buttons.Children.Add(cancel);
        return buttons;
    }

    private void FocusInitialKeyboardTarget()
    {
        _blankPivotTableButton.Focus();
        Keyboard.Focus(_blankPivotTableButton);
    }
}
