using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert ▸ Slicer / Insert ▸ Timeline for the active PivotTable. Both commands resolve the active (or first)
/// pivot on the active sheet, present a compact field picker (checkbox list for slicers, single-select list for
/// timelines), then create one <see cref="SlicerModel"/> per chosen field through shared application plans.
/// The Core command behind each plan validates that a
/// timeline field actually contains dates and surfaces an error if not; the shell relays that message rather
/// than crashing. When no pivot is present, the shell just shows an explanatory status line.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Opens the Insert Slicer field picker for the active PivotTable (Insert-tab ribbon button).</summary>
    private void InsertSlicer() => RunGuarded(ShowInsertSlicerDialogAsync);

    /// <summary>Opens the Insert Timeline field picker for the active PivotTable (Insert-tab ribbon button).</summary>
    private void InsertTimeline() => RunGuarded(ShowInsertTimelineDialogAsync);

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
            RefreshShell(UiText.Get("PivotLoc_SelectCellForSlicer"));
            return;
        }

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot));
        if (headers.Count == 0)
        {
            RefreshShell(UiText.Get("PivotLoc_NoFieldsToSlice"));
            return;
        }

        var dialog = new Window
        {
            // WPF: a NoResize window PivotSlicerTimelineDialogContract.Width wide. This shell picks
            // several fields from a check list instead of one field plus a caption, so the height
            // follows the list rather than the WPF contract height.
            Title = UiText.Get("PivotSlicerTimeline_InsertSlicer"),
            Width = PivotSlicerTimelineDialogContract.Width,
            MinWidth = PivotSlicerTimelineDialogContract.Width,
            MaxWidth = PivotSlicerTimelineDialogContract.Width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertSlicerDialog");

        var checkBoxes = new List<CheckBox>(headers.Count);
        var fieldStack = new StackPanel();
        foreach (var header in headers)
        {
            var checkBox = new CheckBox { Content = header };
            ApplyDataOpsCheckBoxChrome(checkBox);
            checkBoxes.Add(checkBox);
            fieldStack.Children.Add(checkBox);
        }

        var fieldList = new ScrollViewer
        {
            Content = fieldStack,
            MaxHeight = 220,
        };
        AutomationProperties.SetAutomationId(fieldList, "InsertSlicerFieldList");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "InsertSlicerWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(okButton, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "InsertSlicerOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancelButton, PivotDialogButtonMinWidth);
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
                warningText.Text = UiText.Get("PivotLoc_SelectAtLeastOneField");
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

        var buttonRow = CreatePivotDialogButtonRow(okButton, cancelButton);

        // Layout mirrors the WPF InsertSlicer dialog: a 16px root margin, one captioned group box
        // (content inset 10,8,10,10, 12px above the button row's own 12px top margin) and the shared
        // pivot OK/Cancel row. The prompt naming the PivotTable labels the check list inside the group.
        var groupContent = new StackPanel();
        var groupPrompt = CreatePivotDialogLabel(UiText.Format("PivotLoc_ChooseFieldsForSlicers", pivot.Name));
        groupPrompt.TextWrapping = TextWrapping.Wrap;
        groupContent.Children.Add(groupPrompt);
        groupContent.Children.Add(fieldList);
        groupContent.Children.Add(warningText);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                CreatePivotDialogGroupBox(UiText.Get("PivotSlicerTimeline_ChooseFieldsGroup"), groupContent),
                buttonRow,
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
            RefreshShell(UiText.Get("PivotLoc_SelectCellForTimeline"));
            return;
        }

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot));
        if (headers.Count == 0)
        {
            RefreshShell(UiText.Get("PivotLoc_NoFieldsForTimeline"));
            return;
        }

        var dialog = new Window
        {
            // WPF: a NoResize window PivotSlicerTimelineDialogContract.Width wide. This shell picks
            // several fields from a check list instead of one field plus a caption, so the height
            // follows the list rather than the WPF contract height.
            Title = UiText.Get("PivotSlicerTimeline_InsertTimeline"),
            Width = PivotSlicerTimelineDialogContract.Width,
            MinWidth = PivotSlicerTimelineDialogContract.Width,
            MaxWidth = PivotSlicerTimelineDialogContract.Width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertTimelineDialog");

        var checkBoxes = new List<CheckBox>(headers.Count);
        var fieldStack = new StackPanel();
        foreach (var header in headers)
        {
            var checkBox = new CheckBox { Content = header };
            ApplyDataOpsCheckBoxChrome(checkBox);
            checkBoxes.Add(checkBox);
            fieldStack.Children.Add(checkBox);
        }

        var fieldList = new ScrollViewer
        {
            Content = fieldStack,
            MaxHeight = 220,
        };
        AutomationProperties.SetAutomationId(fieldList, "InsertTimelineFieldList");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "InsertTimelineWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(okButton, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "InsertTimelineOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancelButton, PivotDialogButtonMinWidth);
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
                warningText.Text = UiText.Get("PivotLoc_SelectAtLeastOneDateField");
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

        var buttonRow = CreatePivotDialogButtonRow(okButton, cancelButton);

        // Layout mirrors the WPF InsertTimeline dialog: a 16px root margin, one captioned group box
        // (content inset 10,8,10,10, 12px above the button row's own 12px top margin) and the shared
        // pivot OK/Cancel row. The prompt naming the PivotTable labels the check list inside the group.
        var groupContent = new StackPanel();
        var groupPrompt = CreatePivotDialogLabel(UiText.Format("PivotLoc_ChooseDateFieldsForTimelines", pivot.Name));
        groupPrompt.TextWrapping = TextWrapping.Wrap;
        groupContent.Children.Add(groupPrompt);
        groupContent.Children.Add(fieldList);
        groupContent.Children.Add(warningText);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                CreatePivotDialogGroupBox(UiText.Get("PivotSlicerTimeline_ChooseDateFieldsGroup"), groupContent),
                buttonRow,
            },
        };

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Creates one slicer per selected field through the shared Pivot application session. Each name is made unique
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
            var outcome = PivotApplication.Execute(
                PivotApplication.PlanInsertSlicer(
                    new PivotApplicationTarget(_session.ActiveSheet, pivot),
                    name,
                    field));
            if (!outcome.Success)
            {
                error = outcome.Message?.Detail ?? UiText.Format("PivotLoc_CouldNotInsertSlicer", field);
                if (applied > 0)
                    RefreshShell(UiText.Format("PivotLoc_InsertedSlicersCount", applied));
                return false;
            }

            applied++;
        }

        RefreshShell(applied == 1 ? UiText.Format("PivotLoc_InsertedSlicerFor", fields[0]) : UiText.Format("PivotLoc_InsertedSlicersCount", applied));
        return true;
    }

    /// <summary>
    /// Creates one timeline per selected field through the shared Pivot application session. Core rejects
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
            var outcome = PivotApplication.Execute(
                PivotApplication.PlanInsertTimeline(
                    new PivotApplicationTarget(_session.ActiveSheet, pivot),
                    name,
                    field));
            if (!outcome.Success)
            {
                error = outcome.Message?.Detail ?? UiText.Format("PivotLoc_CouldNotInsertTimeline", field);
                if (applied > 0)
                    RefreshShell(UiText.Format("PivotLoc_InsertedTimelinesCount", applied));
                return false;
            }

            applied++;
        }

        RefreshShell(applied == 1 ? UiText.Format("PivotLoc_InsertedTimelineFor", fields[0]) : UiText.Format("PivotLoc_InsertedTimelinesCount", applied));
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

}
