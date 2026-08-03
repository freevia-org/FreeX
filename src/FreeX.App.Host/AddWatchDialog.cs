using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Services;
using Free.Shared.Shell.Wpf;

namespace FreeX.App.Host;

public sealed class AddWatchDialog : DialogWindow
{
    private readonly TextBox _rangeBox = new();

    public AddWatchDialog(string selectedRangeText)
    {
        Title = UiText.Get(AddWatchDialogPlanner.TitleKey);
        Width = AddWatchDialogPlanner.Width;
        Height = AddWatchDialogPlanner.Height;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(AddWatchDialogPlanner.RootMargin) };

        var add = new Button
        {
            Content = UiText.Get(AddWatchDialogPlanner.AddButtonKey),
            Width = AddWatchDialogPlanner.ButtonWidth
        };
        AutomationProperties.SetName(add, UiText.Get(AddWatchDialogPlanner.AddAutomationNameKey));
        AutomationProperties.SetAutomationId(add, AddWatchDialogPlanner.AddButtonAutomationId);
        AutomationProperties.SetHelpText(add, UiText.Get(AddWatchDialogPlanner.AddHelpTextKey));
        add.Click += (_, _) => DialogResult = true;
        var cancel = new Button
        {
            Content = UiText.Get(AddWatchDialogPlanner.CancelButtonKey),
            Width = AddWatchDialogPlanner.ButtonWidth
        };
        AutomationProperties.SetName(cancel, UiText.Get(AddWatchDialogPlanner.CancelAutomationNameKey));
        AutomationProperties.SetAutomationId(cancel, AddWatchDialogPlanner.CancelButtonAutomationId);
        AutomationProperties.SetHelpText(cancel, UiText.Get(AddWatchDialogPlanner.CancelHelpTextKey));

        var buttons = DialogButtonRowFactory.Create(
            add,
            cancel,
            new Thickness(0, AddWatchDialogPlanner.ActionRowTopMargin, 0, 0));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        root.Children.Add(body);
        _rangeBox.Text = selectedRangeText;
        _rangeBox.IsReadOnly = true;
        _rangeBox.Margin = new Thickness(0, 0, 0, AddWatchDialogPlanner.RangeBottomMargin);
        AutomationProperties.SetName(_rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeAutomationNameKey));
        AutomationProperties.SetAutomationId(_rangeBox, AddWatchDialogPlanner.SelectedRangeAutomationId);
        AutomationProperties.SetHelpText(_rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeHelpTextKey));
        body.Children.Add(new Label
        {
            Content = UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey),
            Target = _rangeBox,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(_rangeBox);
        body.Children.Add(new TextBlock
        {
            Text = UiText.Get(AddWatchDialogPlanner.BodyTextKey),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush
        });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_rangeBox);
    }
}
