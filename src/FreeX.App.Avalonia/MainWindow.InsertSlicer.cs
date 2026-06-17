using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Pivot;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert ▸ Slicer / Insert ▸ Timeline for the active PivotTable. Both commands resolve the active (or first)
/// pivot on the active sheet, present a compact field picker (checkbox list for slicers, single-select list for
/// timelines), then create one <see cref="SlicerModel"/> per chosen field via <see cref="AddSlicerCommand"/> /
/// <see cref="AddTimelineCommand"/> through the shared review-command path. The core command validates that a
/// timeline field actually contains dates and surfaces an error if not; the shell relays that message rather
/// than crashing. When no pivot is present, the shell just shows an explanatory status line.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Opens the Insert Slicer field picker for the active PivotTable (Insert-tab ribbon button).</summary>
    private void InsertSlicer() => _ = ShowInsertSlicerDialogAsync();

    /// <summary>Opens the Insert Timeline field picker for the active PivotTable (Insert-tab ribbon button).</summary>
    private void InsertTimeline() => _ = ShowInsertTimelineDialogAsync();

    /// <summary>
    /// Resolves the active pivot for the current cell, falling back to the first pivot on the active sheet so the
    /// command still works when the selection has drifted off the report. Returns null when the sheet has none.
    /// </summary>
    private PivotTableModel? ResolveInsertControlPivot()
    {
        var sheet = _session.ActiveSheet;
        var pivot = PivotSourceContext.FindActivePivot(sheet, _session.ActiveCell);
        if (pivot is not null)
            return pivot;

        return sheet.PivotTables.Count > 0 ? sheet.PivotTables[0] : null;
    }

    private async Task ShowInsertSlicerDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell("Select a cell inside a PivotTable to insert a slicer.");
            return;
        }

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        if (headers.Count == 0)
        {
            RefreshShell("The PivotTable has no fields to slice on.");
            return;
        }

        var dialog = new Window
        {
            Title = "Insert Slicers",
            Width = 320,
            Height = 420,
            MinWidth = 280,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertSlicerDialog");

        var checkBoxes = new List<CheckBox>(headers.Count);
        var fieldStack = new StackPanel { Spacing = 4 };
        foreach (var header in headers)
        {
            var checkBox = new CheckBox { Content = header };
            checkBoxes.Add(checkBox);
            fieldStack.Children.Add(checkBox);
        }

        var fieldList = new ListBox { ItemsSource = checkBoxes };
        AutomationProperties.SetAutomationId(fieldList, "InsertSlicerFieldList");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "InsertSlicerWarningText");

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "InsertSlicerOkButton");
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "InsertSlicerCancelButton");

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var selectedFields = checkBoxes
                .Where(box => box.IsChecked == true && box.Content is string)
                .Select(box => (string)box.Content!)
                .ToList();

            if (selectedFields.Count == 0)
            {
                warningText.Text = "Select at least one field.";
                warningText.IsVisible = true;
                return;
            }

            if (!TryInsertSlicerControls(pivot, selectedFields, out var error))
            {
                warningText.Text = error;
                warningText.IsVisible = true;
                return;
            }

            dialog.Close();
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
                new DockPanel
                {
                    Children =
                    {
                        DockTop(new TextBlock
                        {
                            Text = $"Choose fields from \"{pivot.Name}\" to create slicers:",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 8),
                        }),
                        DockBottom(warningText),
                        fieldList,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    private async Task ShowInsertTimelineDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell("Select a cell inside a PivotTable to insert a timeline.");
            return;
        }

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        if (headers.Count == 0)
        {
            RefreshShell("The PivotTable has no fields for a timeline.");
            return;
        }

        var dialog = new Window
        {
            Title = "Insert Timelines",
            Width = 320,
            Height = 420,
            MinWidth = 280,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertTimelineDialog");

        var checkBoxes = new List<CheckBox>(headers.Count);
        foreach (var header in headers)
            checkBoxes.Add(new CheckBox { Content = header });

        var fieldList = new ListBox { ItemsSource = checkBoxes };
        AutomationProperties.SetAutomationId(fieldList, "InsertTimelineFieldList");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "InsertTimelineWarningText");

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "InsertTimelineOkButton");
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "InsertTimelineCancelButton");

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var selectedFields = checkBoxes
                .Where(box => box.IsChecked == true && box.Content is string)
                .Select(box => (string)box.Content!)
                .ToList();

            if (selectedFields.Count == 0)
            {
                warningText.Text = "Select at least one date field.";
                warningText.IsVisible = true;
                return;
            }

            if (!TryInsertTimelineControls(pivot, selectedFields, out var error))
            {
                warningText.Text = error;
                warningText.IsVisible = true;
                return;
            }

            dialog.Close();
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
                new DockPanel
                {
                    Children =
                    {
                        DockTop(new TextBlock
                        {
                            Text = $"Choose date fields from \"{pivot.Name}\" to create timelines:",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 8),
                        }),
                        DockBottom(warningText),
                        fieldList,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Creates one slicer per selected field through <see cref="AddSlicerCommand"/>. Each name is made unique
    /// against the workbook's existing slicers. Returns false (with a message) on the first command rejection so
    /// the dialog can keep the picker open.
    /// </summary>
    private bool TryInsertSlicerControls(PivotTableModel pivot, IReadOnlyList<string> fields, out string error)
    {
        error = string.Empty;
        var applied = 0;
        foreach (var field in fields)
        {
            var name = MakeUniqueControlName(field, _session.Workbook.Slicers.Select(s => s.Name));
            var command = new AddSlicerCommand(name, pivot.Name, field);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                error = result.ErrorMessage ?? $"Could not insert a slicer for \"{field}\".";
                if (applied > 0)
                    RefreshShell($"Inserted {applied} slicer(s).");
                return false;
            }

            applied++;
        }

        RefreshShell(applied == 1 ? $"Inserted slicer for {fields[0]}." : $"Inserted {applied} slicers.");
        return true;
    }

    /// <summary>
    /// Creates one timeline per selected field through <see cref="AddTimelineCommand"/>. The core command rejects
    /// fields that do not contain dates ("Timeline source field must contain dates."); that message is surfaced
    /// verbatim. Returns false on the first rejection.
    /// </summary>
    private bool TryInsertTimelineControls(PivotTableModel pivot, IReadOnlyList<string> fields, out string error)
    {
        error = string.Empty;
        var applied = 0;
        foreach (var field in fields)
        {
            var name = MakeUniqueControlName(field, _session.Workbook.Timelines.Select(t => t.Name));
            var command = new AddTimelineCommand(name, pivot.Name, field);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                error = result.ErrorMessage ?? $"Could not insert a timeline for \"{field}\".";
                if (applied > 0)
                    RefreshShell($"Inserted {applied} timeline(s).");
                return false;
            }

            applied++;
        }

        RefreshShell(applied == 1 ? $"Inserted timeline for {fields[0]}." : $"Inserted {applied} timelines.");
        return true;
    }

    /// <summary>
    /// Produces a slicer/timeline name based on the field caption that does not collide with any
    /// <paramref name="existingNames"/> (case-insensitive), appending " 2", " 3", … as needed.
    /// </summary>
    private static string MakeUniqueControlName(string fieldName, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(fieldName) ? "Field" : fieldName.Trim();
        if (!taken.Contains(baseName))
            return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private static Control DockTop(Control control)
    {
        DockPanel.SetDock(control, Dock.Top);
        return control;
    }

    private static Control DockBottom(Control control)
    {
        DockPanel.SetDock(control, Dock.Bottom);
        return control;
    }
}
