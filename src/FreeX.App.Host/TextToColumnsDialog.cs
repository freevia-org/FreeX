using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record TextToColumnsRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed partial class TextToColumnsDialog : Window
{
    private const double DialogDefaultWidth = TextToColumnsDialogMetrics.WindowWidth;
    private const double DialogDefaultHeight = TextToColumnsDialogMetrics.WindowHeight;
    private const double DialogMinimumWidth = TextToColumnsDialogMetrics.MinimumWindowWidth;
    private const double DialogMinimumHeight = TextToColumnsDialogMetrics.MinimumWindowHeight;

    private static readonly string[] DateColumnFormatLabels = ["MDY", "DMY", "YMD", "MYD", "DYM", "YDM"];

    private readonly RadioButton _delimitedButton = new() { Content = UiText.Get("TextToColumns_Delimited"), IsChecked = true };
    private readonly RadioButton _fixedWidthButton = new() { Content = UiText.Get("TextToColumns_FixedWidth") };
    private readonly CheckBox _tabBox = new() { Content = UiText.Get("TextToColumns_Tab") };
    private readonly CheckBox _semicolonBox = new() { Content = UiText.Get("TextToColumns_Semicolon") };
    private readonly CheckBox _commaBox = new() { Content = UiText.Get("TextToColumns_Comma"), IsChecked = true };
    private readonly CheckBox _spaceBox = new() { Content = UiText.Get("TextToColumns_Space") };
    private readonly CheckBox _otherBox = new() { Content = UiText.Get("TextToColumns_Other") };
    private readonly TextBox _customBox = new() { Width = 48, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _textQualifierBox = new() { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _treatConsecutiveDelimitersBox = new() { Content = UiText.Get("TextToColumns_TreatConsecutiveDelimitersAsOne"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _fixedWidthBreaksBox = new() { Text = "10,20" };
    private readonly Canvas _fixedWidthRuler = new()
    {
        Height = 58,
        Background = Brushes.White,
        ClipToBounds = true
    };
    private readonly TextBox _destinationBox = new() { Width = 120 };
    private readonly ComboBox _formatColumnBox = new() { Width = 110, Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton _formatGeneralButton = new() { Content = UiText.Get("TextToColumns_General"), IsChecked = true };
    private readonly RadioButton _formatTextButton = new() { Content = UiText.Get("TextToColumns_Text") };
    private readonly RadioButton _formatDateButton = new() { Content = UiText.Get("TextToColumns_Date") };
    private readonly ComboBox _dateFormatBox = new() { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
    private readonly RadioButton _formatSkipButton = new() { Content = UiText.Get("TextToColumns_DoNotImportColumnSkip") };
    private readonly TextBox _decimalSeparatorBox = new() { Text = ".", Width = 42 };
    private readonly TextBox _thousandsSeparatorBox = new() { Text = ",", Width = 42 };
    private readonly CheckBox _trailingMinusBox = new() { Content = UiText.Get("TextToColumns_TrailingMinusForNegativeNumbers") };
    private readonly ListView _previewGrid = new() { Height = 88 };
    private readonly ScrollViewer _wizardBodyScrollViewer = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false
    };
    private readonly IReadOnlyList<string> _previewRows;
    private readonly Dictionary<int, TextToColumnsColumnFormat> _columnFormats = [];
    private readonly CellAddress _defaultDestination;
    private readonly Action<TextToColumnsRangeSelectionRequest>? _requestRangeSelection;
    private readonly TextBlock _wizardHeader = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _wizardInstruction = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private Button? _backButton;
    private Button? _nextButton;
    private Button? _finishButton;
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

        Title = UiText.Get("TextToColumns_TextToColumns");
        Width = DialogDefaultWidth;
        Height = DialogDefaultHeight;
        MinWidth = DialogMinimumWidth;
        MinHeight = DialogMinimumHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
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
        _fixedWidthRuler.LostMouseCapture += FixedWidthRuler_LostMouseCapture;
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

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 330 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(_wizardHeader);
        header.Children.Add(_wizardInstruction);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var buttons = CreateWizardButtonRow();
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        var body = new StackPanel();
        _wizardBodyScrollViewer.Content = body;
        Grid.SetRow(_wizardBodyScrollViewer, 1);
        root.Children.Add(_wizardBodyScrollViewer);
        _originalDataTypePanel = CreateOriginalDataTypePanel();
        _delimiterPanel = CreateDelimiterPanel();
        _fixedWidthPanel = CreateFixedWidthPanel();
        _dataPreviewLabel = new TextBlock { Text = UiText.Get("TextToColumns_DataPreview"), Margin = new Thickness(0, 10, 0, 4) };
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    private GroupBox CreateFixedWidthPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = UiText.Get("TextToColumns_ClickTheRulerToCreateABreakLineDragToMoveItOrRightClickALineToRemoveIt"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(_fixedWidthRuler);

        var breakRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        breakRow.Children.Add(new Label
        {
            Content = UiText.Get("TextToColumns_Breaks"),
            Target = _fixedWidthBreaksBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 8, 0)
        });
        _fixedWidthBreaksBox.Width = 160;
        breakRow.Children.Add(_fixedWidthBreaksBox);
        panel.Children.Add(breakRow);

        return new GroupBox
        {
            Header = UiText.Get("TextToColumns_FixedWidth2"),
            Content = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private void Accept()
    {
        try
        {
            if (!TryParseDestination(_destinationBox.Text, _defaultDestination, out var destination))
            {
                ShowValidation(TextToColumnsDialogValidationIssue.InvalidDestination);
                return;
            }

            if (_fixedWidthButton.IsChecked == true &&
                !TryParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text, FixedWidthMaxLength(), out _))
            {
                ShowValidation(TextToColumnsDialogValidationIssue.MissingFixedWidthBreaks);
                return;
            }

            if (_fixedWidthButton.IsChecked != true && SelectedDelimiterKinds().Count == 0)
            {
                ShowValidation(TextToColumnsDialogValidationIssue.MissingDelimiter);
                return;
            }

            if (_fixedWidthButton.IsChecked != true && _otherBox.IsChecked == true && string.IsNullOrEmpty(_customBox.Text))
            {
                ShowValidation(TextToColumnsDialogValidationIssue.MissingCustomDelimiter);
                return;
            }

            if (!TryParseAdvancedSeparator(_decimalSeparatorBox.Text, out _))
            {
                ShowValidation(TextToColumnsDialogValidationIssue.InvalidDecimalSeparator);
                return;
            }

            if (!TryParseAdvancedSeparator(_thousandsSeparatorBox.Text, out _))
            {
                ShowValidation(TextToColumnsDialogValidationIssue.InvalidThousandsSeparator);
                return;
            }

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
            DialogMessageHelper.ShowWarning(this, ex.Message, Title);
        }
    }

    private void ShowValidation(TextToColumnsDialogValidationIssue issue)
    {
        var presentation = TextToColumnsDialogPlanner.DescribeValidationIssue(issue);
        DialogMessageHelper.ShowWarning(
            this,
            presentation.Message.Resolve(UiText.Get, UiText.Format),
            Title);
        switch (presentation.FocusTarget)
        {
            case TextToColumnsDialogFocusTarget.FixedWidthBreaks:
                FocusInvalidFixedWidthBreaksInput();
                break;
            case TextToColumnsDialogFocusTarget.DelimiterSelection:
                FocusInvalidDelimiterSelectionInput();
                break;
            case TextToColumnsDialogFocusTarget.CustomDelimiter:
                FocusInvalidCustomDelimiterInput();
                break;
            case TextToColumnsDialogFocusTarget.DecimalSeparator:
                FocusInvalidAdvancedSeparatorInput(_decimalSeparatorBox);
                break;
            case TextToColumnsDialogFocusTarget.ThousandsSeparator:
                FocusInvalidAdvancedSeparatorInput(_thousandsSeparatorBox);
                break;
            default:
                FocusInvalidDestinationInput();
                break;
        }
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
                    .Select(row => TextToColumnsApplyPlanner.SplitFixedWidthText(row, positions).ToArray())
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
                    .Select(row => TextToColumnsApplyPlanner.SplitText(
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
                .Select(row => TextToColumnsApplyPlanner.SplitText(row, ",").ToArray())
                .ToList();
        }

        var columnCount = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(row => row.Length));
        _previewColumnCount = columnCount;
        var view = new GridView();
        for (var index = 0; index < columnCount; index++)
        {
            view.Columns.Add(new GridViewColumn
            {
                Header = UiText.Format("TextToColumns_ColumnHeader", index + 1),
                DisplayMemberBinding = new Binding($"[{index}]"),
                Width = index == 0 ? 140 : 100
            });
        }

        _previewGrid.View = view;
        _previewGrid.ItemsSource = rows.Select(row => PadRow(row, columnCount)).ToList();
        RefreshFixedWidthRuler();
        RefreshColumnFormatChoices(columnCount);
    }


    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_customBox, "Other delimiter");
        AutomationProperties.SetName(_textQualifierBox, "Text qualifier");
        AutomationProperties.SetName(_fixedWidthBreaksBox, "Fixed width breaks");
        AutomationProperties.SetName(_destinationBox, "Destination");
        AutomationProperties.SetName(_formatColumnBox, "Column");
        AutomationProperties.SetName(_dateFormatBox, "Date format");
        AutomationProperties.SetName(_decimalSeparatorBox, "Decimal separator");
        AutomationProperties.SetName(_thousandsSeparatorBox, "Thousands separator");
        AutomationProperties.SetName(_previewGrid, "Data preview");
    }
}
