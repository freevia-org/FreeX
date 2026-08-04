using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class DataTableDialogParitySourceTests
{
    [Fact]
    public void AvaloniaDataTableUsesWpfDimensionsAndSharedLocalizedLabels()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "DataTableDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var planner = ReadSource("src", "FreeX.App.Services", "DataTablePlanner.cs");

        wpf.Should().Contain("Width = 360;");
        wpf.Should().Contain("Height = 210;");
        avalonia.Should().Contain("Width = 360,");
        avalonia.Should().Contain("Height = 210,");
        avalonia.Should().Contain("UiText.Get(\"DataTable_Title\")");
        avalonia.Should().Contain("initialRowInputCellText");
        avalonia.Should().Contain("initialColumnInputCellText");
        avalonia.Should().Contain("hasInitialDataTableFixture");
        avalonia.Should().Contain("dataTableValidationEnabled = !hasInitialDataTableFixture");
        avalonia.Should().Contain("if (!hasInitialDataTableFixture)");
        avalonia.Should().Contain("dataTableInputWasEdited = false");
        avalonia.Should().Contain("HandleDataTableInputChanged");
        avalonia.Should().Contain("StripDisplayMnemonic(UiText.Get(\"DataTable_RowInputLabel\"))");
        avalonia.Should().Contain("StripDisplayMnemonic(UiText.Get(\"DataTable_ColumnInputLabel\"))");
        avalonia.Should().Contain("DataTablePlanner.CreatePlan(");
        planner.Should().Contain("public static DataTablePlanResult CreatePlan(");
    }

    [Fact]
    public void AvaloniaDataTableRendersAndWiresBothWpfStyleRangePickers()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "DataTableDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var dataTableMethod = ExtractBetween(
            avalonia,
            "private async Task<DataTablePlan?> ShowDataTableInputDialogAsync(",
            "private static string FormatDataTableMode(DataTablePlan plan)");

        wpf.Should().Contain("DialogReferencePicker.CreateEditor(");
        dataTableMethod.Should().Contain("CreateDialogRangePickerButton(");
        avalonia.Should().Contain("BuildDialogRangePickerRow(input, picker)");
        dataTableMethod.Should().Contain("DataTableRowInputCellPickerButton");
        dataTableMethod.Should().Contain("DataTableColumnInputCellPickerButton");
        dataTableMethod.Should().Contain("AttachDialogRangePicker(dialog, rowInputPicker, rowInputBox, \"range.data-table.row-input-cell\")");
        dataTableMethod.Should().Contain("AttachDialogRangePicker(dialog, columnInputPicker, columnInputBox, \"range.data-table.column-input-cell\")");
        dataTableMethod.Should().Contain("new ColumnDefinition { Width = new GridLength(110) }");
        dataTableMethod.Should().Contain("dialog.Content = new StackPanel");
        dataTableMethod.Should().NotContain("DockPanel.SetDock(buttonRow, Dock.Bottom)");
        dataTableMethod.Should().NotContain("CreateDataTableField(\"Row input cell\"");

        var parityCapture = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs");
        parityCapture.Should().Contain("ShowDataTableInputDialogAsync(\"E2\", \"F2\")");
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadSource(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException("Could not locate repository source.", Path.Combine(parts));
    }
}
