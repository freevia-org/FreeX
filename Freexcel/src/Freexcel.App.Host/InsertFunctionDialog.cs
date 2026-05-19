using System.Windows;
using System.Windows.Controls;

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
        InsertFunctionCatalogPlanner.BuildCatalog();

    public static IReadOnlyList<InsertFunctionCatalogEntry> FilterCatalog(
        IReadOnlyList<InsertFunctionCatalogEntry> catalog,
        string? category,
        string? searchText) =>
        InsertFunctionCatalogPlanner.FilterCatalog(catalog, category, searchText);

    public static string CreateFormula(string functionName) =>
        InsertFunctionCatalogPlanner.CreateFormula(functionName);

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

}
