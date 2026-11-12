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
            UiText.Get("ScenarioManager_SelectChangingCellsRangeAutomationName"),
            "range.scenario-manager.changing-cells");
        AddScenarioManagerRangePicker(
            owner,
            dialog,
            FreeXAutomationIdCatalog.ScenarioManager.ResultCellsBox,
            FreeXAutomationIdCatalog.ScenarioManager.ResultCellsPickerButton,
            UiText.Get("ScenarioManager_SelectResultCellsRangeAutomationName"),
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
        // WPF ScenarioManagerDialog builds these rows with DialogReferencePicker.CreateEditor's
        // defaults: a 28px "..." button docked on the LEFT of the box with a 6px gap after it.
        var picker = new Button
        {
            Content = "...",
            Width = 28,
            MinWidth = 28,
            Margin = new Thickness(0, 0, 6, 0),
        };
        AvaloniaCompactDialogChrome.ApplyButton(picker, ScenarioManagerDialogChromeStyle, 28);
        AutomationProperties.SetAutomationId(picker, pickerAutomationId);
        AutomationProperties.SetName(picker, pickerAutomationName);

        // The row takes over the field's own margin (the 8px gap under each field) so the picker
        // and the box share one baseline instead of the picker centring over box-plus-gap.
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = target.Margin,
        };
        target.Margin = default;
        if (field is Grid parentGrid)
        {
            Grid.SetRow(row, Grid.GetRow(target));
            Grid.SetColumn(row, Grid.GetColumn(target));
            Grid.SetRowSpan(row, Grid.GetRowSpan(target));
            Grid.SetColumnSpan(row, Grid.GetColumnSpan(target));
        }

        field.Children.RemoveAt(targetIndex);
        // The box stays first in the logical order, so Tab still reaches it before its picker.
        Grid.SetRow(target, 0);
        Grid.SetColumn(target, 1);
        Grid.SetRowSpan(target, 1);
        Grid.SetColumnSpan(target, 1);
        row.Children.Add(target);
        Grid.SetColumn(picker, 0);
        row.Children.Add(picker);
        field.Children.Insert(targetIndex, row);

        owner.AttachDialogRangePicker(dialog, picker, target, targetId);
    }
}
