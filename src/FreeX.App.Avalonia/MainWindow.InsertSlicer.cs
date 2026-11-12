using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert ▸ Slicer / Insert ▸ Timeline for the active PivotTable. Both commands resolve the active (or first)
/// pivot on the active sheet and present the WPF dialog shape: an editable field combo (the pivot's source
/// fields, or a typed field name) and a caption box seeded with the default control name, then create the
/// slicer or timeline for that field under that caption through shared application plans.
/// The Core command behind each plan validates the field and the name (and that a
/// timeline field actually contains dates) and surfaces an error if not; the shell relays that message rather
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

        var fields = ReadInsertControlFields(pivot);
        if (fields.Count == 0)
        {
            RefreshShell(UiText.Get("PivotLoc_NoFieldsToSlice"));
            return;
        }

        await ShowInsertPivotControlDialogAsync(
            fields,
            new InsertPivotControlDialogText(
                TitleKey: "PivotSlicerTimeline_InsertSlicer",
                GroupKey: "PivotSlicerTimeline_ChooseFieldsGroup",
                FieldLabelKey: "PivotSlicerTimeline_FieldToConnectLabel",
                CaptionLabelKey: "PivotSlicerTimeline_SlicerCaptionLabel",
                DefaultNameKey: "PivotSlicerTimeline_DefaultSlicerName",
                MissingFieldKey: "PivotSlicerTimeline_SelectFieldToConnect",
                MissingCaptionKey: "PivotSlicerTimeline_EnterSlicerCaption"),
            automationPrefix: "InsertSlicer",
            (string fieldName, string caption, out string error) =>
                TryInsertSlicerControl(pivot, fieldName, caption, out error));
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

        var fields = ReadInsertControlFields(pivot);
        if (fields.Count == 0)
        {
            RefreshShell(UiText.Get("PivotLoc_NoFieldsForTimeline"));
            return;
        }

        await ShowInsertPivotControlDialogAsync(
            fields,
            new InsertPivotControlDialogText(
                TitleKey: "PivotSlicerTimeline_InsertTimeline",
                GroupKey: "PivotSlicerTimeline_ChooseDateFieldsGroup",
                FieldLabelKey: "PivotSlicerTimeline_DateFieldToConnectLabel",
                CaptionLabelKey: "PivotSlicerTimeline_TimelineCaptionLabel",
                DefaultNameKey: "PivotSlicerTimeline_DefaultTimelineName",
                MissingFieldKey: "PivotSlicerTimeline_SelectDateFieldToConnect",
                MissingCaptionKey: "PivotSlicerTimeline_EnterTimelineCaption"),
            automationPrefix: "InsertTimeline",
            (string fieldName, string caption, out string error) =>
                TryInsertTimelineControl(pivot, fieldName, caption, out error));
    }

    // The resource keys that differ between the slicer and the timeline flavour of the shared dialog.
    private sealed record InsertPivotControlDialogText(
        string TitleKey,
        string GroupKey,
        string FieldLabelKey,
        string CaptionLabelKey,
        string DefaultNameKey,
        string MissingFieldKey,
        string MissingCaptionKey);

    private delegate bool TryInsertPivotControl(string fieldName, string caption, out string error);

    // The non-blank source-field captions the field combo offers, in source order (the WPF field list).
    private IReadOnlyList<string> ReadInsertControlFields(PivotTableModel pivot) =>
        PivotApplication.ReadSourceHeaders(new PivotApplicationTarget(_session.ActiveSheet, pivot))
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToList();

    /// <summary>
    /// The WPF InsertSlicerDialog / InsertTimelineDialog shape: one captioned group box holding an editable
    /// field combo (first field preselected) above a caption box seeded with the default control name for
    /// that field, then the shared pivot button row. Accepting requires a non-blank field and caption (the
    /// WPF validation order and messages, shown inline with focus moved to the offending input) and keeps
    /// the dialog open when the Core command rejects the insert.
    /// </summary>
    private async Task ShowInsertPivotControlDialogAsync(
        IReadOnlyList<string> fields,
        InsertPivotControlDialogText text,
        string automationPrefix,
        TryInsertPivotControl tryInsert)
    {
        var initialField = fields[0];

        var dialog = new Window
        {
            // WPF: a NoResize window of PivotSlicerTimelineDialogContract size, so the app-wide dialog
            // sizing makes the contract height the minimum and lets the height follow the content.
            Title = UiText.Get(text.TitleKey),
            Width = PivotSlicerTimelineDialogContract.Width,
            MinWidth = PivotSlicerTimelineDialogContract.Width,
            MaxWidth = PivotSlicerTimelineDialogContract.Width,
            MinHeight = PivotSlicerTimelineDialogContract.Height - WpfNoResizeFrameHeight,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, automationPrefix + "Dialog");

        var fieldBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = fields,
            SelectedIndex = 0,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyPivotComboBoxChrome(fieldBox);
        fieldBox.Text = initialField;
        AutomationProperties.SetAutomationId(fieldBox, automationPrefix + "FieldList");
        AutomationProperties.SetName(fieldBox, UiText.CreateAutomationName(UiText.Get(text.FieldLabelKey)));

        var captionBox = new TextBox { Text = UiText.Format(text.DefaultNameKey, initialField) };
        ApplyPivotTextBoxChrome(captionBox);
        AutomationProperties.SetAutomationId(captionBox, automationPrefix + "CaptionBox");
        AutomationProperties.SetName(captionBox, UiText.CreateAutomationName(UiText.Get(text.CaptionLabelKey)));

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, automationPrefix + "WarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(okButton, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, automationPrefix + "OkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancelButton, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(cancelButton, automationPrefix + "CancelButton");

        // The editable combo hosts its own text box; keyboard focus belongs on that editor.
        void FocusFieldBox()
        {
            dialog.UpdateLayout();
            if (fieldBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } editor)
            {
                editor.Focus();
                editor.SelectAll();
            }
            else
            {
                fieldBox.Focus();
            }
        }

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var fieldName = (fieldBox.Text ?? string.Empty).Trim();
            var caption = (captionBox.Text ?? string.Empty).Trim();
            if (fieldName.Length == 0)
            {
                ShowWarning(UiText.Get(text.MissingFieldKey));
                FocusFieldBox();
                return;
            }

            if (caption.Length == 0)
            {
                ShowWarning(UiText.Get(text.MissingCaptionKey));
                captionBox.Focus();
                return;
            }

            if (!tryInsert(fieldName, caption, out var error))
            {
                ShowWarning(error);
                return;
            }

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = CreatePivotDialogButtonRow(okButton, cancelButton);

        // Layout mirrors the WPF dialog: a 16px root margin, one captioned group box (content inset
        // 10,8,10,10, 12px above the button row's own 12px top margin) with each label above its
        // control and an 8px row gap, then the shared pivot button row. The inline warning is
        // Avalonia-only and takes no room until it is shown.
        var groupContent = new StackPanel();
        AddPivotDialogLabeledControl(groupContent, UiText.Get(text.FieldLabelKey), fieldBox);
        AddPivotDialogLabeledControl(groupContent, UiText.Get(text.CaptionLabelKey), captionBox);
        groupContent.Children.Add(warningText);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                CreatePivotDialogGroupBox(UiText.Get(text.GroupKey), groupContent),
                buttonRow,
            },
        };
        dialog.Opened += (_, _) => FocusFieldBox();

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Creates the slicer for <paramref name="fieldName"/> under <paramref name="caption"/> through the shared
    /// Pivot application session, exactly as the WPF host does. Returns false (with a message) when the command
    /// rejects it so the dialog can stay open.
    /// </summary>
    private bool TryInsertSlicerControl(PivotTableModel pivot, string fieldName, string caption, out string error)
    {
        _slicerTimelinePaneDismissed = false;
        var outcome = PivotApplication.Execute(
            PivotApplication.PlanInsertSlicer(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                caption,
                fieldName));
        if (!outcome.Success)
        {
            error = outcome.Message?.Detail ?? UiText.Format("PivotLoc_CouldNotInsertSlicer", fieldName);
            return false;
        }

        error = string.Empty;
        RefreshShell(UiText.Format("PivotLoc_InsertedSlicerFor", fieldName));
        return true;
    }

    /// <summary>
    /// Creates the timeline for <paramref name="fieldName"/> under <paramref name="caption"/> through the
    /// shared Pivot application session. Core rejects fields that do not contain dates; that message is
    /// surfaced verbatim. Returns false on rejection so the dialog can stay open.
    /// </summary>
    private bool TryInsertTimelineControl(PivotTableModel pivot, string fieldName, string caption, out string error)
    {
        _slicerTimelinePaneDismissed = false;
        var outcome = PivotApplication.Execute(
            PivotApplication.PlanInsertTimeline(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                caption,
                fieldName));
        if (!outcome.Success)
        {
            error = outcome.Message?.Detail ?? UiText.Format("PivotLoc_CouldNotInsertTimeline", fieldName);
            return false;
        }

        error = string.Empty;
        RefreshShell(UiText.Format("PivotLoc_InsertedTimelineFor", fieldName));
        return true;
    }
}
