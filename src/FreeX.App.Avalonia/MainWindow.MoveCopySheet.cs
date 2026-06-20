using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.SheetUI;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

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
        AutomationProperties.SetAutomationId(beforeSheetList, "MoveCopySheetBeforeSheetList");

        var createCopyBox = new CheckBox { Content = UiText.Get("MoveCopySheet_CreateACopy") };
        AutomationProperties.SetAutomationId(createCopyBox, "MoveCopySheetCreateCopyCheckBox");

        var okButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Ok"),
            IsDefault = true,
            MinWidth = 84,
        };
        AutomationProperties.SetAutomationId(okButton, "MoveCopySheetOkButton");
        var cancelButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
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
                new TextBlock { Text = UiText.Get("MoveCopySheet_MoveSelectedSheets") },
                new TextBlock { Text = UiText.Get("MoveCopySheet_BeforeSheet"), FontWeight = FontWeight.SemiBold },
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
}
