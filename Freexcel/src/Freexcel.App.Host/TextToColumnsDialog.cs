using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog : Window
{
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
    private readonly RadioButton _formatSkipButton = new() { Content = "Do not import column (_skip)" };
    private readonly ListView _previewGrid = new() { Height = 88 };
    private readonly IReadOnlyList<string> _previewRows;
    private readonly Dictionary<int, TextToColumnsColumnFormat> _columnFormats = [];
    private readonly CellAddress _defaultDestination;
    private int _previewColumnCount = 1;
    private bool _suppressColumnFormatSync;
    private bool _suppressFixedWidthSync;
    private int? _dragBreakIndex;

    public TextToColumnsDialogResult? Result { get; private set; }

    public TextToColumnsDialog(IEnumerable<string>? previewRows = null, CellAddress? defaultDestination = null)
    {
        _previewRows = NormalizePreviewRows(previewRows);
        _defaultDestination = defaultDestination ?? new CellAddress(SheetId.New(), 1, 1);
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
        _formatSkipButton.Checked += (_, _) => StoreSelectedColumnFormat(TextToColumnsColumnFormat.Skip);

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = CreateWizardButtonRow(Accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        body.Children.Add(new TextBlock
        {
            Text = "Text Wizard - Step 2 of 3",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Choose the delimiters that separate your selected text.",
            Margin = new Thickness(0, 0, 0, 10)
        });
        body.Children.Add(CreateOriginalDataTypePanel());
        body.Children.Add(CreateDelimiterPanel());
        body.Children.Add(CreateFixedWidthPanel());
        body.Children.Add(new TextBlock { Text = "Data preview", Margin = new Thickness(0, 10, 0, 4) });
        body.Children.Add(_previewGrid);
        body.Children.Add(CreateColumnFormatPanel());
        body.Children.Add(CreateDestinationPanel());

        Content = root;
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
        root.Children.Add(_formatSkipButton);

        return new GroupBox
        {
            Header = "Column data format",
            Content = root,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0)
        };
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

        return kinds.Count == 0 ? [TextToColumnsDelimiterKind.Comma] : kinds;
    }

    private void Accept()
    {
        try
        {
            if (!TryParseDestination(_destinationBox.Text, _defaultDestination, out var destination))
                throw new ArgumentException("Enter a single destination cell, such as F2.");

            Result = _fixedWidthButton.IsChecked == true
                ? CreateFixedWidthResult(_fixedWidthBreaksBox.Text, destination, BuildColumnFormats(_previewColumnCount))
                : CreateResult(
                    SelectedDelimiterKinds(),
                    _customBox.Text,
                    SelectedTextQualifier(),
                    _treatConsecutiveDelimitersBox.IsChecked == true,
                    destination,
                    BuildColumnFormats(_previewColumnCount));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        System.Windows.Automation.AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));

    private static StackPanel CreateWizardButtonRow(Action accept)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(new Button
        {
            Content = "< _Back",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
            ToolTip = "This dialog opens on the split-options step."
        });
        var nextButton = new Button
        {
            Content = "_Next >",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        nextButton.Click += (_, _) => accept();
        panel.Children.Add(nextButton);
        var finishButton = new Button
        {
            Content = "_Finish",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        finishButton.Click += (_, _) => accept();
        panel.Children.Add(finishButton);
        panel.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });
        return panel;
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

    private void FixedWidthRuler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fixedWidthButton.IsChecked != true)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var x = e.GetPosition(_fixedWidthRuler).X;
        var nearest = FindNearestBreakIndex(positions, x, tolerance: 8);
        _dragBreakIndex = nearest >= 0
            ? nearest
            : AddFixedWidthBreakAt(x);
        _fixedWidthRuler.CaptureMouse();
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBreakIndex is not { } index || e.LeftButton != MouseButtonState.Pressed)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        UpdateFixedWidthBreakPositions(MoveFixedWidthBreakPosition(
            positions,
            index,
            PositionFromRulerX(e.GetPosition(_fixedWidthRuler).X),
            FixedWidthMaxLength()));
        _dragBreakIndex = FindNearestBreakIndex(ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text), e.GetPosition(_fixedWidthRuler).X, tolerance: double.MaxValue);
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragBreakIndex = null;
        _fixedWidthRuler.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fixedWidthButton.IsChecked != true)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var nearest = FindNearestBreakIndex(positions, e.GetPosition(_fixedWidthRuler).X, tolerance: 10);
        if (nearest >= 0)
            UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));
        e.Handled = true;
    }

    private int AddFixedWidthBreakAt(double x)
    {
        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var position = Math.Clamp(PositionFromRulerX(x), 1, FixedWidthMaxLength() - 1);
        var updated = AddFixedWidthBreakPosition(positions, position, FixedWidthMaxLength());
        UpdateFixedWidthBreakPositions(updated);
        return updated.ToList().IndexOf(position);
    }

    private int FindNearestBreakIndex(IReadOnlyList<int> positions, double x, double tolerance)
    {
        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < positions.Count; index++)
        {
            var distance = Math.Abs(RulerXFromPosition(positions[index]) - x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        return nearestDistance <= tolerance ? nearestIndex : -1;
    }

    private void UpdateFixedWidthBreakPositions(IReadOnlyList<int> positions)
    {
        _suppressFixedWidthSync = true;
        try
        {
            _fixedWidthBreaksBox.Text = string.Join(",", positions);
        }
        finally
        {
            _suppressFixedWidthSync = false;
        }

        RefreshPreview();
    }

    private void RefreshFixedWidthRuler()
    {
        _fixedWidthRuler.Children.Clear();
        var sample = _previewRows.OrderByDescending(row => row.Length).FirstOrDefault() ?? string.Empty;
        var text = new TextBlock
        {
            Text = sample,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(4, 28, 4, 0)
        };
        _fixedWidthRuler.Children.Add(text);

        for (var tick = 1; tick < FixedWidthMaxLength(); tick++)
        {
            var x = RulerXFromPosition(tick);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = tick % 5 == 0 ? 10 : 6,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            _fixedWidthRuler.Children.Add(line);
        }

        foreach (var position in ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text))
        {
            var x = RulerXFromPosition(position);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = 56,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            _fixedWidthRuler.Children.Add(line);
        }
    }

    private int FixedWidthMaxLength() =>
        Math.Max(2, _previewRows.Count == 0 ? 2 : _previewRows.Max(row => row.Length));

    private int PositionFromRulerX(double x)
    {
        var width = RulerWidth();
        return (int)Math.Round(Math.Clamp(x, 0, width) / width * FixedWidthMaxLength());
    }

    private double RulerXFromPosition(int position)
    {
        return Math.Clamp(position, 0, FixedWidthMaxLength()) / (double)FixedWidthMaxLength() * RulerWidth();
    }

    private double RulerWidth() => _fixedWidthRuler.ActualWidth > 1 ? _fixedWidthRuler.ActualWidth : 440;

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
