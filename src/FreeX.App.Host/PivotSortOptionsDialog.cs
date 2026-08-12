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
    private readonly Dictionary<PivotSortOptionMode, RadioButton> _modeButtons = [];
    private readonly ComboBox _valueFieldBox = new() { MinWidth = 220 };

    public PivotSortOptionsDialog(
        string fieldCaption,
        int sourceFieldIndex,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        PivotSortModel? currentSort = null)
    {
        _sourceFieldIndex = sourceFieldIndex;
        _dataFields = dataFields;
        ResultSort = PivotSortPlanner.CreateResult(
            PivotSortOptionMode.LabelAscending,
            sourceFieldIndex,
            valueFieldSelectedIndex: -1);

        Title = UiText.Format("PivotSort_Title", fieldCaption);
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
            Text = UiText.Format("PivotSort_Heading", fieldCaption),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var option in PivotSortPlanner.Options)
        {
            var button = new RadioButton
            {
                Content = option.Text.Resolve(UiText.Get),
                Margin = new Thickness(0, 0, 0, 6),
                GroupName = "PivotSortOptions",
            };
            button.Checked += (_, _) => UpdateValueFieldState();
            AutomationProperties.SetAutomationId(button, option.AutomationId);
            _modeButtons.Add(option.Mode, button);
            stack.Children.Add(button);
        }

        var valuePanel = new StackPanel { Margin = new Thickness(18, 8, 0, 0) };
        var valueLabel = new Label
        {
            Content = UiText.Get("PivotSort_ValueField"),
            Target = _valueFieldBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        };
        valuePanel.Children.Add(valueLabel);
        valuePanel.Children.Add(_valueFieldBox);
        stack.Children.Add(valuePanel);

        _valueFieldBox.ItemsSource = _dataFields.Select(field => field.Name).ToList();
        AutomationProperties.SetAutomationId(_valueFieldBox, "PivotSortOptionsValueFieldBox");
        AutomationProperties.SetHelpText(_valueFieldBox, UiText.Get("PivotSortOptions_ValueFieldHelpText"));

        root.Children.Add(stack);
        return root;
    }

    private void LoadState(PivotSortModel? currentSort)
    {
        ButtonFor(PivotSortPlanner.InitialMode(currentSort, _sourceFieldIndex)).IsChecked = true;
        _valueFieldBox.SelectedIndex = PivotSortPlanner.InitialValueFieldIndex(
            currentSort,
            _sourceFieldIndex,
            _dataFields.Count);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PivotSortPlanner.TryValidate(
                CurrentMode(),
                _dataFields.Count,
                _valueFieldBox.SelectedIndex,
                out var error))
        {
            DialogMessageHelper.ShowWarning(
                this,
                (error ?? PivotSortPlanner.ValueSortRequiresValueField).Resolve(UiText.Get),
                Title);
            _valueFieldBox.Focus();
            Keyboard.Focus(_valueFieldBox);
            return;
        }

        ResultSort = PivotSortPlanner.CreateResult(CurrentMode(), _sourceFieldIndex, _valueFieldBox.SelectedIndex);
        DialogResult = true;
    }

    private void UpdateValueFieldState()
    {
        _valueFieldBox.IsEnabled = PivotSortPlanner.ValueFieldEnabled(CurrentMode(), _dataFields.Count);
    }

    private PivotSortOptionMode CurrentMode()
        => _modeButtons.FirstOrDefault(pair => pair.Value.IsChecked == true).Key;

    private RadioButton ButtonFor(PivotSortOptionMode mode) => _modeButtons[mode];

    private void FocusInitialKeyboardTarget()
    {
        var initialButton = ButtonFor(PivotSortOptionMode.LabelAscending);
        initialButton.Focus();
        Keyboard.Focus(initialButton);
    }
}
