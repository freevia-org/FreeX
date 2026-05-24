using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog
{
    private void FocusInitialKeyboardTarget()
    {
        _delimitedButton.Focus();
        Keyboard.Focus(_delimitedButton);
    }

    private void FocusInvalidDestinationInput()
    {
        _wizardStep = 3;
        UpdateWizardStep();
        _destinationBox.Focus();
        Keyboard.Focus(_destinationBox);
        _destinationBox.SelectAll();
    }

    private void FocusInvalidFixedWidthBreaksInput()
    {
        _wizardStep = 2;
        _fixedWidthButton.IsChecked = true;
        UpdateWizardStep();
        _fixedWidthBreaksBox.Focus();
        _fixedWidthBreaksBox.SelectAll();
        Keyboard.Focus(_fixedWidthBreaksBox);
    }

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));

    private StackPanel CreateWizardButtonRow()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        _backButton = new Button
        {
            Content = "< _Back",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _backButton.Click += (_, _) => MoveWizardStep(-1);
        panel.Children.Add(_backButton);
        _nextButton = new Button
        {
            Content = "_Next >",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _nextButton.Click += (_, _) =>
        {
            if (_wizardStep < 3)
                MoveWizardStep(1);
            else
                Accept();
        };
        panel.Children.Add(_nextButton);
        var finishButton = new Button
        {
            Content = "_Finish",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        finishButton.Click += (_, _) => Accept();
        panel.Children.Add(finishButton);
        panel.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });
        return panel;
    }

    private void MoveWizardStep(int direction)
    {
        _wizardStep = Math.Clamp(_wizardStep + direction, 1, 3);
        UpdateWizardStep();
    }

    private void UpdateWizardStep()
    {
        _wizardHeader.Text = $"Text Wizard - Step {_wizardStep} of 3";
        _wizardInstruction.Text = _wizardStep switch
        {
            1 => "Choose the file type that best describes your data.",
            2 => "Choose the delimiters that separate your selected text.",
            _ => "Select each column and set the data format and destination."
        };

        SetVisible(_originalDataTypePanel, _wizardStep == 1);
        SetVisible(_delimiterPanel, _wizardStep == 2 && _fixedWidthButton.IsChecked != true);
        SetVisible(_fixedWidthPanel, _wizardStep == 2 && _fixedWidthButton.IsChecked == true);
        SetVisible(_dataPreviewLabel, true);
        _previewGrid.Visibility = Visibility.Visible;
        SetVisible(_columnFormatPanel, _wizardStep == 3);
        SetVisible(_destinationPanel, _wizardStep == 3);

        if (_backButton is not null)
            _backButton.IsEnabled = _wizardStep > 1;
        if (_nextButton is not null)
            _nextButton.IsEnabled = _wizardStep < 3;
    }

    private static void SetVisible(FrameworkElement? element, bool visible)
    {
        if (element is not null)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMode()
    {
        var fixedWidth = _fixedWidthButton.IsChecked == true;
        _tabBox.IsEnabled = !fixedWidth;
        _semicolonBox.IsEnabled = !fixedWidth;
        _commaBox.IsEnabled = !fixedWidth;
        _spaceBox.IsEnabled = !fixedWidth;
        _otherBox.IsEnabled = !fixedWidth;
        _customBox.IsEnabled = !fixedWidth && _otherBox.IsChecked == true;
        _textQualifierBox.IsEnabled = !fixedWidth;
        _treatConsecutiveDelimitersBox.IsEnabled = !fixedWidth;
        _fixedWidthBreaksBox.IsEnabled = fixedWidth;
        _fixedWidthRuler.IsEnabled = fixedWidth;
        _fixedWidthRuler.Opacity = fixedWidth ? 1.0 : 0.55;
        UpdateWizardStep();
        RefreshPreview();
    }
}
