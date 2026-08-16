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
        var text = QuickPartCommandPlanner.ResolveText(UiText.Get);
        Title = text.SaveTitle;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var panel = DialogPanel();
        panel.Children.Add(new TextBlock { Text = text.NameLabel });
        panel.Children.Add(_name);
        panel.Children.Add(ButtonRow(
            Button(text.OkButton, Accept, isDefault: true),
            Button(text.CancelButton, () => Close(null), isCancel: true)));
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
    private readonly TextBox _rows;
    private readonly TextBox _columns;

    private DrawTableDimensionDialog(DrawTableDimensionDialogPlan plan)
    {
        Title = plan.Title;
        _rows = new TextBox { Text = plan.DefaultRows.ToString(), Width = 72 };
        _columns = new TextBox { Text = plan.DefaultColumns.ToString(), Width = 72 };
        Width = 290;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, plan.Surface);
        ImageChartDialogSurfaceSemantics.Apply(_rows, plan.Surface.Field(DrawTableDimensionDialogField.Rows));
        ImageChartDialogSurfaceSemantics.Apply(_columns, plan.Surface.Field(DrawTableDimensionDialogField.Columns));

        var panel = QuickPartNameDialog.DialogPanel();
        panel.Children.Add(new TextBlock { Text = plan.RowsLabel });
        panel.Children.Add(_rows);
        panel.Children.Add(new TextBlock { Text = plan.ColumnsLabel });
        panel.Children.Add(_columns);
        panel.Children.Add(QuickPartNameDialog.ButtonRow(
            QuickPartNameDialog.Button(
                plan.FocusPlan.ActionButtons[0].Label,
                Accept,
                isDefault: plan.FocusPlan.ActionButtons[0].IsDefault,
                isCancel: plan.FocusPlan.ActionButtons[0].IsCancel),
            QuickPartNameDialog.Button(
                plan.FocusPlan.ActionButtons[1].Label,
                () => Close(null),
                isDefault: plan.FocusPlan.ActionButtons[1].IsDefault,
                isCancel: plan.FocusPlan.ActionButtons[1].IsCancel)));
        Content = panel;
        Opened += (_, _) =>
        {
            var target = ResolveFocusTarget(plan.FocusPlan.InitialFocusTarget);
            if (plan.FocusPlan.SelectAllOnFocus)
                AvaloniaCompactDialogChrome.FocusAndSelect(target);
            else
                target.Focus();
        };
        QuickPartNameDialog.CloseOnEscape(this);
    }

    public static Task<(int Rows, int Columns)?> AskAsync(Window owner) =>
        new DrawTableDimensionDialog(DrawTableCommandPlanner.BuildDialog(
            DrawTableDimensionDialogKind.DrawTable,
            UiText.Get)).ShowDialog<(int Rows, int Columns)?>(owner);

    public static Task<(int Rows, int Columns)?> AskSplitCellAsync(Window owner) =>
        new DrawTableDimensionDialog(DrawTableCommandPlanner.BuildDialog(
            DrawTableDimensionDialogKind.SplitCells,
            UiText.Get))
            .ShowDialog<(int Rows, int Columns)?>(owner);

    private void Accept() => Close(DrawTableCommandPlanner.Normalize(_rows.Text, _columns.Text));

    private TextBox ResolveFocusTarget(DrawTableDimensionDialogField field) => field switch
    {
        DrawTableDimensionDialogField.Rows => _rows,
        DrawTableDimensionDialogField.Columns => _columns,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}

internal sealed class BuildingBlocksOrganizerDialog : FreeWDialogWindow
{
    private readonly BuildingBlocksOrganizerSession _session;
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
    private bool _updatingProjection;

    internal BuildingBlocksOrganizerDialog(QuickPartLibrary library)
    {
        _session = BuildingBlocksOrganizerPlanner.CreateSession(library);
        Title = BuildingBlocksOrganizerPlanner.Title;
        Width = BuildingBlocksOrganizerPlanner.Width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _blocks.SelectionChanged += (_, _) => OnSelectionChanged();
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
        _insertButton = QuickPartNameDialog.Button(BuildingBlocksOrganizerPlanner.InsertText, Insert, isDefault: true);
        _deleteButton = QuickPartNameDialog.Button(BuildingBlocksOrganizerPlanner.DeleteText, Delete);
        var closeButton = QuickPartNameDialog.Button(BuildingBlocksOrganizerPlanner.CloseText, () => Close(null), isCancel: true);
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

    public static Task<BuildingBlocksOrganizerAction?> ShowAsync(Window owner, QuickPartLibrary library) =>
        new BuildingBlocksOrganizerDialog(library).ShowDialog<BuildingBlocksOrganizerAction?>(owner);

    private void RefreshBlocks()
    {
        var state = _session.Current;
        _updatingProjection = true;
        _blocks.ItemsSource = state.Items;
        _blocks.SelectedIndex = state.SelectedIndex;
        _updatingProjection = false;
        ApplyState(state);
    }

    private void OnSelectionChanged()
    {
        if (_updatingProjection)
            return;

        ApplyState(_session.SelectIndex(_blocks.SelectedIndex));
    }

    private void ApplyState(BuildingBlocksOrganizerState state)
    {
        _preview.Text = state.PreviewText;
        _status.Text = state.StatusText;
        _insertButton.IsEnabled = state.CanInsert;
        _deleteButton.IsEnabled = state.CanDelete;
    }

    private void Insert()
    {
        if (_session.AcceptSelection() is { } action)
            Close(action);
    }

    private void Delete()
    {
        if (!_session.Current.CanDelete)
            return;

        _session.DeleteSelection();
        RefreshBlocks();
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
