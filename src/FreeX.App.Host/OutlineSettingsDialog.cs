using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Host;

/// <summary>
/// Data &gt; Outline &gt; Settings dialog (summary rows below / columns to the right / automatic styles).
/// Extracted verbatim from the inline window the sheet-tab "Outline Settings..." handler built; the caller
/// sets <see cref="Window.Owner"/> before <see cref="Window.ShowDialog"/> and reads <see cref="AcceptedState"/>
/// when it returns true. Distinct from <see cref="OutlineGroupDialog"/>, the Group/Ungroup rows-or-columns picker.
/// </summary>
public sealed class OutlineSettingsDialog : Window
{
    private readonly CheckBox _summaryBelowBox;
    private readonly CheckBox _summaryRightBox;
    private readonly CheckBox _autoStylesBox;

    public OutlineSettingsDialog(OutlineSettingsState current)
    {
        _summaryBelowBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_SummaryRowsBelow"),
            IsChecked = current.SummaryBelow,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(_summaryBelowBox, "OutlineSettingsSummaryBelowCheckBox");

        _summaryRightBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_SummaryColumnsRight"),
            IsChecked = current.SummaryRight,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(_summaryRightBox, "OutlineSettingsSummaryRightCheckBox");

        _autoStylesBox = new CheckBox
        {
            Content = UiText.Get("OutlineSettings_AutomaticStyles"),
            IsChecked = current.ApplyStyles,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(_autoStylesBox, "OutlineSettingsAutomaticStylesCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Ok"),
            IsDefault = true,
            MinWidth = 84,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(okButton, "OutlineSettingsOkButton");

        var cancelButton = new Button
        {
            Content = UiText.Get("OutlineSettings_Cancel"),
            IsCancel = true,
            MinWidth = 84
        };
        AutomationProperties.SetAutomationId(cancelButton, "OutlineSettingsCancelButton");

        Title = UiText.Get("OutlineSettings_Title");
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "OutlineSettingsDialog");

        okButton.Click += (_, _) => DialogResult = true;
        cancelButton.Click += (_, _) => DialogResult = false;

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("OutlineSettings_Direction"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                _summaryBelowBox,
                _summaryRightBox,
                _autoStylesBox,
                buttonRow
            }
        };
    }

    public OutlineSettingsState AcceptedState => new(
        _summaryBelowBox.IsChecked == true,
        _summaryRightBox.IsChecked == true,
        _autoStylesBox.IsChecked == true);
}
