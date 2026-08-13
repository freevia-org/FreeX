using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Free.Shared.Shell.Wpf;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ActivateSheetDialog : DialogWindow
{
    private const double DialogWidth = 352;
    private const double DialogHeight = 380;
    private const double ExcelButtonWidth = 90;
    private const int GwlExStyle = -20;
    private const int WsExContextHelp = 0x00000400;

    private readonly ListBox _sheetList = new();
    private readonly Button _okButton = new() { Content = UiText.Ok, Width = ExcelButtonWidth };
    private readonly Button _cancelButton = new() { Content = UiText.Cancel, Width = ExcelButtonWidth };

    public ActivateSheetDialogResult Result { get; private set; }

    public ActivateSheetDialog(Workbook workbook, SheetId activeSheetId)
    {
        var targets = SheetDialogPlanner.BuildActivateSheetTargets(workbook);
        var selectedTarget = targets.Count == 0
            ? null
            : targets[0];
        Result = SheetDialogPlanner.CreateActivateSheetResult(selectedTarget?.SheetId ?? activeSheetId);

        Title = UiText.Get("ActivateSheet_Title");
        Width = DialogWidth;
        Height = DialogHeight;
        ResizeMode = ResizeMode.NoResize;
        SourceInitialized += (_, _) => ApplyContextHelpButtonStyle();

        _sheetList.ItemsSource = targets;
        _sheetList.SelectedItem = selectedTarget;
        _sheetList.SelectionMode = SelectionMode.Single;
        _sheetList.ItemContainerStyle = CreateSheetListItemStyle();
        _sheetList.Height = 260;
        AutomationProperties.SetName(_sheetList, UiText.Get("ActivateSheet_ListAutomationName"));
        AutomationProperties.SetAutomationId(_sheetList, FreeXAutomationIdCatalog.ActivateSheetList);
        AutomationProperties.SetHelpText(_sheetList, UiText.Get("ActivateSheet_ListHelpText"));
        _sheetList.SelectionChanged += (_, _) => UpdateButtonState();
        _sheetList.MouseDoubleClick += SheetList_MouseDoubleClick;

        AutomationProperties.SetName(_okButton, UiText.Get("ActivateSheet_OkAutomationName"));
        AutomationProperties.SetAutomationId(_okButton, FreeXAutomationIdCatalog.ActivateSheetOkButton);
        AutomationProperties.SetHelpText(_okButton, UiText.Get("ActivateSheet_OkHelpText"));
        _okButton.Click += (_, _) => Accept();
        AutomationProperties.SetName(_cancelButton, UiText.Get("ActivateSheet_CancelAutomationName"));
        AutomationProperties.SetAutomationId(_cancelButton, FreeXAutomationIdCatalog.ActivateSheetCancelButton);
        AutomationProperties.SetHelpText(_cancelButton, UiText.Get("ActivateSheet_CancelHelpText"));

        Content = CreateContent();
        UpdateButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent()
    {
        var grid = new Grid { Margin = new Thickness(10, 8, 10, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new Label
        {
            Content = UiText.Get("ActivateSheet_Title") + ":",
            Target = _sheetList,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        };
        grid.Children.Add(label);

        _sheetList.Margin = new Thickness(0, 0, 0, 16);
        Grid.SetRow(_sheetList, 1);
        grid.Children.Add(_sheetList);

        var buttons = DialogButtonRowFactory.Create(_okButton, _cancelButton);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.Focus(_sheetList);
    }

    private static Style CreateSheetListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Resources.Add(SystemColors.InactiveSelectionHighlightBrushKey, SystemColors.HighlightBrush);
        style.Resources.Add(SystemColors.InactiveSelectionHighlightTextBrushKey, SystemColors.HighlightTextBrush);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 10.5));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 13.0));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, SystemColors.ControlTextBrush));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private void UpdateButtonState()
    {
        _okButton.IsEnabled = _sheetList.SelectedItem is SheetDialogTarget;
    }

    private bool Accept()
    {
        if (_sheetList.SelectedItem is not SheetDialogTarget target)
            return false;

        Result = SheetDialogPlanner.CreateActivateSheetResult(target.SheetId);
        DialogResult = true;
        return true;
    }

    private void SheetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Accept())
            e.Handled = true;
    }

    private void ApplyContextHelpButtonStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style.ToInt64() | WsExContextHelp));
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, index)
            : new IntPtr(GetWindowLong32(handle, index));

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(handle, index, value)
            : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
}
