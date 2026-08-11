using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class RemoveDuplicatesDialog : Window
{
    private readonly GridRange _sourceRange;
    private readonly List<CheckBox> _boxes = [];
    private readonly CheckBox _hasHeadersBox = new() { Content = UiText.Get("RemoveDuplicates_MyDataHasHeaders"), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _columnsPanel = new();
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _headerColumns;
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _genericColumns;
    private readonly Button _selectAllButton = new() { Content = UiText.Get("RemoveDuplicates_SelectAll"), Width = 88, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _unselectAllButton = new() { Content = UiText.Get("RemoveDuplicates_UnselectAll"), Width = 88 };

    public RemoveDuplicatesPlan? Result { get; private set; }

    public RemoveDuplicatesDialog(
        GridRange sourceRange,
        IEnumerable<RemoveDuplicateColumnChoice> columns,
        IEnumerable<RemoveDuplicateColumnChoice>? genericColumns = null,
        bool hasHeaders = true)
    {
        _sourceRange = sourceRange;
        _headerColumns = columns.ToList();
        _genericColumns = genericColumns?.ToList() ?? _headerColumns;
        _hasHeadersBox.IsChecked = hasHeaders;
        ApplyAutomationMetadata();

        Title = UiText.Get("RemoveDuplicates_RemoveDuplicates");
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
            Content = UiText.Get("RemoveDuplicates_Columns"),
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
                Content = column.Label,
                Tag = column.Offset,
                IsChecked = column.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            AutomationProperties.SetAutomationId(box, $"RemoveDuplicatesColumn{column.Offset}Box");
            AutomationProperties.SetHelpText(box, UiText.Get("RemoveDuplicates_ColumnHelpText"));
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
        AutomationProperties.SetName(_hasHeadersBox, UiText.Get("RemoveDuplicates_MyDataHasHeadersAutomationName"));
        AutomationProperties.SetAutomationId(_hasHeadersBox, "RemoveDuplicatesHasHeadersBox");
        AutomationProperties.SetHelpText(_hasHeadersBox, UiText.Get("RemoveDuplicates_MyDataHasHeadersHelpText"));

        AutomationProperties.SetName(_columnsPanel, UiText.Get("RemoveDuplicates_ColumnsAutomationName"));
        AutomationProperties.SetAutomationId(_columnsPanel, "RemoveDuplicatesColumnsPanel");
        AutomationProperties.SetHelpText(_columnsPanel, UiText.Get("RemoveDuplicates_ColumnsHelpText"));

        AutomationProperties.SetName(_selectAllButton, UiText.Get("RemoveDuplicates_SelectAllAutomationName"));
        AutomationProperties.SetAutomationId(_selectAllButton, "RemoveDuplicatesSelectAllButton");
        AutomationProperties.SetHelpText(_selectAllButton, UiText.Get("RemoveDuplicates_SelectAllHelpText"));

        AutomationProperties.SetName(_unselectAllButton, UiText.Get("RemoveDuplicates_UnselectAllAutomationName"));
        AutomationProperties.SetAutomationId(_unselectAllButton, "RemoveDuplicatesUnselectAllButton");
        AutomationProperties.SetHelpText(_unselectAllButton, UiText.Get("RemoveDuplicates_UnselectAllHelpText"));
    }

    private void FocusInitialKeyboardTarget()
    {
        _hasHeadersBox.Focus();
        Keyboard.Focus(_hasHeadersBox);
    }

    private void FocusFirstColumnChoice()
    {
        var firstColumnBox = FindFirstColumnBox();
        if (firstColumnBox is null)
            return;

        firstColumnBox.Focus();
        Keyboard.Focus(firstColumnBox);
    }

    private CheckBox? FindFirstColumnBox() =>
        _boxes.Count == 0 ? null : _boxes[0];

    private void RefreshColumnLabels()
    {
        var labels = _hasHeadersBox.IsChecked == true ? _headerColumns : _genericColumns;
        foreach (var box in _boxes)
        {
            if (box.Tag is not uint offset)
                continue;
            var label = FindColumnChoiceByOffset(labels, offset);
            if (label is not null)
            {
                box.Content = label.Label;
                AutomationProperties.SetName(box, UiText.Format("RemoveDuplicates_ColumnAutomationNameFormat", label.Label));
            }
        }
    }

    private static RemoveDuplicateColumnChoice? FindColumnChoiceByOffset(IReadOnlyList<RemoveDuplicateColumnChoice> columns, uint offset)
    {
        foreach (var column in columns)
        {
            if (column.Offset == offset)
                return column;
        }

        return null;
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
        var planResult = RemoveDuplicatesPlanner.CreatePlan(
            _sourceRange,
            _hasHeadersBox.IsChecked == true,
            _boxes.Select(box => new RemoveDuplicateColumnChoice(
            (uint)box.Tag,
            box.Content?.ToString() ?? "",
            box.IsChecked == true)));
        if (!planResult.IsReady || planResult.Plan is null)
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("RemoveDuplicates_SelectAtLeastOneColumn"), Title);
            FocusFirstColumnChoice();
            return;
        }
        Result = planResult.Plan;
        DialogResult = true;
    }
}
