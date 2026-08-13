using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Editing;

namespace FreeX.App.Host;

public sealed partial class PasteSpecialDialog
{
    private GroupBox CreatePasteGroup()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 9; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var choice in PasteSpecialPlanner.Surface.WpfChoices)
        {
            AddPasteChoice(
                grid,
                _pasteChoiceButtons[choice.Mode],
                choice.WpfPlacement.Row,
                choice.WpfPlacement.Column);
        }

        return new GroupBox
        {
            Header = PasteSpecialPlanner.Surface.PasteGroup.ResolveWpf(UiText.Get),
            Content = grid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private StackPanel CreatePasteOptionsPanel()
    {
        var options = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var toggle in PasteSpecialPlanner.Surface.Toggles.OrderBy(descriptor => descriptor.Order))
            options.Children.Add(GetToggleControl(toggle.Kind));
        return options;
    }

    private GroupBox CreateOperationGroup() =>
        new()
        {
            Header = PasteSpecialPlanner.Surface.OperationGroup.ResolveWpf(UiText.Get),
            Content = CreateOperationPanel(),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

    private StackPanel CreateFooterRow()
    {
        var surface = PasteSpecialPlanner.Surface;
        var acceptAction = surface.GetAction(PasteSpecialDialogActionKind.Accept);
        var cancelAction = surface.GetAction(PasteSpecialDialogActionKind.Cancel);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _pasteLinkButton.Click += (_, _) =>
        {
            _pasteLinkRequested = true;
            DialogResult = true;
        };

        var ok = new Button
        {
            Content = UiText.Get(acceptAction.WpfLabelTextKey),
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = acceptAction.IsDefault,
            IsEnabled = acceptAction.IsEnabled,
        };
        var cancel = new Button
        {
            Content = UiText.Get(cancelAction.WpfLabelTextKey),
            Width = 80,
            IsCancel = cancelAction.IsCancel,
            IsEnabled = cancelAction.IsEnabled,
        };
        ApplyAutomationMetadata(ok, acceptAction);
        ApplyAutomationMetadata(cancel, cancelAction);
        ok.Click += (_, _) => { DialogResult = true; };
        row.Children.Add(_pasteLinkButton);
        row.Children.Add(ok);
        row.Children.Add(cancel);
        return row;
    }

    private void ApplyAutomationMetadata()
    {
        var surface = PasteSpecialPlanner.Surface;
        foreach (var choice in surface.WpfChoices)
            ApplyAutomationMetadata(_pasteChoiceButtons[choice.Mode], choice);

        foreach (var toggle in surface.Toggles)
            ApplyAutomationMetadata(GetToggleControl(toggle.Kind), toggle);

        foreach (var operation in surface.Operations)
            ApplyAutomationMetadata(_operationButtons[operation.Operation], operation);

        ApplyAutomationMetadata(_pasteLinkButton, surface.GetAction(PasteSpecialDialogActionKind.PasteLink));
    }

    private static void ApplyAutomationMetadata(Control control, PasteSpecialChoiceDescriptor descriptor) =>
        SetAutomationMetadata(
            control,
            UiText.Get(descriptor.WpfAutomationNameTextKey),
            descriptor.WpfAutomationId,
            UiText.Get(descriptor.WpfAutomationHelpTextKey));

    private static void ApplyAutomationMetadata(Control control, PasteSpecialToggleDescriptor descriptor) =>
        SetAutomationMetadata(
            control,
            UiText.Get(descriptor.WpfAutomationNameTextKey),
            descriptor.WpfAutomationId,
            UiText.Get(descriptor.WpfAutomationHelpTextKey));

    private static void ApplyAutomationMetadata(Control control, PasteSpecialOperationDescriptor descriptor) =>
        SetAutomationMetadata(
            control,
            UiText.Get(descriptor.WpfAutomationNameTextKey),
            descriptor.WpfAutomationId,
            UiText.Get(descriptor.WpfAutomationHelpTextKey));

    private static void ApplyAutomationMetadata(Control control, PasteSpecialDialogActionDescriptor descriptor) =>
        SetAutomationMetadata(
            control,
            UiText.Get(descriptor.WpfAutomationNameTextKey),
            descriptor.WpfAutomationId,
            UiText.Get(descriptor.WpfAutomationHelpTextKey));

    private static void SetAutomationMetadata(Control control, string name, string automationId, string helpText)
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetHelpText(control, helpText);
    }

    private static void AddPasteChoice(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }

    private static RadioButton CreateOperationButton(PasteSpecialOperationDescriptor operation) =>
        new()
        {
            Content = UiText.Get(operation.WpfLabelTextKey),
            GroupName = "PasteSpecialOperation",
            IsChecked = operation.IsDefault,
            IsEnabled = operation.IsEnabled,
            Margin = new Thickness(0, 0, 12, 6)
        };

    private Grid CreateOperationPanel()
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var operation in PasteSpecialPlanner.Surface.Operations.OrderBy(descriptor => descriptor.Order))
        {
            AddOperation(
                panel,
                _operationButtons[operation.Operation],
                operation.Placement.Row,
                operation.Placement.Column);
        }
        return panel;
    }

    private CheckBox GetToggleControl(PasteSpecialToggleKind kind) =>
        kind switch
        {
            PasteSpecialToggleKind.SkipBlanks => _skipBlanks,
            PasteSpecialToggleKind.Transpose => _transpose,
            PasteSpecialToggleKind.KeepColumnWidths => _keepColumnWidths,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static void AddOperation(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }
}
