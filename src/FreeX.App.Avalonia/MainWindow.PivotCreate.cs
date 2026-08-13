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
            Title = UiText.Get("PivotLoc_InsertPivotTableTitle"),
            Width = 500,
            Height = 460,
            MinWidth = 400,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertPivotTableDialog");

        var sourceBox = new TextBox
        {
            Text = createModel.SourceRangeText,
            MinWidth = 300,
        };
        ApplyDataOpsTextBoxChrome(sourceBox);
        AutomationProperties.SetAutomationId(sourceBox, "InsertPivotTableSourceRangeBox");
        AutomationProperties.SetName(sourceBox, UiText.Get("PivotTable_PivotTableSourceRange"));

        var sourcePicker = new Button { Content = "...", Width = 32, MinWidth = 32 };
        ApplyDataOpsButtonChrome(sourcePicker);
        AutomationProperties.SetAutomationId(sourcePicker, "InsertPivotTableSourceRangePickerButton");
        AutomationProperties.SetName(sourcePicker, UiText.Get("PivotTable_SelectPivotTableSourceRange"));

        var destinationBox = new TextBox
        {
            Text = createModel.DestinationRangeText,
            MinWidth = 300,
            IsEnabled = false,
        };
        ApplyDataOpsTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "InsertPivotTableDestinationRangeBox");
        AutomationProperties.SetName(destinationBox, UiText.Get("PivotTable_PivotTableLocation"));

        var destinationPicker = new Button { Content = "...", Width = 32, MinWidth = 32, IsEnabled = false };
        ApplyDataOpsButtonChrome(destinationPicker);
        AutomationProperties.SetAutomationId(destinationPicker, "InsertPivotTableDestinationRangePickerButton");
        AutomationProperties.SetName(destinationPicker, UiText.Get("PivotTable_SelectPivotTableLocation"));

        var roleBoxes = new Dictionary<int, ComboBox>();
        var fieldsPanel = new StackPanel { Spacing = 6 };
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
                ApplyDataOpsComboBoxChrome(roleBox);
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

        var newSheetBox = new CheckBox { Content = UiText.Get("PivotLoc_PlaceOnNewWorksheet"), IsChecked = true };
        ApplyDataOpsCheckBoxChrome(newSheetBox);
        AutomationProperties.SetAutomationId(newSheetBox, "PivotNewWorksheetBox");
        newSheetBox.IsCheckedChanged += (_, _) =>
        {
            var useExistingWorksheet = newSheetBox.IsChecked != true;
            destinationBox.IsEnabled = useExistingWorksheet;
            destinationPicker.IsEnabled = useExistingWorksheet;
        };

        var errorText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(errorText, "InsertPivotTableErrorText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "InsertPivotTableOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(cancelButton);
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
                    newSheetBox.IsChecked == true
                        ? PivotDestinationKind.NewWorksheet
                        : PivotDestinationKind.ExistingWorksheet,
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        static Grid BuildRangeRow(TextBox textBox, Button picker)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(textBox);
            Grid.SetColumn(picker, 1);
            picker.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(picker);
            return row;
        }

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
                            Text = StripDisplayMnemonic(UiText.Get("PivotTable_TableRangeLabel")),
                            Foreground = HeaderForeground,
                            FontSize = 12,
                            FontFamily = FormulaBarFontFamily,
                        },
                        BuildRangeRow(sourceBox, sourcePicker),
                        new ScrollViewer { Content = fieldsPanel, MaxHeight = 240 },
                        newSheetBox,
                        new TextBlock
                        {
                            Text = StripDisplayMnemonic(UiText.Get("PivotTable_LocationLabel")),
                            Foreground = HeaderForeground,
                            FontSize = 12,
                            FontFamily = FormulaBarFontFamily,
                        },
                        BuildRangeRow(destinationBox, destinationPicker),
                        errorText,
                    },
                },
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
