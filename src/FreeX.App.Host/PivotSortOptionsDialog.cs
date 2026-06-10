using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class PivotSortOptionsDialog : Window
{
    private readonly int _sourceFieldIndex;
    private readonly IReadOnlyList<PivotDataFieldModel> _dataFields;
    private readonly RadioButton _labelAscendingButton = new() { Content = "Ascending (A to Z) by labels", IsChecked = true };
    private readonly RadioButton _labelDescendingButton = new() { Content = "Descending (Z to A) by labels" };
    private readonly RadioButton _valueAscendingButton = new() { Content = "Ascending by values" };
    private readonly RadioButton _valueDescendingButton = new() { Content = "Descending by values" };
    private readonly ComboBox _valueFieldBox = new() { MinWidth = 220 };

    public PivotSortOptionsDialog(
        string fieldCaption,
        int sourceFieldIndex,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        PivotSortModel? currentSort = null)
    {
        _sourceFieldIndex = sourceFieldIndex;
        _dataFields = dataFields;
        ResultSort = new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: sourceFieldIndex);

        Title = $"More Sort Options - {fieldCaption}";
        Width = 360;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        Content = CreateContent(fieldCaption);
        LoadState(currentSort);
        UpdateValueFieldState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotSortModel ResultSort { get; private set; }

    private UIElement CreateContent(string fieldCaption)
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            Width = 74,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okButton.Click += OkButton_Click;
        AutomationProperties.SetAutomationId(okButton, "PivotSortOptionsOkButton");

        var cancelButton = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            Width = 74,
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(cancelButton, "PivotSortOptionsCancelButton");

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"Sort {fieldCaption}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(_labelAscendingButton);
        stack.Children.Add(_labelDescendingButton);
        stack.Children.Add(_valueAscendingButton);
        stack.Children.Add(_valueDescendingButton);

        var valuePanel = new StackPanel { Margin = new Thickness(18, 8, 0, 0) };
        var valueLabel = new Label
        {
            Content = "Value field:",
            Target = _valueFieldBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        };
        valuePanel.Children.Add(valueLabel);
        valuePanel.Children.Add(_valueFieldBox);
        stack.Children.Add(valuePanel);

        _valueFieldBox.ItemsSource = _dataFields.Select(field => field.Name).ToList();
        _valueFieldBox.SelectedIndex = _dataFields.Count == 0 ? -1 : 0;
        AutomationProperties.SetAutomationId(_valueFieldBox, "PivotSortOptionsValueFieldBox");
        AutomationProperties.SetHelpText(_valueFieldBox, "Choose the value field used for value sorting.");

        foreach (var button in new[] { _labelAscendingButton, _labelDescendingButton, _valueAscendingButton, _valueDescendingButton })
        {
            button.Margin = new Thickness(0, 0, 0, 6);
            button.GroupName = "PivotSortOptions";
            button.Checked += (_, _) => UpdateValueFieldState();
        }

        root.Children.Add(stack);
        return root;
    }

    private void LoadState(PivotSortModel? currentSort)
    {
        if (currentSort is null || currentSort.FieldIndex != _sourceFieldIndex)
            return;

        if (currentSort.Target == PivotSortTarget.Value)
        {
            if (currentSort.Direction == PivotSortDirection.Descending)
                _valueDescendingButton.IsChecked = true;
            else
                _valueAscendingButton.IsChecked = true;

            if (currentSort.DataFieldIndex >= 0 && currentSort.DataFieldIndex < _dataFields.Count)
                _valueFieldBox.SelectedIndex = currentSort.DataFieldIndex;
            return;
        }

        if (currentSort.Direction == PivotSortDirection.Descending)
            _labelDescendingButton.IsChecked = true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if ((_valueAscendingButton.IsChecked == true || _valueDescendingButton.IsChecked == true) &&
            (_valueFieldBox.SelectedIndex < 0 || _valueFieldBox.SelectedIndex >= _dataFields.Count))
        {
            DialogMessageHelper.ShowWarning(this, "Add a PivotTable value field before sorting by values.", Title);
            _valueFieldBox.Focus();
            Keyboard.Focus(_valueFieldBox);
            return;
        }

        ResultSort = CreateResultSort();
        DialogResult = true;
    }

    private PivotSortModel CreateResultSort()
    {
        if (_valueAscendingButton.IsChecked == true || _valueDescendingButton.IsChecked == true)
        {
            return new PivotSortModel(
                PivotSortTarget.Value,
                _valueDescendingButton.IsChecked == true ? PivotSortDirection.Descending : PivotSortDirection.Ascending,
                DataFieldIndex: Math.Max(0, _valueFieldBox.SelectedIndex),
                FieldIndex: _sourceFieldIndex);
        }

        return new PivotSortModel(
            PivotSortTarget.Label,
            _labelDescendingButton.IsChecked == true ? PivotSortDirection.Descending : PivotSortDirection.Ascending,
            FieldIndex: _sourceFieldIndex);
    }

    private void UpdateValueFieldState()
    {
        var enabled = _dataFields.Count > 0 &&
            (_valueAscendingButton.IsChecked == true || _valueDescendingButton.IsChecked == true);
        _valueFieldBox.IsEnabled = enabled;
    }

    private void FocusInitialKeyboardTarget()
    {
        _labelAscendingButton.Focus();
        Keyboard.Focus(_labelAscendingButton);
    }
}
