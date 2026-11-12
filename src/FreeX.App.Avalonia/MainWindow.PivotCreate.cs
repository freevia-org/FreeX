using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static IReadOnlyList<(PivotCreatePlanner.FieldRole Role, string Label)> PivotFieldRoleChoices =>
    [
        (PivotCreatePlanner.FieldRole.Unused, UiText.Get("PivotLoc_RoleUnused")),
        (PivotCreatePlanner.FieldRole.Row, UiText.Get("PivotLoc_RoleRow")),
        (PivotCreatePlanner.FieldRole.Value, UiText.Get("PivotLoc_RoleValue")),
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

        var createModel = PivotApplication.PrepareCreate(
            _session.ActiveSheet.Id,
            _session.SelectedRange);
        if (!createModel.CanShow || createModel.SourceRange is not { } source)
        {
            ShowPivotApplicationIssue(createModel.Message);
            return;
        }

        var fields = createModel.Fields;
        var defaults = createModel.DefaultRoles;

        var dialog = new Window
        {
            // WPF PivotTableDialog: Create PivotTable, a 500px NoResize window whose height follows the
            // content with a 320px outer minimum.
            Title = UiText.Get("PivotTable_CreatePivotTable"),
            Width = 500,
            MinWidth = 500,
            MaxWidth = 500,
            MinHeight = 320 - WpfNoResizeFrameHeight,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertPivotTableDialog");

        var sourceBox = new TextBox
        {
            Text = createModel.SourceRangeText,
        };
        ApplyPivotTextBoxChrome(sourceBox);
        AutomationProperties.SetAutomationId(sourceBox, "InsertPivotTableSourceRangeBox");
        AutomationProperties.SetName(sourceBox, UiText.Get("PivotTable_PivotTableSourceRange"));

        var sourcePicker = new Button { Content = "..." };
        AutomationProperties.SetAutomationId(sourcePicker, "InsertPivotTableSourceRangePickerButton");
        AutomationProperties.SetName(sourcePicker, UiText.Get("PivotTable_SelectPivotTableSourceRange"));

        var destinationBox = new TextBox
        {
            Text = createModel.DestinationRangeText,
            IsEnabled = false,
        };
        ApplyPivotTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "InsertPivotTableDestinationRangeBox");
        AutomationProperties.SetName(destinationBox, UiText.Get("PivotTable_PivotTableLocation"));

        var destinationPicker = new Button { Content = "...", IsEnabled = false };
        AutomationProperties.SetAutomationId(destinationPicker, "InsertPivotTableDestinationRangePickerButton");
        AutomationProperties.SetName(destinationPicker, UiText.Get("PivotTable_SelectPivotTableLocation"));

        var roleBoxes = new Dictionary<int, ComboBox>();
        var fieldsPanel = new StackPanel { Spacing = 4 };
        void ReloadFields(
            IReadOnlyList<PivotCreatePlanner.SourceField> sourceFields,
            IReadOnlyDictionary<int, PivotCreatePlanner.FieldRole> defaultRoles)
        {
            roleBoxes.Clear();
            fieldsPanel.Children.Clear();
            foreach (var field in sourceFields)
            {
                var roleBox = new ComboBox
                {
                    ItemsSource = PivotFieldRoleChoices.Select(c => c.Label).ToList(),
                    SelectedIndex = RoleIndex(defaultRoles.TryGetValue(field.Index, out var role) ? role : PivotCreatePlanner.FieldRole.Unused),
                    MinWidth = 130,
                };
                ApplyPivotComboBoxChrome(roleBox);
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
                            FontSize = 12,
                            FontFamily = FormulaBarFontFamily,
                        },
                        roleBox,
                    },
                });
            }
        }
        ReloadFields(fields, defaults);

        sourceBox.TextChanged += (_, _) =>
        {
            if (_session.TryResolveReferenceRange(sourceBox.Text, out var selectedSource)
                && selectedSource.Start.Sheet == _session.ActiveSheet.Id
                && PivotCreatePlanner.IsValidSource(selectedSource))
            {
                source = selectedSource;
                var selectedFields = PivotCreatePlanner.ReadFields(_session.ActiveSheet, source);
                ReloadFields(selectedFields, PivotCreatePlanner.DefaultRoles(selectedFields));
            }
        };

        // WPF offers the destination as two radio buttons (new worksheet is the default); only the
        // existing-worksheet choice enables the location editor.
        var selectTableRangeButton = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotTable_SelectATableOrRange")),
            GroupName = "PivotCreateSource",
            IsChecked = true,
        };
        ApplyPivotRadioButtonChrome(selectTableRangeButton);
        AutomationProperties.SetAutomationId(selectTableRangeButton, "PivotSelectTableOrRangeButton");
        var newSheetBox = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotTable_NewWorksheet")),
            GroupName = "PivotCreateDestination",
            IsChecked = true,
        };
        ApplyPivotRadioButtonChrome(newSheetBox);
        AutomationProperties.SetAutomationId(newSheetBox, "PivotNewWorksheetBox");
        var existingSheetBox = new RadioButton
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotTable_ExistingWorksheet")),
            GroupName = "PivotCreateDestination",
        };
        ApplyPivotRadioButtonChrome(existingSheetBox);
        AutomationProperties.SetAutomationId(existingSheetBox, "PivotExistingWorksheetBox");
        void UpdateDestinationState()
        {
            var useExistingWorksheet = existingSheetBox.IsChecked == true;
            destinationBox.IsEnabled = useExistingWorksheet;
            destinationPicker.IsEnabled = useExistingWorksheet;
        }
        newSheetBox.IsCheckedChanged += (_, _) => UpdateDestinationState();
        existingSheetBox.IsCheckedChanged += (_, _) => UpdateDestinationState();

        var errorText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(errorText, "InsertPivotTableErrorText");

        // WPF button row: Create (default) then Cancel, both 80 wide with an 8px gap.
        var okButton = new Button { Content = UiText.Get("PivotTable_Create"), IsDefault = true };
        ApplyPivotButtonChrome(okButton, 80, isDefault: true);
        okButton.Width = 80;
        AutomationProperties.SetAutomationId(okButton, "InsertPivotTableOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancelButton, 80);
        cancelButton.Width = 80;
        AutomationProperties.SetAutomationId(cancelButton, "InsertPivotTableCancelButton");

        okButton.Click += (_, _) =>
        {
            var roles = new Dictionary<int, PivotCreatePlanner.FieldRole>();
            foreach (var (index, box) in roleBoxes)
                roles[index] = PivotFieldRoleChoices[Math.Max(0, box.SelectedIndex)].Role;

            var plan = PivotApplication.PlanCreate(
                _session.ActiveSheet.Id,
                new PivotCreateSubmission(
                    sourceBox.Text,
                    existingSheetBox.IsChecked == true
                        ? PivotDestinationKind.ExistingWorksheet
                        : PivotDestinationKind.NewWorksheet,
                    destinationBox.Text,
                    OpenFieldList: true,
                    roles));
            if (!plan.CanApply)
            {
                errorText.Text = plan.Message?.Issue is
                    PivotApplicationIssue.InvalidDestinationReference or
                    PivotApplicationIssue.DestinationMustBeOnCurrentSheet
                        ? UiText.Get("PivotTable_EnterDestinationCellOnActiveWorksheet")
                        : plan.Message is { } issue
                            ? PivotApplicationIssueText(issue)
                            : UiText.Get("PivotLoc_InsertPivotTableFailed");
                errorText.IsVisible = true;
                var invalidDestination = plan.Message?.Issue is
                    PivotApplicationIssue.InvalidDestinationReference or
                    PivotApplicationIssue.DestinationMustBeOnCurrentSheet;
                var targetBox = invalidDestination ? destinationBox : sourceBox;
                targetBox.Focus();
                targetBox.SelectAll();
                return;
            }

            var outcome = PivotApplication.Execute(plan);
            if (!outcome.Success)
            {
                errorText.Text = outcome.Message?.Detail ?? UiText.Get("PivotLoc_InsertPivotTableFailed");
                errorText.IsVisible = true;
                return;
            }

            dialog.Close();
            ApplyPivotApplicationOutcome(outcome);
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // Layout mirrors PivotTableDialog: a 16px root margin; semi-bold section headers (6px below);
        // the source radio (6px below); labels and reference editors indented 22px under their radio
        // with a 4px label gap; the two destination radios 4px apart; the location editor 12px above
        // the trailing block; and the Create/Cancel row with no extra top margin. A WPF radio is a 16px
        // row where the compact Avalonia radio is 20px, so the radio gaps drop 4px to keep the pitch.
        TextBlock CreateSectionHeader(string text) => new()
        {
            Text = text,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };

        selectTableRangeButton.Margin = new Thickness(0, 0, 0, 2);
        errorText.Margin = new Thickness(0, 0, 0, 8);
        var buttonRow = CreatePivotDialogButtonRow(okButton, cancelButton, new Thickness(0));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                CreateSectionHeader(UiText.Get("PivotTable_ChooseDataHeader")),
                selectTableRangeButton,
                CreatePivotDialogLabel(UiText.Get("PivotTable_TableRangeLabel"), new Thickness(22, 0, 0, 4)),
                CreatePivotDialogReferenceEditor(sourcePicker, sourceBox, new Thickness(22, 0, 0, 8)),
                // Avalonia-only: the per-column Row/Value role list the macOS shell builds the pivot from.
                new ScrollViewer
                {
                    Content = fieldsPanel,
                    MaxHeight = 160,
                    Margin = new Thickness(22, 0, 0, 12),
                },
                CreateSectionHeader(UiText.Get("PivotTable_ChooseDestinationHeader")),
                newSheetBox,
                existingSheetBox,
                CreatePivotDialogLabel(UiText.Get("PivotTable_LocationLabel"), new Thickness(22, 0, 0, 4)),
                CreatePivotDialogReferenceEditor(destinationPicker, destinationBox, new Thickness(22, 0, 0, 16)),
                errorText,
                buttonRow,
            },
        };
        AttachDialogRangePicker(dialog, sourcePicker, sourceBox, "range.pivot-create.source");
        AttachDialogRangePicker(dialog, destinationPicker, destinationBox, "range.pivot-create.destination");
        ConfigurePivotDialogLifecycle(dialog, sourceBox, selectAllText: true);

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
