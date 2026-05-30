using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

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
        DialogFocus.FocusAndSelect(_destinationBox);
    }

    private void FocusInvalidFixedWidthBreaksInput()
    {
        _wizardStep = 2;
        _fixedWidthButton.IsChecked = true;
        UpdateWizardStep();
        DialogFocus.FocusAndSelect(_fixedWidthBreaksBox);
    }

    private void FocusInvalidCustomDelimiterInput()
    {
        _wizardStep = 2;
        _delimitedButton.IsChecked = true;
        _otherBox.IsChecked = true;
        UpdateWizardStep();
        DialogFocus.FocusAndSelect(_customBox);
    }

    private void FocusInvalidDelimiterSelectionInput()
    {
        _wizardStep = 2;
        _delimitedButton.IsChecked = true;
        UpdateWizardStep();
        _tabBox.Focus();
        Keyboard.Focus(_tabBox);
    }

    private void FocusInvalidAdvancedSeparatorInput(TextBox textBox)
    {
        _wizardStep = 3;
        UpdateWizardStep();
        DialogFocus.FocusAndSelect(textBox);
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
            Content = UiText.Get("TextToColumns_BackButton"),
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _backButton.Click += (_, _) => MoveWizardStep(-1);
        panel.Children.Add(_backButton);
        _nextButton = new Button
        {
            Content = UiText.Get("TextToColumns_NextButton"),
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
        _finishButton = new Button
        {
            Content = UiText.Get("TextToColumns_FinishButton"),
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _finishButton.Click += (_, _) => Accept();
        panel.Children.Add(_finishButton);
        panel.Children.Add(new Button { Content = UiText.Cancel, Width = 72, IsCancel = true });
        return panel;
    }

    private void MoveWizardStep(int direction)
    {
        _wizardStep = Math.Clamp(_wizardStep + direction, 1, 3);
        UpdateWizardStep();
        UpdateLayout();
        FocusCurrentWizardStepTarget();
    }

    private void UpdateWizardStep()
    {
        var plan = TextToColumnsWizardPlanner.CreateStepPlan(_wizardStep, _fixedWidthButton.IsChecked == true);
        _wizardHeader.Text = plan.Header;
        _wizardInstruction.Text = plan.Instruction;

        SetVisible(_originalDataTypePanel, plan.ShowOriginalDataTypePanel);
        SetVisible(_delimiterPanel, plan.ShowDelimiterPanel);
        SetVisible(_fixedWidthPanel, plan.ShowFixedWidthPanel);
        SetVisible(_dataPreviewLabel, true);
        _previewGrid.Visibility = Visibility.Visible;
        SetVisible(_columnFormatPanel, plan.ShowColumnFormatPanel);
        SetVisible(_destinationPanel, plan.ShowDestinationPanel);

        if (_backButton is not null)
            _backButton.IsEnabled = plan.BackEnabled;
        if (_nextButton is not null)
        {
            _nextButton.IsEnabled = plan.NextEnabled;
            _nextButton.IsDefault = plan.NextDefault;
        }
        if (_finishButton is not null)
            _finishButton.IsDefault = plan.FinishDefault;
    }

    private static void SetVisible(FrameworkElement? element, bool visible)
    {
        if (element is not null)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMode()
    {
        var plan = TextToColumnsWizardPlanner.CreateModePlan(
            _fixedWidthButton.IsChecked == true,
            _otherBox.IsChecked == true);
        _tabBox.IsEnabled = plan.DelimitedControlsEnabled;
        _semicolonBox.IsEnabled = plan.DelimitedControlsEnabled;
        _commaBox.IsEnabled = plan.DelimitedControlsEnabled;
        _spaceBox.IsEnabled = plan.DelimitedControlsEnabled;
        _otherBox.IsEnabled = plan.DelimitedControlsEnabled;
        _customBox.IsEnabled = plan.CustomDelimiterEnabled;
        _textQualifierBox.IsEnabled = plan.DelimitedControlsEnabled;
        _treatConsecutiveDelimitersBox.IsEnabled = plan.DelimitedControlsEnabled;
        _fixedWidthBreaksBox.IsEnabled = plan.FixedWidthControlsEnabled;
        _fixedWidthRuler.IsEnabled = plan.FixedWidthControlsEnabled;
        _fixedWidthRuler.Opacity = plan.FixedWidthRulerOpacity;
        UpdateWizardStep();
        RefreshPreview();
    }

    private void FocusCurrentWizardStepTarget()
    {
        switch (_wizardStep)
        {
            case 1:
                FocusControl(_fixedWidthButton.IsChecked == true ? _fixedWidthButton : _delimitedButton);
                break;
            case 2 when _fixedWidthButton.IsChecked == true:
                DialogFocus.FocusAndSelect(_fixedWidthBreaksBox);
                break;
            case 2:
                FocusControl(_tabBox);
                break;
            default:
                FocusControl(_formatColumnBox);
                break;
        }
    }

    private static void FocusControl(Control target)
    {
        target.Focus();
        Keyboard.Focus(target);
    }
}
