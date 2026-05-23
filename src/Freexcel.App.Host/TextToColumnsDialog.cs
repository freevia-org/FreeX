using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record TextToColumnsRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed partial class TextToColumnsDialog : Window
{
    private static readonly string[] DateColumnFormatLabels = ["MDY", "DMY", "YMD", "MYD", "DYM", "YDM"];

    private readonly RadioButton _delimitedButton = new() { Content = "_Delimited", IsChecked = true };
    private readonly RadioButton _fixedWidthButton = new() { Content = "Fi_xed width" };
    private readonly CheckBox _tabBox = new() { Content = "_Tab" };
    private readonly CheckBox _semicolonBox = new() { Content = "_Semicolon" };
    private readonly CheckBox _commaBox = new() { Content = "_Comma", IsChecked = true };
    private readonly CheckBox _spaceBox = new() { Content = "S_pace" };
    private readonly CheckBox _otherBox = new() { Content = "_Other:" };
    private readonly TextBox _customBox = new() { Width = 48, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _textQualifierBox = new() { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _treatConsecutiveDelimitersBox = new() { Content = "_Treat consecutive delimiters as one", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _fixedWidthBreaksBox = new() { Text = "10,20" };
    private readonly Canvas _fixedWidthRuler = new()
    {
        Height = 58,
        Background = Brushes.White,
        ClipToBounds = true
    };
    private readonly TextBox _destinationBox = new() { Width = 120 };
    private readonly ComboBox _formatColumnBox = new() { Width = 110, Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton _formatGeneralButton = new() { Content = "_General", IsChecked = true };
    private readonly RadioButton _formatTextButton = new() { Content = "_Text" };
    private readonly RadioButton _formatDateButton = new() { Content = "_Date:" };
    private readonly ComboBox _dateFormatBox = new() { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
    private readonly RadioButton _formatSkipButton = new() { Content = "Do not import column (_skip)" };
    private readonly TextBox _decimalSeparatorBox = new() { Text = ".", Width = 42 };
    private readonly TextBox _thousandsSeparatorBox = new() { Text = ",", Width = 42 };
    private readonly CheckBox _trailingMinusBox = new() { Content = "_Trailing minus for negative numbers" };
    private readonly ListView _previewGrid = new() { Height = 88 };
    private readonly IReadOnlyList<string> _previewRows;
    private readonly Dictionary<int, TextToColumnsColumnFormat> _columnFormats = [];
    private readonly CellAddress _defaultDestination;
    private readonly Action<TextToColumnsRangeSelectionRequest>? _requestRangeSelection;
    private readonly TextBlock _wizardHeader = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _wizardInstruction = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private Button? _backButton;
    private Button? _nextButton;
    private FrameworkElement? _originalDataTypePanel;
    private FrameworkElement? _delimiterPanel;
    private FrameworkElement? _fixedWidthPanel;
    private FrameworkElement? _dataPreviewLabel;
    private FrameworkElement? _columnFormatPanel;
    private FrameworkElement? _destinationPanel;
    private int _previewColumnCount = 1;
    private int _wizardStep = 1;
    private bool _suppressColumnFormatSync;
    private bool _suppressFixedWidthSync;
    private int? _dragBreakIndex;

    public TextToColumnsDialogResult? Result { get; private set; }
    public TextToColumnsRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public TextToColumnsDialog(
        IEnumerable<string>? previewRows = null,
        CellAddress? defaultDestination = null,
        Action<TextToColumnsRangeSelectionRequest>? requestRangeSelection = null)
    {
        _previewRows = NormalizePreviewRows(previewRows);
        _defaultDestination = defaultDestination ?? new CellAddress(SheetId.New(), 1, 1);
        _requestRangeSelection = requestRangeSelection;
        _destinationBox.Text = _defaultDestination.ToA1();

        Title = "Text to Columns";
        Width = 500;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _otherBox.Checked += (_, _) => _customBox.Focus();
        foreach (var box in new[] { _tabBox, _semicolonBox, _commaBox, _spaceBox, _otherBox })
        {
            box.Checked += (_, _) => RefreshMode();
            box.Unchecked += (_, _) => RefreshMode();
        }
        _delimitedButton.Checked += (_, _) => RefreshMode();
        _fixedWidthButton.Checked += (_, _) => RefreshMode();
        _customBox.TextChanged += (_, _) => RefreshPreview();
        _textQualifierBox.SelectionChanged += (_, _) => RefreshPreview();
        _treatConsecutiveDelimitersBox.Checked += (_, _) => RefreshPreview();
        _treatConsecutiveDelimitersBox.Unchecked += (_, _) => RefreshPreview();
        _fixedWidthBreaksBox.TextChanged += (_, _) =>
        {
            if (!_suppressFixedWidthSync)
                RefreshPreview();
        };
        _fixedWidthRuler.MouseLeftButtonDown += FixedWidthRuler_MouseLeftButtonDown;
        _fixedWidthRuler.MouseMove += FixedWidthRuler_MouseMove;
        _fixedWidthRuler.MouseLeftButtonUp += FixedWidthRuler_MouseLeftButtonUp;
        _fixedWidthRuler.MouseRightButtonDown += FixedWidthRuler_MouseRightButtonDown;
        _formatColumnBox.SelectionChanged += (_, _) => SyncColumnFormatControls();
        _formatGeneralButton.Checked += (_, _) => StoreSelectedColumnFormat(TextToColumnsColumnFormat.General);
        _formatTextButton.Checked += (_, _) => StoreSelectedColumnFormat(TextToColumnsColumnFormat.Text);
        _formatDateButton.Checked += (_, _) => StoreSelectedColumnFormat(SelectedDateColumnFormat());
        _dateFormatBox.SelectionChanged += (_, _) =>
        {
            if (_formatDateButton.IsChecked == true)
                StoreSelectedColumnFormat(SelectedDateColumnFormat());
        };
        _formatSkipButton.Checked += (_, _) => StoreSelectedColumnFormat(TextToColumnsColumnFormat.Skip);

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = CreateWizardButtonRow();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        body.Children.Add(_wizardHeader);
        body.Children.Add(_wizardInstruction);
        _originalDataTypePanel = CreateOriginalDataTypePanel();
        _delimiterPanel = CreateDelimiterPanel();
        _fixedWidthPanel = CreateFixedWidthPanel();
        _dataPreviewLabel = new TextBlock { Text = "Data preview", Margin = new Thickness(0, 10, 0, 4) };
        _columnFormatPanel = CreateColumnFormatPanel();
        _destinationPanel = CreateDestinationPanel();
        body.Children.Add(_originalDataTypePanel);
        body.Children.Add(_delimiterPanel);
        body.Children.Add(_fixedWidthPanel);
        body.Children.Add(_dataPreviewLabel);
        body.Children.Add(_previewGrid);
        body.Children.Add(_columnFormatPanel);
        body.Children.Add(_destinationPanel);

        Content = root;
        UpdateWizardStep();
        RefreshMode();
        RefreshPreview();
    }

    private GroupBox CreateOriginalDataTypePanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(_delimitedButton);
        panel.Children.Add(_fixedWidthButton);

        return new GroupBox
        {
            Header = "Original data type",
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
            Content = "Text _qualifier:",
            Target = _textQualifierBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        _textQualifierBox.Items.Add("\"");
        _textQualifierBox.Items.Add("'");
        _textQualifierBox.Items.Add("{none}");
        _textQualifierBox.SelectedIndex = 0;
        qualifierPanel.Children.Add(_textQualifierBox);

        var layout = new StackPanel();
        layout.Children.Add(panel);
        layout.Children.Add(qualifierPanel);
        layout.Children.Add(_treatConsecutiveDelimitersBox);

        return new GroupBox
        {
            Header = "Delimiters",
            Content = layout,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private GroupBox CreateFixedWidthPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Click the ruler to create a break line, drag to move it, or right-click a line to remove it.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(_fixedWidthRuler);

        var breakRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        breakRow.Children.Add(new Label
        {
            Content = "_Breaks:",
            Target = _fixedWidthBreaksBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 8, 0)
        });
        _fixedWidthBreaksBox.Width = 160;
        breakRow.Children.Add(_fixedWidthBreaksBox);
        panel.Children.Add(breakRow);

        return new GroupBox
        {
            Header = "Fixed width",
            Content = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private DockPanel CreateDestinationPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(new Label
        {
            Content = "_Destination:",
            Target = _destinationBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 8, 0)
        });
        panel.Children.Add(CreateReferenceEditor(_destinationBox, "Select destination cell"));
        return panel;
    }

    private GroupBox CreateColumnFormatPanel()
    {
        var root = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Label
        {
            Content = "_Column:",
            Target = _formatColumnBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 8, 0)
        });
        row.Children.Add(_formatColumnBox);
        root.Children.Add(row);
        root.Children.Add(_formatGeneralButton);
        root.Children.Add(_formatTextButton);
        root.Children.Add(CreateDateFormatRow());
        root.Children.Add(_formatSkipButton);
        root.Children.Add(CreateAdvancedOptionsPanel());

        return new GroupBox
        {
            Header = "Column data format",
            Content = root,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0)
        };
    }

    private StackPanel CreateDateFormatRow()
    {
        foreach (var value in DateColumnFormatLabels)
            _dateFormatBox.Items.Add(value);
        _dateFormatBox.SelectedIndex = 0;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_formatDateButton);
        row.Children.Add(_dateFormatBox);
        return row;
    }

    private GroupBox CreateAdvancedOptionsPanel()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddAdvancedLabel(grid, "_Decimal separator:", _decimalSeparatorBox, 0, 0);
        AddAdvancedTextBox(grid, _decimalSeparatorBox, 0, 1);
        AddAdvancedLabel(grid, "_Thousands separator:", _thousandsSeparatorBox, 0, 2);
        AddAdvancedTextBox(grid, _thousandsSeparatorBox, 0, 3);
        Grid.SetRow(_trailingMinusBox, 1);
        Grid.SetColumnSpan(_trailingMinusBox, 4);
        _trailingMinusBox.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(_trailingMinusBox);

        return new GroupBox
        {
            Header = "Advanced",
            Content = grid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0)
        };
    }

    private static void AddAdvancedLabel(Grid grid, string content, Control target, int row, int column)
    {
        var label = new Label
        {
            Content = content,
            Target = target,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 6, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static void AddAdvancedTextBox(Grid grid, TextBox textBox, int row, int column)
    {
        textBox.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, column);
        grid.Children.Add(textBox);
    }

    private TextToColumnsAdvancedOptions BuildAdvancedOptions() =>
        new(NormalizeSeparator(_decimalSeparatorBox.Text, "."),
            NormalizeSeparator(_thousandsSeparatorBox.Text, ","),
            _trailingMinusBox.IsChecked == true);

    private static string NormalizeSeparator(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

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

        return kinds.Count == 0 ? [TextToColumnsDelimiterKind.Comma] : kinds;
    }

    private void Accept()
    {
        try
        {
            if (!TryParseDestination(_destinationBox.Text, _defaultDestination, out var destination))
                throw new ArgumentException("Enter a single destination cell, such as F2.");

            Result = _fixedWidthButton.IsChecked == true
                ? CreateFixedWidthResult(_fixedWidthBreaksBox.Text, destination, BuildColumnFormats(_previewColumnCount), BuildAdvancedOptions())
                : CreateResult(
                    SelectedDelimiterKinds(),
                    _customBox.Text,
                    SelectedTextQualifier(),
                    _treatConsecutiveDelimitersBox.IsChecked == true,
                    destination,
                    BuildColumnFormats(_previewColumnCount),
                    BuildAdvancedOptions());
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static TextToColumnsRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
            });

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));

    private StackPanel CreateWizardButtonRow()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
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

    private void RefreshPreview()
    {
        IReadOnlyList<string[]> rows;
        try
        {
            if (_fixedWidthButton.IsChecked == true)
            {
                var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
                rows = _previewRows
                    .Select(row => TextToColumnsPlanner.SplitFixedWidthText(row, positions).ToArray())
                    .ToList();
            }
            else
            {
                var result = CreateResult(
                    SelectedDelimiterKinds(),
                    _customBox.Text,
                    SelectedTextQualifier(),
                    _treatConsecutiveDelimitersBox.IsChecked == true,
                    _defaultDestination);
                rows = _previewRows
                    .Select(row => TextToColumnsPlanner.SplitText(
                        row,
                        result.Delimiters,
                        result.TextQualifierChar,
                        result.TreatConsecutiveDelimitersAsOne).ToArray())
                    .ToList();
            }
        }
        catch
        {
            rows = _previewRows
                .Select(row => TextToColumnsPlanner.SplitText(row, ",").ToArray())
                .ToList();
        }

        var columnCount = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(row => row.Length));
        _previewColumnCount = columnCount;
        var view = new GridView();
        for (var index = 0; index < columnCount; index++)
        {
            view.Columns.Add(new GridViewColumn
            {
                Header = $"Column {index + 1}",
                DisplayMemberBinding = new Binding($"[{index}]"),
                Width = index == 0 ? 140 : 100
            });
        }

        _previewGrid.View = view;
        _previewGrid.ItemsSource = rows.Select(row => PadRow(row, columnCount)).ToList();
        RefreshFixedWidthRuler();
        RefreshColumnFormatChoices(columnCount);
    }

    private TextToColumnsTextQualifier SelectedTextQualifier() =>
        _textQualifierBox.SelectedIndex switch
        {
            1 => TextToColumnsTextQualifier.SingleQuote,
            2 => TextToColumnsTextQualifier.None,
            _ => TextToColumnsTextQualifier.DoubleQuote
        };

    private void RefreshColumnFormatChoices(int columnCount)
    {
        var selectedIndex = Math.Max(0, _formatColumnBox.SelectedIndex);
        _suppressColumnFormatSync = true;
        try
        {
            _formatColumnBox.ItemsSource = Enumerable.Range(1, columnCount)
                .Select(index => $"Column {index}")
                .ToList();
            _formatColumnBox.SelectedIndex = Math.Min(selectedIndex, columnCount - 1);
        }
        finally
        {
            _suppressColumnFormatSync = false;
        }

        SyncColumnFormatControls();
    }

    private void SyncColumnFormatControls()
    {
        if (_suppressColumnFormatSync)
            return;

        var columnIndex = Math.Max(0, _formatColumnBox.SelectedIndex);
        var format = _columnFormats.TryGetValue(columnIndex, out var stored)
            ? stored
            : TextToColumnsColumnFormat.General;

        _suppressColumnFormatSync = true;
        try
        {
            _formatGeneralButton.IsChecked = format == TextToColumnsColumnFormat.General;
            _formatTextButton.IsChecked = format == TextToColumnsColumnFormat.Text;
            _formatDateButton.IsChecked = IsDateColumnFormat(format);
            _dateFormatBox.SelectedItem = DateColumnFormatLabel(format);
            _formatSkipButton.IsChecked = format == TextToColumnsColumnFormat.Skip;
        }
        finally
        {
            _suppressColumnFormatSync = false;
        }
    }

    private void StoreSelectedColumnFormat(TextToColumnsColumnFormat format)
    {
        if (_suppressColumnFormatSync || _formatColumnBox.SelectedIndex < 0)
            return;

        var columnIndex = _formatColumnBox.SelectedIndex;
        if (format == TextToColumnsColumnFormat.General)
            _columnFormats.Remove(columnIndex);
        else
            _columnFormats[columnIndex] = format;
    }

    private TextToColumnsColumnFormat SelectedDateColumnFormat() =>
        (_dateFormatBox.SelectedItem as string) switch
        {
            "DMY" => TextToColumnsColumnFormat.DateDMY,
            "YMD" => TextToColumnsColumnFormat.DateYMD,
            "MYD" => TextToColumnsColumnFormat.DateMYD,
            "DYM" => TextToColumnsColumnFormat.DateDYM,
            "YDM" => TextToColumnsColumnFormat.DateYDM,
            _ => TextToColumnsColumnFormat.DateMDY
        };

    private static bool IsDateColumnFormat(TextToColumnsColumnFormat format) =>
        format is TextToColumnsColumnFormat.DateMDY
            or TextToColumnsColumnFormat.DateDMY
            or TextToColumnsColumnFormat.DateYMD
            or TextToColumnsColumnFormat.DateMYD
            or TextToColumnsColumnFormat.DateDYM
            or TextToColumnsColumnFormat.DateYDM;

    private static string DateColumnFormatLabel(TextToColumnsColumnFormat format) =>
        format switch
        {
            TextToColumnsColumnFormat.DateDMY => "DMY",
            TextToColumnsColumnFormat.DateYMD => "YMD",
            TextToColumnsColumnFormat.DateMYD => "MYD",
            TextToColumnsColumnFormat.DateDYM => "DYM",
            TextToColumnsColumnFormat.DateYDM => "YDM",
            _ => "MDY"
        };

    private IReadOnlyList<TextToColumnsColumnFormat> BuildColumnFormats(int columnCount)
    {
        var formats = Enumerable.Range(0, columnCount)
            .Select(index => _columnFormats.TryGetValue(index, out var format)
                ? format
                : TextToColumnsColumnFormat.General)
            .ToList();
        return NormalizeColumnFormats(formats);
    }

}
