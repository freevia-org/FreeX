using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ActivateSheetDialogResult(SheetId SheetId);

public sealed class ActivateSheetDialog : Window
{
    private readonly ListBox _sheetList = new();
    private readonly Button _okButton = new() { Content = UiText.Ok, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _cancelButton = new() { Content = UiText.Cancel, Width = 72, IsCancel = true };

    public ActivateSheetDialogResult Result { get; private set; }

    public ActivateSheetDialog(Workbook workbook, SheetId activeSheetId)
    {
        var targets = BuildTargets(workbook).ToList();
        var selectedTarget = targets.FirstOrDefault(target => target.SheetId == activeSheetId)
            ?? targets.FirstOrDefault();
        Result = new ActivateSheetDialogResult(selectedTarget?.SheetId ?? activeSheetId);

        Title = UiText.Get("ActivateSheet_Title");
        Width = 280;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _sheetList.ItemsSource = targets;
        _sheetList.SelectedItem = selectedTarget;
        _sheetList.SelectionMode = SelectionMode.Single;
        _sheetList.MinHeight = 210;
        AutomationProperties.SetName(_sheetList, UiText.Get("ActivateSheet_ListAutomationName"));
        AutomationProperties.SetAutomationId(_sheetList, "ActivateSheetList");
        AutomationProperties.SetHelpText(_sheetList, UiText.Get("ActivateSheet_ListHelpText"));
        _sheetList.SelectionChanged += (_, _) => UpdateButtonState();
        _sheetList.MouseDoubleClick += SheetList_MouseDoubleClick;

        AutomationProperties.SetName(_okButton, UiText.Get("ActivateSheet_OkAutomationName"));
        AutomationProperties.SetAutomationId(_okButton, "ActivateSheetOkButton");
        AutomationProperties.SetHelpText(_okButton, UiText.Get("ActivateSheet_OkHelpText"));
        _okButton.Click += (_, _) => Accept();
        AutomationProperties.SetName(_cancelButton, UiText.Get("ActivateSheet_CancelAutomationName"));
        AutomationProperties.SetAutomationId(_cancelButton, "ActivateSheetCancelButton");
        AutomationProperties.SetHelpText(_cancelButton, UiText.Get("ActivateSheet_CancelHelpText"));

        Content = CreateContent();
        UpdateButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static IEnumerable<ActivateSheetTarget> BuildTargets(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets.Where(sheet => !sheet.IsHidden))
            yield return new ActivateSheetTarget(sheet.Name, sheet.Id);
    }

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        _sheetList.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(_sheetList);
        stack.Children.Add(CreateButtonRow());
        return stack;
    }

    private UIElement CreateButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        row.Children.Add(_okButton);
        row.Children.Add(_cancelButton);
        return row;
    }

    private void FocusInitialKeyboardTarget()
    {
        _sheetList.Focus();
        Keyboard.Focus(_sheetList);
    }

    private void UpdateButtonState()
    {
        _okButton.IsEnabled = _sheetList.SelectedItem is ActivateSheetTarget;
    }

    private bool Accept()
    {
        if (_sheetList.SelectedItem is not ActivateSheetTarget target)
            return false;

        Result = new ActivateSheetDialogResult(target.SheetId);
        DialogResult = true;
        return true;
    }

    private void SheetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Accept())
            e.Handled = true;
    }

    private sealed record ActivateSheetTarget(string DisplayName, SheetId SheetId)
    {
        public override string ToString() => DisplayName;
    }
}
