using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Pivot;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly IReadOnlyList<(PivotCreatePlanner.FieldRole Role, string Label)> PivotFieldRoleChoices =
    [
        (PivotCreatePlanner.FieldRole.Unused, "(Unused)"),
        (PivotCreatePlanner.FieldRole.Row, "Row"),
        (PivotCreatePlanner.FieldRole.Value, "Value"),
    ];

    /// <summary>
    /// Opens the Insert PivotTable dialog for the current selection. The selection is the pivot's source
    /// range (header row + data); the user assigns each source column to the Row or Values area (defaults
    /// proposed by <see cref="PivotCreatePlanner.DefaultRoles"/>) and chooses a new worksheet or an in-place
    /// target. On OK the Core <c>AddPivotTable…</c> command runs and the pivot renders; the field pane then
    /// refines it. Surfaces the Core guard message (e.g. needs a header row and a data field) on failure.
    /// </summary>
    private async Task ShowInsertPivotTableDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var source = _session.SelectedRange;
        if (!PivotCreatePlanner.IsValidSource(source))
        {
            ShowEditIssue("Select a range with a header row and at least one data row to insert a PivotTable.");
            return;
        }

        var fields = PivotCreatePlanner.ReadFields(_session.ActiveSheet, source);
        var defaults = PivotCreatePlanner.DefaultRoles(fields);

        var dialog = new Window
        {
            Title = "Insert PivotTable",
            Width = 420,
            Height = 460,
            MinWidth = 360,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertPivotTableDialog");

        var roleBoxes = new Dictionary<int, ComboBox>();
        var fieldsPanel = new StackPanel { Spacing = 6 };
        foreach (var field in fields)
        {
            var roleBox = new ComboBox
            {
                ItemsSource = PivotFieldRoleChoices.Select(c => c.Label).ToList(),
                SelectedIndex = RoleIndex(defaults.TryGetValue(field.Index, out var r) ? r : PivotCreatePlanner.FieldRole.Unused),
                MinWidth = 130,
            };
            AutomationProperties.SetAutomationId(roleBox, $"PivotFieldRole{field.Index}");
            roleBoxes[field.Index] = roleBox;

            fieldsPanel.Children.Add(new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = field.Header,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Width = 200,
                    },
                    roleBox,
                },
            });
        }

        var newSheetBox = new CheckBox { Content = "Place on a new worksheet", IsChecked = true };
        AutomationProperties.SetAutomationId(newSheetBox, "PivotNewWorksheetBox");

        var errorText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(errorText, "InsertPivotTableErrorText");

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "InsertPivotTableOkButton");
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "InsertPivotTableCancelButton");

        okButton.Click += (_, _) =>
        {
            var roles = new Dictionary<int, PivotCreatePlanner.FieldRole>();
            foreach (var (index, box) in roleBoxes)
                roles[index] = PivotFieldRoleChoices[Math.Max(0, box.SelectedIndex)].Role;

            var rowIndexes = PivotCreatePlanner.RowIndexes(roles);
            var dataIndexes = PivotCreatePlanner.ValueIndexes(roles);
            if (dataIndexes.Count == 0)
            {
                errorText.Text = "Assign at least one column to the Values area.";
                errorText.IsVisible = true;
                return;
            }

            CellAddress? target = newSheetBox.IsChecked == true
                ? null
                : new CellAddress(_session.ActiveSheet.Id, source.End.Row + 2, source.Start.Col);

            var command = PivotCreatePlanner.BuildCommand(
                source,
                PivotCreatePlanner.SuggestName(_session.Workbook),
                rowIndexes,
                dataIndexes,
                _session.ActiveSheet.Id,
                target);

            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                errorText.Text = result.ErrorMessage ?? "Insert PivotTable failed.";
                errorText.IsVisible = true;
                return;
            }

            dialog.Close();
            RefreshShell($"Inserted PivotTable from {FormatRangeReference(source)}");
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Source: {FormatRangeReference(source)}. Assign each column to the Row or Values area.",
                            Foreground = HeaderForeground,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new ScrollViewer { Content = fieldsPanel, MaxHeight = 240 },
                        newSheetBox,
                        errorText,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    private static int RoleIndex(PivotCreatePlanner.FieldRole role)
    {
        for (var i = 0; i < PivotFieldRoleChoices.Count; i++)
            if (PivotFieldRoleChoices[i].Role == role)
                return i;
        return 0;
    }
}
