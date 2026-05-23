using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record RemoveDuplicateColumnChoice(uint Offset, string Header, bool IsSelected);

public sealed record RemoveDuplicatesDialogResult(IReadOnlyList<uint> SelectedColumnOffsets, bool HasHeaders = false);

public sealed partial class RemoveDuplicatesDialog : Window
{
    private readonly List<CheckBox> _boxes = [];
    private readonly CheckBox _hasHeadersBox = new() { Content = "_My data has headers", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _columnsPanel = new();
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _headerColumns;
    private readonly IReadOnlyList<RemoveDuplicateColumnChoice> _genericColumns;

    public RemoveDuplicatesDialogResult? Result { get; private set; }

    public RemoveDuplicatesDialog(
        IEnumerable<RemoveDuplicateColumnChoice> columns,
        IEnumerable<RemoveDuplicateColumnChoice>? genericColumns = null,
        bool hasHeaders = true)
    {
        _headerColumns = columns.ToList();
        _genericColumns = genericColumns?.ToList() ?? _headerColumns;
        _hasHeadersBox.IsChecked = hasHeaders;

        Title = "Remove Duplicates";
        Width = 360;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        _hasHeadersBox.Checked += (_, _) => RefreshColumnLabels();
        _hasHeadersBox.Unchecked += (_, _) => RefreshColumnLabels();
        root.Children.Add(_hasHeadersBox);
        root.Children.Add(new TextBlock { Text = "Columns:", Margin = new Thickness(0, 0, 0, 4) });
        var bulkButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var selectAllButton = new Button { Content = "_Select All", Width = 88, Margin = new Thickness(0, 0, 8, 0) };
        selectAllButton.Click += SelectAllButton_Click;
        var unselectAllButton = new Button { Content = "_Unselect All", Width = 88 };
        unselectAllButton.Click += UnselectAllButton_Click;
        bulkButtons.Children.Add(selectAllButton);
        bulkButtons.Children.Add(unselectAllButton);
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _hasHeadersBox.Focus();
        Keyboard.Focus(_hasHeadersBox);
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
                box.Content = label.Header;
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
            MessageBox.Show(this, "Select at least one column.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
