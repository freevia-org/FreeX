using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class QuickPartNameDialog : FreeWDialogWindow
{
    private readonly TextBox _name = new() { MinWidth = 300 };

    private QuickPartNameDialog()
    {
        Title = "Save to Quick Parts";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var panel = DialogPanel();
        panel.Children.Add(new TextBlock { Text = "Name:" });
        panel.Children.Add(_name);
        panel.Children.Add(ButtonRow(
            Button("OK", Accept, isDefault: true),
            Button("Cancel", () => Close(null), isCancel: true)));
        Content = panel;
        Opened += (_, _) => _name.Focus();
        CloseOnEscape(this);
    }

    public static Task<string?> AskAsync(Window owner) =>
        new QuickPartNameDialog().ShowDialog<string?>(owner);

    private void Accept()
    {
        var value = _name.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
            Close(value);
    }

    internal static StackPanel DialogPanel() => new() { Margin = new Thickness(16), Spacing = 8 };

    internal static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 78,
            IsDefault = isDefault,
            IsCancel = isCancel,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

    internal static StackPanel ButtonRow(params Button[] buttons) =>
        AvaloniaCompactDialogChrome.CreateActionRow(buttons, new Thickness(0, 6, 0, 0));

    internal static void CloseOnEscape(Window window) => window.KeyDown += (_, e) =>
    {
        if (e.Key != Key.Escape)
            return;
        window.Close(null);
        e.Handled = true;
    };
}

internal sealed class FieldPickerDialog : FreeWDialogWindow
{
    private readonly ListBox _categories = new() { MinWidth = 160, MinHeight = 210 };
    private readonly ListBox _fields = new() { MinWidth = 250, MinHeight = 210 };

    private FieldPickerDialog()
    {
        Title = FieldPickerDialogPlanner.Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _categories.ItemsSource = FieldPickerDialogPlanner.Categories;
        _categories.SelectionChanged += (_, _) => RefreshFields();
        _categories.SelectedIndex = 0;
        _fields.DoubleTapped += (_, _) => Accept();

        var lists = new Grid { ColumnDefinitions = new ColumnDefinitions("160,8,*") };
        Grid.SetColumn(_categories, 0);
        Grid.SetColumn(_fields, 2);
        lists.Children.Add(_categories);
        lists.Children.Add(_fields);

        var panel = QuickPartNameDialog.DialogPanel();
        panel.Children.Add(new TextBlock { Text = FieldPickerDialogPlanner.Prompt });
        panel.Children.Add(lists);
        panel.Children.Add(QuickPartNameDialog.ButtonRow(
            QuickPartNameDialog.Button("OK", Accept, isDefault: true),
            QuickPartNameDialog.Button("Cancel", () => Close(null), isCancel: true)));
        Content = panel;
        Opened += (_, _) => _categories.Focus();
        QuickPartNameDialog.CloseOnEscape(this);
    }

    public static Task<string?> AskAsync(Window owner) =>
        new FieldPickerDialog().ShowDialog<string?>(owner);

    private void RefreshFields()
    {
        var category = _categories.SelectedItem as string;
        _fields.ItemsSource = FieldPickerDialogPlanner
            .ChoicesForCategory(category)
            .Select(choice => choice.Label)
            .ToArray();
        _fields.SelectedIndex = _fields.ItemCount > 0 ? 0 : -1;
    }

    private void Accept()
    {
        if (FieldPickerDialogPlanner.TryGetInstruction(
                _categories.SelectedItem as string,
                _fields.SelectedItem as string,
                out var instruction))
            Close(instruction);
    }
}

internal sealed class DrawTableDimensionDialog : FreeWDialogWindow
{
    private readonly TextBox _rows = new() { Text = DrawTableCommandPlanner.DefaultRows.ToString(), Width = 72 };
    private readonly TextBox _columns = new() { Text = DrawTableCommandPlanner.DefaultColumns.ToString(), Width = 72 };

    private DrawTableDimensionDialog()
    {
        Title = "Draw Table";
        Width = 290;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var panel = QuickPartNameDialog.DialogPanel();
        panel.Children.Add(new TextBlock { Text = "Number of rows:" });
        panel.Children.Add(_rows);
        panel.Children.Add(new TextBlock { Text = "Number of columns:" });
        panel.Children.Add(_columns);
        panel.Children.Add(QuickPartNameDialog.ButtonRow(
            QuickPartNameDialog.Button("OK", Accept, isDefault: true),
            QuickPartNameDialog.Button("Cancel", () => Close(null), isCancel: true)));
        Content = panel;
        Opened += (_, _) => _rows.Focus();
        QuickPartNameDialog.CloseOnEscape(this);
    }

    public static Task<(int Rows, int Columns)?> AskAsync(Window owner) =>
        new DrawTableDimensionDialog().ShowDialog<(int Rows, int Columns)?>(owner);

    private void Accept() => Close(DrawTableCommandPlanner.Normalize(_rows.Text, _columns.Text));
}

internal enum BuildingBlockActionKind
{
    Insert,
}

