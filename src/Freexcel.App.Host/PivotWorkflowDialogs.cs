using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableDataSourceDialogResult(string SourceRangeText);

public sealed class PivotTableDataSourceDialog : Window
{
    private readonly TextBox _sourceBox = new();

    public PivotTableDataSourceDialogResult Result { get; private set; }

    public PivotTableDataSourceDialog(string sourceRangeText)
    {
        Result = CreateResult(sourceRangeText);
        Title = "Change PivotTable Data Source";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sourceBox.Text = Result.SourceRangeText;
        Content = ObjectSizeDialog.CreateSingleInputContent("Table/Range:", _sourceBox, () =>
        {
            Result = CreateResult(_sourceBox.Text);
            DialogResult = true;
        });
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());
}

public sealed record InsertSlicerDialogResult(string FieldName, string SlicerName);

public sealed class InsertSlicerDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertSlicerDialogResult Result { get; private set; }

    public InsertSlicerDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Slicer");
        Title = "Insert Slicer";
        Width = 360;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateFieldNameContent(fields, field, Result.SlicerName, Accept);
    }

    public static InsertSlicerDialogResult CreateResult(string fieldName, string slicerName) =>
        new(fieldName.Trim(), slicerName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }

    private StackPanel CreateFieldNameContent(IReadOnlyList<string> fields, string field, string name, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Field", Margin = new Thickness(0, 0, 0, 4) });
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        _fieldBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_fieldBox);
        stack.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
        _nameBox.Text = name;
        _nameBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_nameBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(accept));
        return stack;
    }
}

public sealed record InsertTimelineDialogResult(string DateFieldName, string TimelineName);

public sealed class InsertTimelineDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertTimelineDialogResult Result { get; private set; }

    public InsertTimelineDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Timeline");
        Title = "Insert Timeline";
        Width = 360;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Date field", Margin = new Thickness(0, 0, 0, 4) });
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        _fieldBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_fieldBox);
        stack.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
        _nameBox.Text = Result.TimelineName;
        _nameBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_nameBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static InsertTimelineDialogResult CreateResult(string dateFieldName, string timelineName) =>
        new(dateFieldName.Trim(), timelineName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }
}

public sealed record PivotChartTypeDialogResult(ChartType ChartType);

public sealed class PivotChartTypeDialog : Window
{
    private readonly ComboBox _chartTypeBox = new();

    public ChartType SelectedChartType { get; private set; }
    public PivotChartTypeDialogResult Result { get; private set; }

    public PivotChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change PivotChart Type";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var options = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.ItemsSource = options;
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedItem = options.FirstOrDefault(option => option.Type == currentType);
        _chartTypeBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_chartTypeBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            if (_chartTypeBox.SelectedItem is ChartTypePickerOption option)
                SelectedChartType = option.Type;
            Result = CreateResult(SelectedChartType);
            DialogResult = true;
        }));
        Content = stack;
    }

    public static PivotChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);
}
