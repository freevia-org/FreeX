using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly bool ScenarioManagerRangePickerBuilderRegistered =
        RegisterScenarioManagerRangePickerBuilder();

    private static bool RegisterScenarioManagerRangePickerBuilder()
    {
        Window.OwnerProperty.Changed.AddClassHandler<Window>(AddScenarioManagerRangePickers);
        return true;
    }

    private static void AddScenarioManagerRangePickers(Window dialog, AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog.Owner is not MainWindow owner
            || !string.Equals(
                AutomationProperties.GetAutomationId(dialog),
                FreeXAutomationIdCatalog.ScenarioManager.AvaloniaDialog,
                StringComparison.Ordinal))
        {
            return;
        }

        AddScenarioManagerRangePicker(
            owner,
            dialog,
            FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsBox,
            FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsPickerButton,
            "Select changing cells range",
            "range.scenario-manager.changing-cells");
        AddScenarioManagerRangePicker(
            owner,
            dialog,
            FreeXAutomationIdCatalog.ScenarioManager.ResultCellsBox,
            FreeXAutomationIdCatalog.ScenarioManager.ResultCellsPickerButton,
            "Select result cells range",
            "range.scenario-manager.result-cells");
    }

    private static void AddScenarioManagerRangePicker(
        MainWindow owner,
        Window dialog,
        string textBoxAutomationId,
        string pickerAutomationId,
        string pickerAutomationName,
        string targetId)
    {
        var target = dialog.GetLogicalDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(textBox => string.Equals(
                AutomationProperties.GetAutomationId(textBox),
                textBoxAutomationId,
                StringComparison.Ordinal));
        if (target?.Parent is not Panel field || field.Children.Contains(target) is false)
            return;

        var targetIndex = field.Children.IndexOf(target);
        var picker = new Button
        {
            Content = "...",
            Width = 30,
            MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyButton(picker, ScenarioManagerDialogChromeStyle, 30);
        AutomationProperties.SetAutomationId(picker, pickerAutomationId);
        AutomationProperties.SetName(picker, pickerAutomationName);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        if (field is Grid parentGrid)
        {
            Grid.SetRow(row, Grid.GetRow(target));
            Grid.SetColumn(row, Grid.GetColumn(target));
            Grid.SetRowSpan(row, Grid.GetRowSpan(target));
            Grid.SetColumnSpan(row, Grid.GetColumnSpan(target));
        }

        field.Children.RemoveAt(targetIndex);
        Grid.SetRow(target, 0);
        Grid.SetColumn(target, 0);
        Grid.SetRowSpan(target, 1);
        Grid.SetColumnSpan(target, 1);
        row.Children.Add(target);
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        field.Children.Insert(targetIndex, row);

        owner.AttachDialogRangePicker(dialog, picker, target, targetId);
    }
}
