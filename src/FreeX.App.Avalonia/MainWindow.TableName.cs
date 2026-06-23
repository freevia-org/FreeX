using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Table Name" dialog for the Avalonia/macOS shell: a single text box pre-filled with the
/// active table's current display name, validated for uniqueness and Excel's name format before the rename
/// runs. The current-name capture and the validation come from the portable <see cref="TableNamePlanner"/>
/// (which single-sources onto the shared Core <see cref="StructuredTableDesignCommandHelpers.ValidateTableName"/>
/// guard) so the behavior matches the WPF host and is reusable on macOS. The result round-trips through
/// <see cref="RenameStructuredTableCommand"/>. Reached from the Table Design ▸ Table Name ribbon command
/// (<c>tableDesign.tableName</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Table Design ▸ Table Name — opens the rename dialog for the active table and applies the validated name
    /// through the Core rename command. Reports an honest status when no table is active.
    /// </summary>
    private void OpenTableName()
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("TableName_NoTable"));
            return;
        }

        _ = OpenTableNameDialogAsync(table);
    }

    private async Task OpenTableNameDialogAsync(StructuredTableModel table)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;

        var nameBox = new TextBox
        {
            Text = TableNamePlanner.Capture(table),
            MinWidth = 280,
        };
        ApplyTableNameTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "TableNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("TableName_BoxName"));

        var dialog = new Window
        {
            Title = UiText.Get("TableName_Title"),
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TableNameDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyTableNameButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "TableNameOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyTableNameButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "TableNameCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!TableNamePlanner.TryCreateRename(
                    _session.Workbook, sheetId, table.Id, nameBox.Text, out _, out var error))
            {
                ShowEditIssue(error ?? UiText.Get("TableName_Failed"));
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("TableName_Label"),
            Foreground = HeaderForeground,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });
        content.Children.Add(nameBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!TableNamePlanner.TryCreateRename(
                _session.Workbook, sheetId, table.Id, nameBox.Text, out var values, out var lateError))
        {
            ShowEditIssue(lateError ?? UiText.Get("TableName_Failed"));
            return;
        }

        var renameValues = values!;
        var result = _session.ExecuteReviewCommand(
            TableDesignCommandPlanner.BuildRenameCommand(sheetId, table, renameValues));

        if (result.Success)
        {
            RefreshTableContextualTab();
            RefreshShell(UiText.Format("TableName_Renamed", renameValues.Name));
        }
        else
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableName_Failed"));
        }
    }

    // ── Visual chrome helpers (TableName dialog) ─────────────────────────────

    /// <summary>
    /// Applies standard TableName-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// </summary>
    private static void ApplyTableNameButtonChrome(Button button, double minWidth, bool isDefault = false)
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
    /// Applies standard TableName-dialog text-box chrome (Height=24, Padding=(4,1), FontSize=12, grey border).
    /// </summary>
    private static void ApplyTableNameTextBoxChrome(TextBox textBox)
    {
        textBox.Height = 24;
        textBox.MinHeight = 24;
        textBox.MaxHeight = 24;
        textBox.Padding = new Thickness(4, 1);
        textBox.FontSize = 12;
        textBox.FontFamily = FormulaBarFontFamily;
        textBox.BorderBrush = Brush(130, 130, 130);
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }
}
