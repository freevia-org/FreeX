using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>WPF chrome for the shared Drop Cap Options state and result policy.</summary>
internal sealed class DropCapOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly DropCapOptionsDialogSession _session;
    private readonly RadioButton _none;
    private readonly RadioButton _dropped;
    private readonly RadioButton _inMargin;
    private readonly ComboBox _font;
    private readonly TextBox _lines;
    private readonly TextBox _distance;
    private DropCapOptionsDialogResult? _result;

    private DropCapOptionsDialog(Window? owner)
    {
        _session = new DropCapOptionsDialogSession(CultureInfo.CurrentCulture);
        Owner = owner;
        Title = DropCapOptionsDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var state = _session.InitialState;
        _none = PositionButton(DropCapOptionsDialogPlanner.NoneLabel);
        _dropped = PositionButton(DropCapOptionsDialogPlanner.DroppedLabel);
        _inMargin = PositionButton(DropCapOptionsDialogPlanner.InMarginLabel);
        new[] { _none, _dropped, _inMargin }[state.PositionIndex].IsChecked = true;

        _font = new ComboBox { IsEditable = true, MinWidth = 160, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var fontName in _session.FontNames)
            _font.Items.Add(fontName);
        _font.SelectedIndex = state.FontIndex;

        _lines = NumberBox(state.LinesToDropText);
        _distance = NumberBox(state.DistanceFromTextText);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, DropCapOptionsDialogPlanner.AutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_none, DropCapOptionsDialogPlanner.NoneAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_dropped, DropCapOptionsDialogPlanner.DroppedAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_inMargin, DropCapOptionsDialogPlanner.InMarginAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_font, DropCapOptionsDialogPlanner.FontAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_lines, DropCapOptionsDialogPlanner.LinesAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_distance, DropCapOptionsDialogPlanner.DistanceAutomationId);

        var positionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        positionRow.Children.Add(_none);
        positionRow.Children.Add(_dropped);
        positionRow.Children.Add(_inMargin);

        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = DropCapOptionsDialogPlanner.PositionLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(positionRow);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Row(DropCapOptionsDialogPlanner.FontLabel, _font));
        panel.Children.Add(Row(DropCapOptionsDialogPlanner.LinesToDropLabel, _lines));
        panel.Children.Add(Row(DropCapOptionsDialogPlanner.DistanceFromTextLabel, _distance));
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72));
        Content = panel;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_lines);
    }

    private static RadioButton PositionButton(string label) => new()
    {
        Content = label,
        GroupName = "DropCapPosition",
        Margin = new Thickness(4, 2, 12, 2)
    };

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        Width = 50,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static StackPanel Row(string label, UIElement control)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        row.Children.Add(control);
        return row;
    }

    private void Accept()
    {
        var positionIndex = _none.IsChecked == true
            ? (int)DropCapDialogPosition.None
            : _inMargin.IsChecked == true
                ? (int)DropCapDialogPosition.InMargin
                : (int)DropCapDialogPosition.Dropped;
        _result = _session.PlanAcceptance(
            new DropCapOptionsDialogInput(positionIndex, _font.Text, _lines.Text, _distance.Text));
        Close();
    }

    public static DropCapOptionsDialogResult? Prompt(Window? owner)
    {
        var dialog = new DropCapOptionsDialog(owner);
        dialog.ShowDialog();
        return dialog._result;
    }
}
