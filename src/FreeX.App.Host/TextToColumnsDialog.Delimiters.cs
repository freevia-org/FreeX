using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class TextToColumnsDialog
{
    private GroupBox CreateOriginalDataTypePanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(_delimitedButton);
        panel.Children.Add(_fixedWidthButton);

        return new GroupBox
        {
            Header = UiText.Get("TextToColumns_OriginalDataTypeGroup"),
            Content = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private GroupBox CreateDelimiterPanel()
    {
        var panel = new WrapPanel();
        panel.Children.Add(_tabBox);
        panel.Children.Add(_semicolonBox);
        panel.Children.Add(_commaBox);
        panel.Children.Add(_spaceBox);

        var otherPanel = new StackPanel { Orientation = Orientation.Horizontal };
        otherPanel.Children.Add(_otherBox);
        otherPanel.Children.Add(_customBox);
        panel.Children.Add(otherPanel);

        var qualifierPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        qualifierPanel.Children.Add(new Label
        {
            Content = UiText.Get("TextToColumns_TextQualifierLabel"),
            Target = _textQualifierBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        _textQualifierBox.Items.Add("\"");
        _textQualifierBox.Items.Add("'");
        _textQualifierBox.Items.Add(UiText.Get("TextToColumns_TextQualifierNone"));
        _textQualifierBox.SelectedIndex = 0;
        qualifierPanel.Children.Add(_textQualifierBox);

        var layout = new StackPanel();
        layout.Children.Add(panel);
        layout.Children.Add(qualifierPanel);
        layout.Children.Add(_treatConsecutiveDelimitersBox);

        return new GroupBox
        {
            Header = UiText.Get("TextToColumns_DelimitersGroup"),
            Content = layout,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private DockPanel CreateDestinationPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(new Label
        {
            Content = UiText.Get("TextToColumns_DestinationLabel"),
            Target = _destinationBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 8, 0)
        });
        panel.Children.Add(CreateReferenceEditor(_destinationBox, UiText.Get("TextToColumns_SelectDestinationCell")));
        return panel;
    }

    private IReadOnlyList<TextToColumnsDelimiterKind> SelectedDelimiterKinds()
    {
        var kinds = new List<TextToColumnsDelimiterKind>();
        if (_otherBox.IsChecked == true)
            kinds.Add(TextToColumnsDelimiterKind.Custom);
        if (_tabBox.IsChecked == true)
            kinds.Add(TextToColumnsDelimiterKind.Tab);
        if (_semicolonBox.IsChecked == true)
            kinds.Add(TextToColumnsDelimiterKind.Semicolon);
        if (_spaceBox.IsChecked == true)
            kinds.Add(TextToColumnsDelimiterKind.Space);
        if (_commaBox.IsChecked == true)
            kinds.Add(TextToColumnsDelimiterKind.Comma);

        return kinds;
    }

    public static TextToColumnsRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(CellAddress destination)
    {
        _destinationBox.Text = destination.ToA1();
        FocusRangeSelectionInput(_destinationBox);
    }

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
                FocusRangeSelectionInput(request.Target);
            });

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }
}
