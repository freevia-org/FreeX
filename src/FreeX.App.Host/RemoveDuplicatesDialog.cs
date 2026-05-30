using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record RemoveDuplicateColumnChoice(uint Offset, string Header, bool IsSelected);

public sealed record RemoveDuplicatesDialogResult(IReadOnlyList<uint> SelectedColumnOffsets, bool HasHeaders = false);

public sealed partial class RemoveDuplicatesDialog : Window
{
    private readonly List<CheckBox> _boxes = [];
    private readonly CheckBox _hasHeadersBox = new() { Content = "_My data has headers", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _columnsPanel = new();
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _headerColumns;
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _genericColumns;
    private readonly Button _selectAllButton = new() { Content = "_Select All", Width = 88, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _unselectAllButton = new() { Content = "_Unselect All", Width = 88 };

    public RemoveDuplicatesDialogResult? Result { get; private set; }

    public RemoveDuplicatesDialog(
        IEnumerable<RemoveDuplicateColumnChoice> columns,
        IEnumerable<RemoveDuplicateColumnChoice>? genericColumns = null,
        bool hasHeaders = true)
    {
        _headerColumns = columns.ToList();
        _genericColumns = genericColumns?.ToList() ?? _headerColumns;
        _hasHeadersBox.IsChecked = hasHeaders;
        ApplyAutomationMetadata();

        Title = "Remove Duplicates";
        Width = 360;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        _hasHeadersBox.Checked += (_, _) => RefreshColumnLabels();
        _hasHeadersBox.Unchecked += (_, _) => RefreshColumnLabels();
        _columnsPanel.Focusable = true;
        _columnsPanel.GotKeyboardFocus += (_, _) => FocusFirstColumnChoice();
        root.Children.Add(_hasHeadersBox);
        root.Children.Add(new Label
        {
            Content = "_Columns:",
            Target = _columnsPanel,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0)
        });
        var bulkButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _selectAllButton.Click += SelectAllButton_Click;
        _unselectAllButton.Click += UnselectAllButton_Click;
        bulkButtons.Children.Add(_selectAllButton);
        bulkButtons.Children.Add(_unselectAllButton);
        root.Children.Add(bulkButtons);

        foreach (var column in _headerColumns)
        {
            var box = new CheckBox
            {
                Content = column.Header,
                Tag = column.Offset,
                IsChecked = column.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            AutomationProperties.SetAutomationId(box, $"RemoveDuplicatesColumn{column.Offset}Box");
            AutomationProperties.SetHelpText(box, "Select to include this column when identifying duplicate rows.");
            box.Checked += (_, _) => RefreshBulkButtonState();
            box.Unchecked += (_, _) => RefreshBulkButtonState();
            _boxes.Add(box);
            _columnsPanel.Children.Add(box);
        }
        root.Children.Add(new ScrollViewer
        {
            Content = _columnsPanel,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
        RefreshColumnLabels();
        RefreshBulkButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetName(_hasHeadersBox, "My data has headers");
        AutomationProperties.SetAutomationId(_hasHeadersBox, "RemoveDuplicatesHasHeadersBox");
        AutomationProperties.SetHelpText(_hasHeadersBox, "Select when the first row contains column headers.");

        AutomationProperties.SetName(_columnsPanel, "Columns");
        AutomationProperties.SetAutomationId(_columnsPanel, "RemoveDuplicatesColumnsPanel");
        AutomationProperties.SetHelpText(_columnsPanel, "Choose the columns used to identify duplicate rows.");

        AutomationProperties.SetName(_selectAllButton, "Select all columns");
        AutomationProperties.SetAutomationId(_selectAllButton, "RemoveDuplicatesSelectAllButton");
        AutomationProperties.SetHelpText(_selectAllButton, "Select every column for duplicate detection.");

        AutomationProperties.SetName(_unselectAllButton, "Unselect all columns");
        AutomationProperties.SetAutomationId(_unselectAllButton, "RemoveDuplicatesUnselectAllButton");
        AutomationProperties.SetHelpText(_unselectAllButton, "Clear every column selection.");
    }

    private void FocusInitialKeyboardTarget()
    {
        _hasHeadersBox.Focus();
        Keyboard.Focus(_hasHeadersBox);
    }

    private void FocusFirstColumnChoice()
    {
        var firstColumnBox = _boxes.FirstOrDefault();
        if (firstColumnBox is null)
            return;

        firstColumnBox.Focus();
        Keyboard.Focus(firstColumnBox);
    }

    private void RefreshColumnLabels()
    {
        var labels = _hasHeadersBox.IsChecked == true ? _headerColumns : _genericColumns;
        foreach (var box in _boxes)
        {
            if (box.Tag is not uint offset)
                continue;
            var label = labels.FirstOrDefault(column => column.Offset == offset);
            if (label is not null)
            {
                box.Content = label.Header;
                AutomationProperties.SetName(box, $"{label.Header} column");
            }
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetColumnSelection(true);
    }

    private void UnselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetColumnSelection(false);
    }

    private void SetColumnSelection(bool isSelected)
    {
        foreach (var box in _boxes)
            box.IsChecked = isSelected;
        RefreshBulkButtonState();
    }

    private void RefreshBulkButtonState()
    {
        var selectedCount = _boxes.Count(box => box.IsChecked == true);
        _selectAllButton.IsEnabled = selectedCount < _boxes.Count;
        _unselectAllButton.IsEnabled = selectedCount > 0;
    }

    private void Accept()
    {
        var selected = CreateResult(_boxes.Select(box => new RemoveDuplicateColumnChoice(
            (uint)box.Tag,
            box.Content?.ToString() ?? "",
            box.IsChecked == true)));
        Result = selected with { HasHeaders = _hasHeadersBox.IsChecked == true };
        if (Result.SelectedColumnOffsets.Count == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Select at least one column.", Title);
            FocusFirstColumnChoice();
            return;
        }
        DialogResult = true;
    }
}