internal sealed record BuildingBlockAction(BuildingBlockActionKind Kind, string Name);

internal sealed class BuildingBlocksOrganizerDialog : FreeWDialogWindow
{
    private readonly QuickPartLibrary _library;
    private readonly ListBox _blocks = new()
    {
        MinWidth = BuildingBlocksOrganizerPlanner.ListMinWidth,
        MinHeight = BuildingBlocksOrganizerPlanner.ListMinHeight,
    };
    private readonly TextBox _preview = new()
    {
        MinWidth = BuildingBlocksOrganizerPlanner.PreviewMinWidth,
        MinHeight = BuildingBlocksOrganizerPlanner.PreviewMinHeight,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _insertButton;
    private readonly Button _deleteButton;

    internal BuildingBlocksOrganizerDialog(QuickPartLibrary library)
    {
        _library = library;
        Title = "Building Blocks Organizer";
        Width = BuildingBlocksOrganizerPlanner.Width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _blocks.SelectionChanged += (_, _) => RefreshPreview();
        _blocks.DoubleTapped += (_, _) => Insert();

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{BuildingBlocksOrganizerPlanner.ListMinWidth},{BuildingBlocksOrganizerPlanner.ColumnGap},*"),
        };
        Grid.SetColumn(_blocks, 0);
        Grid.SetColumn(_preview, 2);
        content.Children.Add(_blocks);
        content.Children.Add(_preview);

        var panel = QuickPartNameDialog.DialogPanel();
        panel.Margin = new Thickness(14);
        panel.Spacing = 0;
        var labels = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{BuildingBlocksOrganizerPlanner.ListMinWidth},{BuildingBlocksOrganizerPlanner.ColumnGap},*"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var listLabel = new TextBlock
        {
            Text = BuildingBlocksOrganizerPlanner.ListLabel,
            FontWeight = FontWeight.SemiBold,
        };
        var previewLabel = new TextBlock
        {
            Text = BuildingBlocksOrganizerPlanner.PreviewLabel,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetColumn(listLabel, 0);
        Grid.SetColumn(previewLabel, 2);
        labels.Children.Add(listLabel);
        labels.Children.Add(previewLabel);
        panel.Children.Add(labels);
        panel.Children.Add(content);
        _insertButton = QuickPartNameDialog.Button("Insert", Insert, isDefault: true);
        _deleteButton = QuickPartNameDialog.Button("Delete", Delete);
        var closeButton = QuickPartNameDialog.Button("Close", () => Close(null), isCancel: true);
        foreach (var button in new[] { _insertButton, _deleteButton, closeButton })
        {
            button.MinWidth = 84;
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Padding = new Thickness(6, 3);
        }
        var buttons = QuickPartNameDialog.ButtonRow(_insertButton, _deleteButton, closeButton);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(buttons);
        panel.Children.Add(_status);
        Content = panel;
        RefreshBlocks();
        Opened += (_, _) => _blocks.Focus();
        QuickPartNameDialog.CloseOnEscape(this);
    }

    public static Task<BuildingBlockAction?> ShowAsync(Window owner, QuickPartLibrary library) =>
        new BuildingBlocksOrganizerDialog(library).ShowDialog<BuildingBlockAction?>(owner);

    private BuildingBlockListItem? SelectedItem => _blocks.SelectedItem as BuildingBlockListItem;

    private void RefreshBlocks()
    {
        _blocks.ItemsSource = _library.Snippets.Select(part => new BuildingBlockListItem(part)).ToArray();
        _blocks.SelectedIndex = _blocks.ItemCount > 0 ? 0 : -1;
        _status.Text = _blocks.ItemCount == 0 ? BuildingBlocksOrganizerPlanner.EmptyStatus : string.Empty;
        _insertButton.IsEnabled = _blocks.ItemCount > 0;
        _deleteButton.IsEnabled = _blocks.ItemCount > 0;
        RefreshPreview();
    }

    private void RefreshPreview() => _preview.Text = BuildingBlocksOrganizerPlanner.FormatPreview(SelectedItem?.Part);

    private void Insert()
    {
        if (SelectedItem is { } item)
            Close(new BuildingBlockAction(BuildingBlockActionKind.Insert, item.Part.Name));
    }

    private void Delete()
    {
        if (SelectedItem is not { } item)
            return;
        _library.Remove(item.Part.Name);
        RefreshBlocks();
        _status.Text = BuildingBlocksOrganizerPlanner.FormatRemovedStatus(item.Part.Name);
    }
}

internal sealed class FreeWInfoDialog : FreeWDialogWindow
{
    private FreeWInfoDialog(string message, string title)
    {
        Title = title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var panel = QuickPartNameDialog.DialogPanel();
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(QuickPartNameDialog.ButtonRow(
            QuickPartNameDialog.Button("OK", () => Close(), isDefault: true)));
        Content = panel;
        QuickPartNameDialog.CloseOnEscape(this);
    }

    public static Task ShowAsync(Window owner, string message, string title = "FreeW") =>
        new FreeWInfoDialog(message, title).ShowDialog(owner);
}
