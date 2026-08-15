using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class MoveOrCopySheetDialog : Window
{
    private readonly ComboBox _bookBox = new();
    private readonly ListBox _beforeSheetBox = new();
    private readonly CheckBox _createCopyBox = new();
    private readonly Button _okButton = new() { Content = UiText.Ok, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _cancelButton = new() { Content = UiText.Cancel, Width = 72, IsCancel = true };
    private readonly int _sheetCount;

    public MoveCopySheetPlan Result { get; private set; }

    public MoveOrCopySheetDialog(Workbook workbook, SheetId sourceSheetId)
    {
        _sheetCount = workbook.Sheets.Count;
        var sourceIndex = FindSheetIndexOrZero(workbook, sourceSheetId);
        var targets = MoveCopySheetPlanner.BuildTargets(
            workbook.Sheets.Select(sheet => sheet.Name).ToArray(),
            UiText.Get("MoveOrCopySheet_MoveToEnd"));
        Result = CreateResult(sourceIndex, createCopy: false, _sheetCount);

        Title = UiText.Get("MoveOrCopySheet_Title");
        Width = 360;
        Height = 310;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _bookBox.ItemsSource = new[] { workbook.Name };
        _bookBox.SelectedIndex = 0;
        _bookBox.IsEnabled = false;
        AutomationProperties.SetName(_bookBox, UiText.Get("MoveOrCopySheet_ToBookAutomationName"));
        AutomationProperties.SetAutomationId(_bookBox, "MoveOrCopySheetToBookBox");
        AutomationProperties.SetHelpText(_bookBox, UiText.Get("MoveOrCopySheet_ToBookHelpText"));

        _beforeSheetBox.ItemsSource = targets;
        _beforeSheetBox.DisplayMemberPath = nameof(MoveCopySheetTarget.DisplayName);
        _beforeSheetBox.SelectedIndex = MoveCopySheetPlanner.InitialTargetIndex(targets, sourceIndex);
        _beforeSheetBox.SelectionMode = SelectionMode.Single;
        _beforeSheetBox.MinHeight = 112;
        AutomationProperties.SetName(_beforeSheetBox, UiText.Get("MoveOrCopySheet_BeforeSheetAutomationName"));
        AutomationProperties.SetAutomationId(_beforeSheetBox, "MoveOrCopySheetBeforeSheetList");
        AutomationProperties.SetHelpText(_beforeSheetBox, UiText.Get("MoveOrCopySheet_BeforeSheetHelpText"));
        _beforeSheetBox.SelectionChanged += (_, _) => UpdateButtonState();
        _beforeSheetBox.MouseDoubleClick += BeforeSheetBox_MouseDoubleClick;

        _createCopyBox.Content = UiText.Get("MoveOrCopySheet_CreateACopy");
        AutomationProperties.SetName(_createCopyBox, UiText.CreateAutomationName(UiText.Get("MoveOrCopySheet_CreateACopy")));
        AutomationProperties.SetAutomationId(_createCopyBox, "MoveOrCopySheetCreateCopyCheckBox");
        AutomationProperties.SetHelpText(_createCopyBox, UiText.Get("MoveOrCopySheet_CreateCopyHelpText"));

        AutomationProperties.SetName(_okButton, UiText.Get("MoveOrCopySheet_OkAutomationName"));
        AutomationProperties.SetAutomationId(_okButton, "MoveOrCopySheetOkButton");
        AutomationProperties.SetHelpText(_okButton, UiText.Get("MoveOrCopySheet_OkHelpText"));
        _okButton.Click += (_, _) => Accept();
        AutomationProperties.SetName(_cancelButton, UiText.Get("MoveOrCopySheet_CancelAutomationName"));
        AutomationProperties.SetAutomationId(_cancelButton, "MoveOrCopySheetCancelButton");
        AutomationProperties.SetHelpText(_cancelButton, UiText.Get("MoveOrCopySheet_CancelHelpText"));

        Content = CreateContent();
        UpdateButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static MoveCopySheetPlan CreateResult(
        int insertBeforeIndex,
        bool createCopy,
        int sheetCount) =>
        MoveCopySheetPlanner.CreatePlan(insertBeforeIndex, createCopy, sheetCount);

    private static int FindSheetIndexOrZero(Workbook workbook, SheetId sourceSheetId)
        => Math.Max(0, workbook.IndexOfSheet(sourceSheetId));

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get("MoveOrCopySheet_MoveSelectedSheets"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new Label
        {
            Content = UiText.Get("MoveOrCopySheet_ToBook"),
            Target = _bookBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        _bookBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_bookBox);

        stack.Children.Add(new Label
        {
            Content = UiText.Get("MoveOrCopySheet_BeforeSheet"),
            Target = _beforeSheetBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        _beforeSheetBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_beforeSheetBox);

        _createCopyBox.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(_createCopyBox);
        stack.Children.Add(CreateButtonRow());
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _beforeSheetBox.Focus();
        Keyboard.Focus(_beforeSheetBox);
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

    private void UpdateButtonState()
    {
        _okButton.IsEnabled = _beforeSheetBox.SelectedItem is MoveCopySheetTarget;
    }

    private bool Accept()
    {
        if (_beforeSheetBox.SelectedItem is not MoveCopySheetTarget target)
            return false;

        Result = CreateResult(target.InsertBeforeIndex, _createCopyBox.IsChecked == true, _sheetCount);
        DialogResult = true;
        return true;
    }

    private void BeforeSheetBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Accept())
            e.Handled = true;
    }

}
