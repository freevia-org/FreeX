using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed record InsertFunctionCatalogEntry(string Name, string Category, string Description);

public sealed class InsertFunctionDialog : Window
{
    public string? SelectedFormula { get; private set; }

    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryBox;
    private readonly TextBlock _descText;
    private readonly TextBlock _syntaxText;
    private readonly IReadOnlyList<InsertFunctionCatalogEntry> _catalog;

    private const string MostRecentlyUsedCategory = InsertFunctionCatalogPlanner.MostRecentlyUsedCategory;
    private const string AllCategory = InsertFunctionCatalogPlanner.AllCategory;

    public InsertFunctionDialog()
    {
        Title = UiText.Get("InsertFunction_InsertFunction");
        Width = 560; Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        _catalog = BuildCatalog();

        var outer = new DockPanel { Margin = new Thickness(12) };

        var searchPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition());
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(searchPanel, Dock.Top);

        _searchBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 260, Margin = new Thickness(4, 0, 8, 6) };
        var searchLabel = new Label { Content = UiText.Get("InsertFunction_SearchForAFunction"), Target = _searchBox, VerticalContentAlignment = VerticalAlignment.Center };
        searchPanel.Children.Add(searchLabel);
        Grid.SetColumn(_searchBox, 1);
        searchPanel.Children.Add(_searchBox);
        var go = new Button { Content = UiText.Get("InsertFunction_Go"), Width = 64, Height = 24, Margin = new Thickness(0, 0, 0, 6) };
        go.Click += (_, _) => RefreshList();
        Grid.SetColumn(go, 2);
        searchPanel.Children.Add(go);
        _searchBox.TextChanged += (_, _) => RefreshList();

        _categoryBox = new ComboBox { Margin = new Thickness(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var categoryLabel = new Label { Content = UiText.Get("InsertFunction_OrSelectACategory"), Target = _categoryBox, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(categoryLabel, 1);
        searchPanel.Children.Add(categoryLabel);
        _categoryBox.ItemsSource = BuildCategoryChoices(_catalog);
        _categoryBox.SelectedItem = MostRecentlyUsedCategory;
        _categoryBox.SelectionChanged += (_, _) => RefreshList();
        Grid.SetColumn(_categoryBox, 1);
        Grid.SetRow(_categoryBox, 1);
        searchPanel.Children.Add(_categoryBox);

        _listBox = new ListBox();
        AutomationProperties.SetName(_listBox, UiText.Get("InsertFunction_Functions"));
        var listLabel = new Label { Content = UiText.Get("InsertFunction_SelectAFunction"), Target = _listBox, Padding = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(listLabel, Dock.Top);

        var helpPanel = new GroupBox
        {
            Header = UiText.Get("InsertFunction_FormulaSyntaxAndHelp"),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8)
        };
        DockPanel.SetDock(helpPanel, Dock.Bottom);
        var helpStack = new StackPanel();
        helpPanel.Content = helpStack;
        _syntaxText = new TextBlock
        {
            FontWeight = System.Windows.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        helpStack.Children.Add(_syntaxText);
        _descText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            MinHeight = 40
        };
        helpStack.Children.Add(_descText);

        // Button row
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(btnRow, Dock.Bottom);
        var ok = new Button { Content = UiText.Ok, Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var help = new Button { Content = UiText.Get("InsertFunction_HelpOnThisFunction"), Width = 146, Margin = new Thickness(0, 0, 8, 0) };
        help.Click += (_, _) => ShowFunctionHelp();
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(help);
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        // Function list
        _listBox.DisplayMemberPath = nameof(InsertFunctionCatalogEntry.Name);
        _listBox.SelectionChanged += (_, _) =>
        {
            _descText.Text = _listBox.SelectedItem is InsertFunctionCatalogEntry entry
                ? entry.Description
                : "";
            _syntaxText.Text = _listBox.SelectedItem is InsertFunctionCatalogEntry selected
                ? $"{selected.Name}()"
                : "";
        };
        _listBox.MouseDoubleClick += (_, _) => Ok_Click(null!, null!);

        outer.Children.Add(searchPanel);
        outer.Children.Add(btnRow);
        outer.Children.Add(helpPanel);
        outer.Children.Add(listLabel);
        outer.Children.Add(_listBox);

        Content = outer;

        Loaded += (_, _) => { RefreshList(); FocusInitialKeyboardTarget(); };
    }

    private void FocusInitialKeyboardTarget()
    {
        _searchBox.Focus();
        _searchBox.SelectAll();
        Keyboard.Focus(_searchBox);
    }

    public static IReadOnlyList<InsertFunctionCatalogEntry> BuildCatalog() =>
        InsertFunctionCatalogPlanner.BuildCatalog();

    public static IReadOnlyList<InsertFunctionCatalogEntry> FilterCatalog(
        IReadOnlyList<InsertFunctionCatalogEntry> catalog,
        string? category,
        string? searchText) =>
        InsertFunctionCatalogPlanner.FilterCatalog(catalog, category, searchText);

    public static IReadOnlyList<string> BuildCategoryChoices(IReadOnlyList<InsertFunctionCatalogEntry> catalog) =>
        [MostRecentlyUsedCategory, AllCategory, .. catalog.Select(entry => entry.Category).Distinct().OrderBy(category => category)];

    public static string CreateFormula(string functionName) =>
        InsertFunctionCatalogPlanner.CreateFormula(functionName);

    public static string CreateFormula(string functionName, IEnumerable<string?> arguments) =>
        FunctionArgumentsDialog.CreateFormula(functionName, arguments);

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
            var argumentsDialog = new FunctionArgumentsDialog(entry) { Owner = this };
            if (argumentsDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(argumentsDialog.ResultFormula))
                return;

            SelectedFormula = argumentsDialog.ResultFormula;
            DialogResult = true;
        }
    }

    private void ShowFunctionHelp()
    {
        var entry = _listBox.SelectedItem as InsertFunctionCatalogEntry;
        var message = entry is null
            ? UiText.Get("InsertFunction_SelectFunctionForHelpMessage")
            : UiText.Format("InsertFunction_FunctionHelpMessage", entry.Name, entry.Description);

        DialogMessageHelper.ShowInfo(this, message, UiText.Get("InsertFunction_FunctionHelpTitle"));
    }

}
