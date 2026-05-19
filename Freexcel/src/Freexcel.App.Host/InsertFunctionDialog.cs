using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Formula;

namespace Freexcel.App.Host;

public sealed record InsertFunctionCatalogEntry(string Name, string Category, string Description);

public sealed class InsertFunctionDialog : Window
{
    public string? SelectedFormula { get; private set; }

    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryBox;
    private readonly TextBlock _descText;
    private readonly IReadOnlyList<InsertFunctionCatalogEntry> _catalog;

    private const string AllCategory = "All";

    public InsertFunctionDialog()
    {
        Title = "Insert Function";
        Width = 480; Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        _catalog = BuildCatalog();

        var outer = new DockPanel { Margin = new Thickness(12) };

        var searchPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition());
        DockPanel.SetDock(searchPanel, Dock.Top);

        searchPanel.Children.Add(new Label { Content = "Category:", VerticalContentAlignment = VerticalAlignment.Center });
        _categoryBox = new ComboBox { Margin = new Thickness(4, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _categoryBox.ItemsSource = new[] { AllCategory }.Concat(_catalog.Select(entry => entry.Category).Distinct().OrderBy(category => category)).ToArray();
        _categoryBox.SelectedItem = AllCategory;
        _categoryBox.SelectionChanged += (_, _) => RefreshList();
        Grid.SetColumn(_categoryBox, 1);
        searchPanel.Children.Add(_categoryBox);

        var searchLabel = new Label { Content = "Search:", VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(searchLabel, 2);
        searchPanel.Children.Add(searchLabel);
        _searchBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_searchBox, 3);
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
        _listBox.DisplayMemberPath = nameof(InsertFunctionCatalogEntry.Name);
        _listBox.SelectionChanged += (_, _) =>
        {
            _descText.Text = _listBox.SelectedItem is InsertFunctionCatalogEntry entry
                ? entry.Description
                : "";
        };
        _listBox.MouseDoubleClick += (_, _) => Ok_Click(null!, null!);

        outer.Children.Add(searchPanel);
        outer.Children.Add(btnRow);
        outer.Children.Add(_descText);
        outer.Children.Add(_listBox);

        Content = outer;

        Loaded += (_, _) => { RefreshList(); _searchBox.Focus(); };
    }

    public static IReadOnlyList<InsertFunctionCatalogEntry> BuildCatalog() =>
        BuiltInFunctions.Names
            .Select(name => name.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new InsertFunctionCatalogEntry(name, GetCategory(name), GetDescription(name)))
            .ToArray();

    public static IReadOnlyList<InsertFunctionCatalogEntry> FilterCatalog(
        IReadOnlyList<InsertFunctionCatalogEntry> catalog,
        string? category,
        string? searchText)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? AllCategory : category.Trim();
        var search = searchText?.Trim() ?? "";
        return catalog
            .Where(entry => normalizedCategory == AllCategory || entry.Category == normalizedCategory)
            .Where(entry =>
                search.Length == 0 ||
                entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string CreateFormula(string functionName) =>
        $"{functionName.Trim().ToUpperInvariant()}()";

    private void RefreshList()
    {
        _listBox.Items.Clear();
        foreach (var entry in FilterCatalog(_catalog, _categoryBox.SelectedItem?.ToString(), _searchBox.Text))
            _listBox.Items.Add(entry);
        if (_listBox.Items.Count > 0) _listBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is InsertFunctionCatalogEntry entry)
        {
            SelectedFormula = CreateFormula(entry.Name);
            DialogResult = true;
        }
    }

    private static string GetCategory(string name)
    {
        if (LogicalFunctions.Contains(name)) return "Logical";
        if (LookupFunctions.Contains(name)) return "Lookup & Reference";
        if (TextFunctions.Contains(name)) return "Text";
        if (DateTimeFunctions.Contains(name)) return "Date & Time";
        if (StatisticalFunctions.Contains(name)) return "Statistical";
        if (DynamicArrayFunctions.Contains(name)) return "Dynamic Array";
        if (FinancialFunctions.Contains(name)) return "Financial";
        if (InformationFunctions.Contains(name)) return "Information";
        return "Math & Trig";
    }

    private static string GetDescription(string name) =>
        KnownDescriptions.TryGetValue(name, out var description)
            ? description
            : $"{name} function.";

    private static readonly HashSet<string> LogicalFunctions = ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA", "LET", "LAMBDA"];
    private static readonly HashSet<string> LookupFunctions = ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH", "XMATCH", "INDIRECT", "OFFSET"];
    private static readonly HashSet<string> TextFunctions = ["CONCAT", "TEXTJOIN", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"];
    private static readonly HashSet<string> DateTimeFunctions = ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF", "EOMONTH", "WORKDAY", "NETWORKDAYS"];
    private static readonly HashSet<string> StatisticalFunctions = ["AVERAGE", "COUNT", "COUNTA", "MIN", "MAX", "COUNTIF", "COUNTIFS", "AVERAGEIF", "MEDIAN", "STDEV.S", "VAR.S", "RANK.EQ", "PERCENTILE.INC"];
    private static readonly HashSet<string> DynamicArrayFunctions = ["FILTER", "SORT", "UNIQUE", "SEQUENCE", "RANDARRAY", "TRANSPOSE", "MAP", "REDUCE", "SCAN", "BYROW", "BYCOL", "MAKEARRAY"];
    private static readonly HashSet<string> FinancialFunctions = ["PMT", "NPV", "IRR", "RATE", "PV", "FV"];
    private static readonly HashSet<string> InformationFunctions = ["ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "NA", "CELL", "INFO"];

    private static readonly Dictionary<string, string> KnownDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = "Adds numbers.",
        ["AVERAGE"] = "Returns the average of numbers.",
        ["COUNT"] = "Counts numeric values.",
        ["COUNTA"] = "Counts non-empty values.",
        ["IF"] = "Returns one value if a condition is true and another if false.",
        ["VLOOKUP"] = "Looks up a value in the first column of a table.",
        ["HLOOKUP"] = "Looks up a value in the first row of a table.",
        ["XLOOKUP"] = "Searches a range and returns a matching item.",
        ["INDEX"] = "Returns a value from a range by position.",
        ["MATCH"] = "Returns the relative position of an item.",
        ["XMATCH"] = "Returns the relative position of an item with modern match options.",
        ["CONCAT"] = "Joins text values.",
        ["TEXT"] = "Formats a value as text.",
        ["TODAY"] = "Returns the current date.",
        ["NOW"] = "Returns the current date and time.",
        ["ROUND"] = "Rounds a number to a specified number of digits.",
        ["FILTER"] = "Filters a range by included rows or columns.",
        ["SORT"] = "Sorts a range or array.",
        ["UNIQUE"] = "Returns unique values from a range or array."
    };
}
