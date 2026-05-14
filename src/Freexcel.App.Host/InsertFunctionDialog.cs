using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class InsertFunctionDialog : Window
{
    public string? SelectedFormula { get; private set; }

    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;
    private readonly TextBlock _descText;

    private static readonly (string Name, string Desc)[] Functions =
    [
        ("SUM",      "Adds all numbers in a range."),
        ("AVERAGE",  "Returns the average of numbers in a range."),
        ("COUNT",    "Counts the number of cells with numbers."),
        ("COUNTA",   "Counts non-empty cells."),
        ("MAX",      "Returns the maximum value."),
        ("MIN",      "Returns the minimum value."),
        ("IF",       "Returns one value if condition is true, another if false."),
        ("IFS",      "Checks multiple conditions and returns the first true result."),
        ("VLOOKUP",  "Looks up a value in the first column of a range."),
        ("HLOOKUP",  "Looks up a value in the first row of a range."),
        ("INDEX",    "Returns a value from a range by row and column number."),
        ("MATCH",    "Returns the relative position of a value in a range."),
        ("XLOOKUP",  "Searches a range and returns an item from another range."),
        ("CONCATENATE", "Joins text strings together."),
        ("CONCAT",   "Joins text strings together (modern version)."),
        ("LEFT",     "Returns leftmost characters from a text string."),
        ("RIGHT",    "Returns rightmost characters from a text string."),
        ("MID",      "Returns characters from the middle of a text string."),
        ("LEN",      "Returns the number of characters in a text string."),
        ("TRIM",     "Removes extra spaces from text."),
        ("UPPER",    "Converts text to uppercase."),
        ("LOWER",    "Converts text to lowercase."),
        ("TEXT",     "Formats a number as text with a format string."),
        ("NOW",      "Returns the current date and time."),
        ("TODAY",    "Returns the current date."),
        ("DATE",     "Returns a date value from year, month, day."),
        ("YEAR",     "Returns the year from a date."),
        ("MONTH",    "Returns the month from a date."),
        ("DAY",      "Returns the day from a date."),
        ("ROUND",    "Rounds a number to a specified number of digits."),
        ("INT",      "Rounds a number down to the nearest integer."),
        ("ABS",      "Returns the absolute value of a number."),
        ("SQRT",     "Returns the square root of a number."),
        ("POWER",    "Returns a number raised to a power."),
        ("MOD",      "Returns the remainder from division."),
        ("SUMIF",    "Adds cells that meet a condition."),
        ("COUNTIF",  "Counts cells that meet a condition."),
        ("AVERAGEIF","Averages cells that meet a condition."),
        ("SUMIFS",   "Adds cells that meet multiple conditions."),
        ("COUNTIFS", "Counts cells that meet multiple conditions."),
        ("AND",      "Returns TRUE if all conditions are true."),
        ("OR",       "Returns TRUE if any condition is true."),
        ("NOT",      "Reverses the logic of an argument."),
        ("IFERROR",  "Returns a value if there is an error, another value otherwise."),
        ("ISBLANK",  "Returns TRUE if the cell is blank."),
        ("ISNUMBER", "Returns TRUE if the value is a number."),
        ("ISTEXT",   "Returns TRUE if the value is text."),
    ];

    public InsertFunctionDialog()
    {
        Title = "Insert Function";
        Width = 480; Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var outer = new DockPanel { Margin = new Thickness(12) };

        // Search row
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(searchPanel, Dock.Top);
        searchPanel.Children.Add(new Label { Content = "Search:", VerticalContentAlignment = VerticalAlignment.Center });
        _searchBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_searchBox, Dock.Left);
        searchPanel.Children.Add(_searchBox);
        _searchBox.TextChanged += (_, _) => RefreshList();

        // Description panel at bottom
        _descText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = System.Windows.Media.Brushes.DimGray,
            Height = 40
        };
        DockPanel.SetDock(_descText, Dock.Bottom);

        // Button row
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(btnRow, Dock.Bottom);
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        // Function list
        _listBox = new ListBox();
        _listBox.SelectionChanged += (_, _) =>
        {
            if (_listBox.SelectedItem is string name)
            {
                var entry = Functions.FirstOrDefault(f => f.Name == name);
                _descText.Text = entry.Desc;
            }
        };
        _listBox.MouseDoubleClick += (_, _) => Ok_Click(null!, null!);

        outer.Children.Add(searchPanel);
        outer.Children.Add(btnRow);
        outer.Children.Add(_descText);
        outer.Children.Add(_listBox);

        Content = outer;

        Loaded += (_, _) => { RefreshList(); _searchBox.Focus(); };
    }

    private void RefreshList()
    {
        var filter = _searchBox.Text.Trim().ToUpperInvariant();
        _listBox.Items.Clear();
        foreach (var (name, _) in Functions)
        {
            if (string.IsNullOrEmpty(filter) || name.Contains(filter))
                _listBox.Items.Add(name);
        }
        if (_listBox.Items.Count > 0) _listBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is string name)
        {
            SelectedFormula = name + "()";
            DialogResult = true;
        }
    }
}
