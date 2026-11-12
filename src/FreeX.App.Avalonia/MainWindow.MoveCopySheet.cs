using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.SheetUI;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle MoveCopySheetDialogChromeStyle => new(FormulaBarFontFamily);

    // Sheet-tab context menu ▸ Move or Copy (parity with Excel's Move or Copy dialog and the WPF
    // host's MoveOrCopySheetDialog). Pick a "Before sheet" position (or "move to end") and optionally
    // tick "Create a copy". The position/list/clamp logic lives in the portable MoveCopySheetPlanner;
    // the host maps the resulting plan onto the Core sheet commands (DuplicateSheetCommand to copy,
    // MoveSheetCommand to reposition), both undo/redo aware via the shared session command path.

    /// <summary>Opens the Move-or-Copy dialog for the active sheet.</summary>
    private void ShowMoveOrCopySheetDialog() => RunGuarded(ShowMoveOrCopySheetDialogAsync);

    private async Task ShowMoveOrCopySheetDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheetNames = _session.SheetTabs.Select(tab => tab.Name).ToList();
        var sourceIndex = FindActiveSheetTabIndex();
        var targets = MoveCopySheetPlanner.BuildTargets(sheetNames, UiText.Get("MoveCopySheet_MoveToEnd"));
        var initialIndex = MoveCopySheetPlanner.InitialTargetIndex(targets, sourceIndex);

        var beforeSheetList = new ListBox
        {
            ItemsSource = targets.Select(target => target.DisplayName).ToList(),
            SelectedIndex = initialIndex,
            // WPF: MinHeight = 112, 10px above the "Create a copy" box.
            Height = 112,
            Margin = new Thickness(0, 0, 0, 10),
        };
        ApplySheetListBoxStyle(beforeSheetList);
        AutomationProperties.SetAutomationId(beforeSheetList, "MoveCopySheetBeforeSheetList");

        // WPF "To book:" combo: disabled, listing only the current workbook.
        var toBookBox = new ComboBox
        {
            ItemsSource = new[] { _session.Workbook.Name },
            SelectedIndex = 0,
            IsEnabled = false,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(toBookBox, MoveCopySheetDialogChromeStyle);
        AutomationProperties.SetName(toBookBox, UiText.Get("MoveOrCopySheet_ToBookAutomationName"));
        AutomationProperties.SetAutomationId(toBookBox, "MoveCopySheetToBookBox");

        // WPF: a ~16px check box with a 14px bottom margin; the compact check box is a 20px row.
        var createCopyBox = new CheckBox { Content = UiText.Get("MoveCopySheet_CreateACopy"), Margin = new Thickness(0, 0, 0, 10) };
        ApplySheetCheckBoxChrome(createCopyBox);
        AutomationProperties.SetAutomationId(createCopyBox, "MoveCopySheetCreateCopyCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("Common_OkText"),
            IsDefault = true,
            MinWidth = 72,
        };
        ApplySheetButtonChrome(okButton, 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "MoveCopySheetOkButton");
        var cancelButton = new Button
        {
            Content = UiText.Get("Common_CancelText"),
            IsCancel = true,
            MinWidth = 72,
        };
        ApplySheetButtonChrome(cancelButton, 72);
        AutomationProperties.SetAutomationId(cancelButton, "MoveCopySheetCancelButton");

        var dialog = new Window
        {
            Title = UiText.Get("MoveCopySheet_Title"),
            // WPF MoveOrCopySheetDialog is a 360x310 NoResize window; the height is
            // sized to the content instead of guessing a caption allowance.
            Width = 360,
            MinWidth = 360,
            MaxWidth = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "MoveCopySheetDialog");

        var accepted = false;
        okButton.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);

        // WPF CreateContent: 16px root margin; intro line 12px above the first label; regular-weight
        // labels 4px above their control; 10px between the controls; OK/Cancel 72 wide.
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = UiText.Get("MoveCopySheet_MoveSelectedSheets"), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("MoveOrCopySheet_ToBook")), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 4) },
                toBookBox,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("MoveCopySheet_BeforeSheet")), FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 4) },
                beforeSheetList,
                createCopyBox,
                buttonRow,
            },
        };

        await dialog.ShowDialog(this);
        if (!accepted)
            return;

        var selectedTargetIndex = beforeSheetList.SelectedIndex;
        if (selectedTargetIndex < 0 || selectedTargetIndex >= targets.Count)
            return;

        var plan = MoveCopySheetPlanner.CreatePlan(
            targets[selectedTargetIndex].InsertBeforeIndex,
            createCopyBox.IsChecked == true,
            _session.SheetTabs.Count);

        ApplyMoveOrCopySheetPlan(plan);
    }

    private void ApplyMoveOrCopySheetPlan(MoveCopySheetPlan plan)
    {
        var sourceName = _session.ActiveSheet.Name;
        var result = _session.MoveOrCopySelectedSheets(plan.InsertBeforeIndex, plan.CreateCopy);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? (plan.CreateCopy
                ? UiText.Get("ShellLoc_CopySheetFailed")
                : UiText.Get("ShellLoc_MoveSheetFailed")));
            return;
        }

        RefreshShell(UiText.Format(
            plan.CreateCopy ? "MoveCopySheet_CopiedStatus" : "MoveCopySheet_MovedStatus",
            sourceName));
    }

    // ── Visual chrome helpers (MoveCopySheet / SheetTabColor dialogs) ─────────

    /// <summary>
    /// Applies standard Sheet-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// <paramref name="minWidth"/> sets MinWidth; <paramref name="isDefault"/> uses blue border for the
    /// default/OK button.
    /// </summary>
    private static void ApplySheetButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, MoveCopySheetDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard Sheet-dialog check-box chrome (MinHeight=20, MaxHeight=20, FontSize=12).
    /// </summary>
    private static void ApplySheetCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, MoveCopySheetDialogChromeStyle);
    }

    /// <summary>
    /// Applies standard Sheet-dialog list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplySheetListBoxStyle(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, MoveCopySheetDialogChromeStyle);
}
