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

using FreeX.App.Presentation.SheetUI;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Sheet-tab context menu ▸ Move or Copy (parity with Excel's Move or Copy dialog and the WPF
    // host's MoveOrCopySheetDialog). Pick a "Before sheet" position (or "move to end") and optionally
    // tick "Create a copy". The position/list/clamp logic lives in the portable MoveCopySheetPlanner;
    // the host maps the resulting plan onto the Core sheet commands (DuplicateSheetCommand to copy,
    // MoveSheetCommand to reposition), both undo/redo aware via the shared session command path.

    /// <summary>Opens the Move-or-Copy dialog for the active sheet.</summary>
    private void ShowMoveOrCopySheetDialog() => _ = ShowMoveOrCopySheetDialogAsync();

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
            MinHeight = 132,
        };
        ApplySheetListBoxStyle(beforeSheetList);
        AutomationProperties.SetAutomationId(beforeSheetList, "MoveCopySheetBeforeSheetList");

        var createCopyBox = new CheckBox { Content = UiText.Get("MoveCopySheet_CreateACopy") };
        ApplySheetCheckBoxChrome(createCopyBox);
        AutomationProperties.SetAutomationId(createCopyBox, "MoveCopySheetCreateCopyCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Ok"),
            IsDefault = true,
            MinWidth = 84,
        };
        ApplySheetButtonChrome(okButton, 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "MoveCopySheetOkButton");
        var cancelButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ApplySheetButtonChrome(cancelButton, 84);
        AutomationProperties.SetAutomationId(cancelButton, "MoveCopySheetCancelButton");

        var dialog = new Window
        {
            Title = UiText.Get("MoveCopySheet_Title"),
            Width = 340,
            Height = 320,
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { okButton, cancelButton },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("MoveCopySheet_MoveSelectedSheets"), FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TextBlock { Text = UiText.Get("MoveCopySheet_BeforeSheet"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
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

        ApplyMoveOrCopySheetPlan(plan, sourceIndex);
    }

    private void ApplyMoveOrCopySheetPlan(MoveCopySheetPlan plan, int sourceIndex)
    {
        var sourceName = _session.ActiveSheet.Name;

        if (plan.CreateCopy)
        {
            var copyResult = _session.DuplicateActiveSheet();
            if (!copyResult.Success)
            {
                ShowEditIssue(copyResult.ErrorMessage ?? UiText.Get("ShellLoc_CopySheetFailed"));
                return;
            }

            // DuplicateActiveSheet drops the copy immediately after the source and makes it active;
            // move that copy to the requested position when it differs from the landing slot.
            var copyIndex = System.Math.Min(sourceIndex + 1, _session.SheetTabs.Count - 1);
            var targetIndex = MoveCopySheetPlanner.ResolveCopyTargetIndex(
                sourceIndex,
                plan.InsertBeforeIndex,
                _session.SheetTabs.Count - 1);
            if (copyIndex != targetIndex && !TryMoveActiveSheetTo(targetIndex))
                return;

            RefreshShell(UiText.Format("MoveCopySheet_CopiedStatus", sourceName));
            return;
        }

        var landingIndex = MoveCopySheetPlanner.ResolveMoveTargetIndex(
            sourceIndex,
            plan.InsertBeforeIndex,
            _session.SheetTabs.Count);
        if (landingIndex != sourceIndex && !TryMoveActiveSheetTo(landingIndex))
            return;

        RefreshShell(UiText.Format("MoveCopySheet_MovedStatus", sourceName));
    }

    private bool TryMoveActiveSheetTo(int targetIndex)
    {
        var result = _session.MoveActiveSheetTo(targetIndex);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_MoveSheetFailed"));
            return false;
        }

        return true;
    }

    // ── Visual chrome helpers (MoveCopySheet / SheetTabColor dialogs) ─────────

    /// <summary>
    /// Applies standard Sheet-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// <paramref name="minWidth"/> sets MinWidth; <paramref name="isDefault"/> uses blue border for the
    /// default/OK button.
    /// </summary>
    private static void ApplySheetButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        button.MinWidth = minWidth;
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        button.Padding = new Thickness(4, 1);
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);
        button.BorderThickness = new Thickness(1);
        button.FontSize = 12;
        button.FontFamily = FormulaBarFontFamily;
        button.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        button.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    /// <summary>
    /// Applies standard Sheet-dialog check-box chrome (MinHeight=20, MaxHeight=20, FontSize=12).
    /// </summary>
    private static void ApplySheetCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.FontSize = 12;
        checkBox.FontFamily = FormulaBarFontFamily;
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
    }

    /// <summary>
    /// Applies standard Sheet-dialog list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplySheetListBoxStyle(ListBox listBox)
    {
        listBox.FontSize = 12;
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 1)),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.FontSizeProperty, 12.0),
            },
        });
    }
}
