using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-MAIL: modal dialogs for the Mailings tab — a recipient-list (CSV) editor and a merge-field-name
/// picker. Both are thin, dependency-free Avalonia windows that return their result (or <c>null</c> on
/// cancel) so the ribbon glue (<see cref="Ribbon.MailMergeEngine"/>) stays UI-agnostic and testable.
/// Send E-mail Messages planning is included here, but no messages are sent.
/// </summary>
internal static class MailMergeDialogs
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    /// <summary>
    /// Recipient-list dialog: a multi-line CSV editor (first line = column headers). When the document
    /// already has merge fields, <paramref name="seedHeader"/> pre-fills the header line as a hint. Returns
    /// the entered CSV text, or <c>null</c> if cancelled / empty.
    /// </summary>
    public static async Task<string?> AskRecipientCsvAsync(
        Window owner,
        string seedHeader,
        string? initialCsv = null)
    {
        var dialog = new Window
        {
            Title = MailMergeDialogMetadata.SelectRecipientsTitle,
            Width = 460,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };

        var hint = new TextBlock
        {
            Text = MailMergeDialogMetadata.RecipientCsvHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 6),
        };

        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Margin = new Thickness(16, 0, 16, 0),
            Text = initialCsv ??
                (string.IsNullOrWhiteSpace(seedHeader) ? string.Empty : seedHeader + "\n"),
            PlaceholderText ="FirstName,LastName,City…",
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(editor, DialogChromeStyle, fixedHeight: false);
        Grid.SetRow(editor, 1);

        string? result = null;

        var ok = new Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            var text = editor.Text ?? string.Empty;
            result = string.IsNullOrWhiteSpace(text) ? null : text;
            dialog.Close();
        };
        var cancel = new Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 10, 16, 14));

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var hintHost = hint; Grid.SetRow(hintHost, 0);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(hintHost);
        grid.Children.Add(editor);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Merge-field picker: an editable combo seeded with the available <paramref name="fieldNames"/> (the
    /// loaded recipient list's columns). The user can pick one or type a new name. Returns the chosen name,
    /// or <c>null</c> if cancelled / blank.
    /// </summary>
    public static async Task<string?> AskMergeFieldNameAsync(Window owner, IReadOnlyList<string> fieldNames)
    {
        var dialog = new Window
        {
            Title = MailMergeDialogMetadata.InsertMergeFieldTitle,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var label = new TextBlock
        {
            Text = MailMergeDialogMetadata.FieldNameLabel,
            Margin = new Thickness(16, 16, 16, 4),
        };
        Grid.SetRow(label, 0);

        var combo = new ComboBox
        {
            ItemsSource = fieldNames,
            Margin = new Thickness(16, 0, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = fieldNames.Count > 0 ? 0 : -1,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        Grid.SetRow(combo, 1);

        // Also allow free text entry for a field not in the loaded list.
        var freeText = new TextBox
        {
            PlaceholderText ="…or type a field name",
            Margin = new Thickness(16, 8, 16, 0),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(freeText, DialogChromeStyle);
        Grid.SetRow(freeText, 2);

        string? result = null;

        var ok = new Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) =>
        {
            var typed = freeText.Text?.Trim();
            result = !string.IsNullOrWhiteSpace(typed)
                ? typed
                : combo.SelectedItem as string;
            dialog.Close();
        };
        var cancel = new Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));
        Grid.SetRow(buttons, 3);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        grid.Children.Add(label);
        grid.Children.Add(combo);
        grid.Children.Add(freeText);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeStartType?> AskStartMailMergeAsync(
        Window owner,
        MailMergeStartType selectedType = MailMergeStartType.Letters)
    {
        var dialog = CreateDialog(MailMergeDialogMetadata.StartMailMergeTitle, 330, 175);
        var plan = MailMergeStartDialogPlanner.GetChoices();
        var typeCombo = CreateChoiceCombo(
            plan.Select(choice => choice.Label),
            MailMergeStartDialogPlanner.GetSelectedIndex(selectedType));
        MailMergeStartType? result = null;
        var content = CreateForm((MailMergeDialogMetadata.DocumentTypeLabel, (Control)typeCombo));
        AddActions(dialog, content, () => result = MailMergeStartDialogPlanner.GetType(typeCombo.SelectedIndex));
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<FieldMapping?> AskMatchFieldsAsync(
        Window owner,
        IReadOnlyList<string> header,
        FieldMapping current)
    {
        var dialog = CreateDialog(MailMergeDialogMetadata.MatchFieldsTitle, 455, 520);
        var dialogPlan = MailMergeMatchFieldsDialogPlanner.GetRolePlans(header, current);
        var columnChoices = MailMergeMatchFieldsDialogPlanner.GetColumnChoices(header);
        var combos = new Dictionary<FieldRole, ComboBox>();
        foreach (var rolePlan in dialogPlan)
        {
            var combo = CreateChoiceCombo(columnChoices, Array.IndexOf(columnChoices.ToArray(), rolePlan.SelectedChoice));
            combos[rolePlan.Role] = combo;
        }

        var rows = dialogPlan
            .Select(rolePlan => (rolePlan.Label, (Control)combos[rolePlan.Role]))
            .ToArray();
        FieldMapping? result = null;
        var content = CreateForm(rows);
        AddActions(dialog, content, () =>
        {
            result = MailMergeMatchFieldsDialogPlanner.CreateResult(
                combos.ToDictionary(pair => pair.Key, pair => pair.Value.SelectedItem as string));
        });
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MergeData?> AskFilterSortRecipientsAsync(Window owner, MergeData data)
    {
        var dialog = CreateDialog(MailMergeDialogMetadata.FilterSortRecipientsTitle, 560, 470);
        var plan = MailMergeFilterSortDialogPlanner.CreatePlan(data);
        var sortCombo = CreateChoiceCombo(plan.SortColumns, 0);
        var ascending = new RadioButton { Content = MailMergeDialogMetadata.AscendingLabel, IsChecked = true, GroupName = "sort" };
        var descending = new RadioButton { Content = MailMergeDialogMetadata.DescendingLabel, GroupName = "sort" };
        var sortRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = MailMergeDialogMetadata.SortByLabel, VerticalAlignment = VerticalAlignment.Center },
                sortCombo,
                ascending,
                descending,
            },
        };

        var rowChecks = new List<CheckBox>();
        var rowList = new StackPanel { Spacing = 2 };
        rowList.Children.Add(new TextBlock { Text = plan.PreviewHeader, Foreground = Brushes.Gray });
        for (var i = 0; i < plan.PreviewRows.Count; i++)
        {
            var check = new CheckBox { Content = plan.PreviewRows[i], IsChecked = true };
            rowChecks.Add(check);
            rowList.Children.Add(check);
        }

        var scroll = new ScrollViewer
        {
            Content = rowList,
            MaxHeight = 285,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 16, 16, 0),
            Children =
            {
                new TextBlock { Text = MailMergeDialogMetadata.FilterInstruction },
                sortRow,
                scroll,
            },
        };
        MergeData? result = null;
        AddActions(dialog, content, () =>
        {
            var indexes = rowChecks
                .Select((check, index) => (check, index))
                .Where(item => item.check.IsChecked == true)
                .Select(item => item.index);
            result = MailMergeFilterSortDialogPlanner.Apply(data, indexes, sortCombo.SelectedItem as string, ascending.IsChecked == true);
        });
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<EnvelopeSetupResult?> AskEnvelopeAsync(Window owner)
    {
        var plan = MailingsEnvelopeLabelPlanner.CreateEnvelopeDialogPlan();
        var dialog = CreateDialog(MailMergeDialogMetadata.EnvelopesTitle, 365, 230);
        var combo = CreateChoiceCombo(plan.Sizes.Select(size => size.Name), plan.SelectedIndex);
        var note = new TextBlock
        {
            Text = plan.Note,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        };
        EnvelopeSetupResult? result = null;
        var content = CreateForm(
            (MailMergeDialogMetadata.EnvelopeSizeLabel, (Control)combo),
            (MailMergeDialogMetadata.NoteLabel, note));
        AddActions(dialog, content, () => result = MailingsEnvelopeLabelPlanner.PlanEnvelope(combo.SelectedIndex));
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<LabelSetupResult?> AskLabelsAsync(Window owner)
    {
        var plan = MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan();
        var dialog = CreateDialog(MailMergeDialogMetadata.LabelsTitle, 400, 270);
        var combo = CreateChoiceCombo(plan.Presets.Select(preset => preset.Name), plan.SelectedIndex);
        var rowsBox = CreateTextBox(plan.CustomRowsText, "Rows");
        var columnsBox = CreateTextBox(plan.CustomColumnsText, "Columns");
        var customPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            IsVisible = plan.ShowCustomGrid,
            Children =
            {
                new TextBlock { Text = MailMergeDialogMetadata.RowsLabel, VerticalAlignment = VerticalAlignment.Center }, rowsBox,
                new TextBlock { Text = MailMergeDialogMetadata.ColumnsLabel, VerticalAlignment = VerticalAlignment.Center }, columnsBox,
            },
        };
        combo.SelectionChanged += (_, _) =>
        {
            var selected = MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan(combo.SelectedIndex, rowsBox.Text, columnsBox.Text);
            customPanel.IsVisible = selected.ShowCustomGrid;
        };
        LabelSetupResult? result = null;
        var content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 16, 16, 0),
            Children =
            {
                new TextBlock { Text = MailMergeDialogMetadata.LabelProductLabel }, combo, customPanel,
            },
        };
        AddActions(dialog, content, () =>
        {
            var selected = MailingsEnvelopeLabelPlanner.PlanLabel(combo.SelectedIndex, rowsBox.Text, columnsBox.Text);
            result = selected.Result;
        });
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergePreviewDialogAction?> AskPreviewNavigationAsync(
        Window owner,
        int currentIndex,
        int recordCount)
    {
        var plan = MailMergePreviewDialogPlanner.CreatePlan(currentIndex, recordCount);
        var dialog = CreateDialog(MailMergeDialogMetadata.PreviewResultsTitle, 350, 170);
        var label = new TextBlock { Text = plan.RecordLabel, Margin = new Thickness(16, 16, 16, 8) };
        var previous = new Button { Content = MailMergeDialogMetadata.PreviousLabel, IsEnabled = plan.CanGoPrevious };
        var next = new Button { Content = MailMergeDialogMetadata.NextLabel, IsEnabled = plan.CanGoNext };
        var done = new Button { Content = MailMergeDialogMetadata.DoneLabel, IsDefault = true };
        var cancel = new Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true };
        foreach (var button in new[] { previous, next, done, cancel })
            AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 72, isDefault: button == done);

        MailMergePreviewDialogAction? result = null;
        previous.Click += (_, _) => { result = MailMergePreviewDialogAction.MovePrevious; dialog.Close(); };
        next.Click += (_, _) => { result = MailMergePreviewDialogAction.MoveNext; dialog.Close(); };
        done.Click += (_, _) => { result = MailMergePreviewDialogAction.Done; dialog.Close(); };
        cancel.Click += (_, _) => { result = MailMergePreviewDialogAction.Cancel; dialog.Close(); };
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [previous, next, done, cancel], new Thickness(16, 8, 16, 14));
        var content = new StackPanel { Children = { label, buttons } };
        dialog.Content = content;
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> AskFindRecipientAsync(
        Window owner,
        string? initialQuery = null)
    {
        var dialog = CreateDialog(MailMergeDialogMetadata.FindRecipientTitle, 360, 155);
        var queryBox = CreateTextBox(initialQuery ?? string.Empty, MailMergeDialogMetadata.FindPlaceholder);
        string? result = null;
        var content = CreateForm((MailMergeDialogMetadata.FindLabel, queryBox));
        AddActions(dialog, content, () =>
        {
            var value = queryBox.Text?.Trim();
            result = string.IsNullOrWhiteSpace(value) ? null : value;
        });
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeFinishPlan?> AskFinishMergeAsync(
        Window owner,
        int recordCount,
        int currentRecordIndex)
    {
        var dialogPlan = MailMergeFinishPlanner.CreateDialogPlan(recordCount, currentRecordIndex);
        var dialog = CreateDialog(MailMergeDialogMetadata.FinishAndMergeTitle, 430, 275);
        var destination = CreateChoiceCombo(dialogPlan.Destinations.Select(choice => choice.Label), dialogPlan.DestinationIndex);
        var scope = CreateChoiceCombo(dialogPlan.Scopes.Select(choice => choice.Label), dialogPlan.ScopeIndex);
        var from = CreateTextBox(dialogPlan.FromRecordText, "From record");
        var to = CreateTextBox(dialogPlan.ToRecordText, "To record");
        var range = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = MailMergeDialogMetadata.FromLabel, VerticalAlignment = VerticalAlignment.Center }, from,
                new TextBlock { Text = MailMergeDialogMetadata.ToLabel, VerticalAlignment = VerticalAlignment.Center }, to,
            },
        };
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        Button? okButton = null;

        MailMergeFinishPlan CurrentPlan() => MailMergeFinishPlanner.Plan(
            dialogPlan.Destinations[Math.Clamp(destination.SelectedIndex, 0, dialogPlan.Destinations.Count - 1)].Destination,
            dialogPlan.Scopes[Math.Clamp(scope.SelectedIndex, 0, dialogPlan.Scopes.Count - 1)].Scope,
            recordCount,
            currentRecordIndex,
            from.Text,
            to.Text);

        void Refresh()
        {
            var current = CurrentPlan();
            validation.Text = current.Success
                ? MailMergeDialogMetadata.ReadyToFinishMessage
                : MailMergeDialogMetadata.FormatFinishIssue(current.Issue);
            if (okButton is not null)
                okButton.IsEnabled = current.Success;
        }

        destination.SelectionChanged += (_, _) => Refresh();
        scope.SelectionChanged += (_, _) => Refresh();
        from.TextChanged += (_, _) => Refresh();
        to.TextChanged += (_, _) => Refresh();
        var content = CreateForm(
            (MailMergeDialogMetadata.DestinationLabel, (Control)destination),
            (MailMergeDialogMetadata.RecordsLabel, (Control)scope),
            (MailMergeDialogMetadata.RangeLabel, range),
            (MailMergeDialogMetadata.ValidationLabel, validation));
        MailMergeFinishPlan? result = null;
        AddActions(dialog, content, () => result = CurrentPlan(), ok => okButton = ok);
        Refresh();
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeCheckForErrorsMode?> AskCheckForErrorsAsync(Window owner)
    {
        var dialog = CreateDialog(MailMergeDialogMetadata.CheckForErrorsTitle, 520, 220);
        var choices = MailMergeCheckForErrorsPlanner.GetChoices();
        var combo = CreateChoiceCombo(choices.Select(choice => choice.Label), 0);
        MailMergeCheckForErrorsMode? result = null;
        var content = CreateForm((MailMergeDialogMetadata.CheckForErrorsLabel, (Control)combo));
        AddActions(dialog, content, () => result = MailMergeCheckForErrorsPlanner.GetMode(combo.SelectedIndex));
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeEmailDeliveryIntent?> AskEmailMergeDeliveryAsync(
        Window owner,
        MergeData data,
        int currentRecordIndex,
        IReadOnlyList<int> selectedRecordIndexes)
    {
        var dialogPlan = MailMergeEmailDeliveryPlanner.CreateDialogPlan(data, currentRecordIndex, selectedRecordIndexes);
        var dialog = CreateDialog(MailMergeDialogMetadata.SendEmailTitle, 430, 315);

        var fieldCombo = new ComboBox
        {
            ItemsSource = dialogPlan.RecipientAddressFields,
            SelectedItem = dialogPlan.RecipientAddressField,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(fieldCombo, DialogChromeStyle);

        var subjectBox = CreateTextBox(dialogPlan.Subject, "Subject line");
        var outputCombo = CreateChoiceCombo(dialogPlan.OutputFormats.Select(choice => choice.Label), dialogPlan.OutputFormatIndex);
        var bodyCombo = CreateChoiceCombo(dialogPlan.BodyFormats.Select(choice => choice.Label), dialogPlan.BodyFormatIndex);
        var scopeCombo = CreateChoiceCombo(dialogPlan.RecordScopes.Select(choice => choice.Label), dialogPlan.RecordScopeIndex);
        var validation = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 8),
        };

        MailMergeEmailDeliveryIntent? result = null;
        Button? okButton = null;

        MailMergeEmailDeliveryIntent CurrentIntent() =>
            MailMergeEmailDeliveryPlanner.CreateIntent(
                fieldCombo.SelectedItem as string ?? dialogPlan.RecipientAddressField,
                subjectBox.Text,
                outputCombo.SelectedIndex,
                bodyCombo.SelectedIndex,
                scopeCombo.SelectedIndex,
                currentRecordIndex,
                selectedRecordIndexes);

        void RefreshValidation()
        {
            var plan = MailMerge.CreateEmailDeliveryPlan(data, CurrentIntent());
            var messages = MailMergeEmailDeliveryPlanner.GetValidationMessages(plan);
            validation.Text = messages.Count == 0
                ? MailMergeDialogMetadata.ReadyEmailMessage
                : string.Join(Environment.NewLine, messages);
            if (okButton is not null)
                okButton.IsEnabled = plan.Errors.Count == 0;
        }

        fieldCombo.SelectionChanged += (_, _) => RefreshValidation();
        subjectBox.TextChanged += (_, _) => RefreshValidation();
        outputCombo.SelectionChanged += (_, _) => RefreshValidation();
        bodyCombo.SelectionChanged += (_, _) => RefreshValidation();
        scopeCombo.SelectionChanged += (_, _) => RefreshValidation();

        var content = CreateForm(
            (MailMergeDialogMetadata.ToFieldLabel, (Control)fieldCombo),
            (MailMergeDialogMetadata.SubjectLabel, subjectBox),
            (MailMergeDialogMetadata.OutputLabel, outputCombo),
            (MailMergeDialogMetadata.BodyFormatLabel, bodyCombo),
            (MailMergeDialogMetadata.SendRecordsLabel, scopeCombo),
            (MailMergeDialogMetadata.ValidationLabel, validation));

        AddActions(dialog, content, () =>
        {
            result = CurrentIntent();
        }, ok => okButton = ok);
        RefreshValidation();

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeRuleIfDialogResult?> AskMergeRuleIfAsync(
        Window owner,
        IReadOnlyList<string> fieldNames)
    {
        var session = new MailMergeRuleConditionDialogSession(fieldNames);
        var dialog = CreateDialog(MailMergeDialogMetadata.IfThenElseTitle, 380, 300);
        var fieldBox = CreateTextBox(session.InitialFieldName, "Field name");
        var opCombo = CreateOperatorCombo(session.ConditionOperators);
        var valueBox = CreateTextBox(string.Empty, "Comparison value");
        var trueBox = CreateTextBox(string.Empty, "Text if true");
        var falseBox = CreateTextBox(string.Empty, "Text if false");

        void RefreshValueEnabled()
        {
            session.SelectOperator(opCombo.SelectedIndex);
            valueBox.IsEnabled = session.IsComparisonValueEnabled;
        }

        opCombo.SelectionChanged += (_, _) => RefreshValueEnabled();
        RefreshValueEnabled();

        MailMergeRuleIfDialogResult? result = null;
        var content = CreateForm(
            (MailMergeDialogMetadata.FieldNameLabel, (Control)fieldBox),
            (MailMergeDialogMetadata.ComparisonLabel, opCombo),
            (MailMergeDialogMetadata.CompareToLabel, valueBox),
            (MailMergeDialogMetadata.ThenInsertLabel, trueBox),
            (MailMergeDialogMetadata.OtherwiseInsertLabel, falseBox));
        AddActions(dialog, content, () =>
        {
            result = session.AcceptIf(
                fieldBox.Text,
                valueBox.Text,
                trueBox.Text,
                falseBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeRuleConditionDialogResult?> AskMergeRuleConditionAsync(
        Window owner,
        IReadOnlyList<string> fieldNames,
        string title)
    {
        var session = new MailMergeRuleConditionDialogSession(fieldNames);
        var dialog = CreateDialog(title, 360, 230);
        var fieldBox = CreateTextBox(session.InitialFieldName, "Field name");
        var opCombo = CreateOperatorCombo(session.ConditionOperators);
        var valueBox = CreateTextBox(string.Empty, "Comparison value");

        void RefreshValueEnabled()
        {
            session.SelectOperator(opCombo.SelectedIndex);
            valueBox.IsEnabled = session.IsComparisonValueEnabled;
        }

        opCombo.SelectionChanged += (_, _) => RefreshValueEnabled();
        RefreshValueEnabled();

        MailMergeRuleConditionDialogResult? result = null;
        var content = CreateForm(
            (MailMergeDialogMetadata.FieldNameLabel, (Control)fieldBox),
            (MailMergeDialogMetadata.ComparisonLabel, opCombo),
            (MailMergeDialogMetadata.CompareToLabel, valueBox));
        AddActions(dialog, content, () =>
        {
            result = session.AcceptCondition(
                fieldBox.Text,
                valueBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> AskMergeRulePromptAsync(
        Window owner,
        string title,
        string prompt,
        string initialValue = "")
    {
        var dialog = CreateDialog(title, 340, 165);
        var valueBox = CreateTextBox(initialValue, prompt);
        string? result = null;
        var content = CreateForm((prompt, (Control)valueBox));
        AddActions(dialog, content, () =>
        {
            result = valueBox.Text?.Trim() ?? string.Empty;
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<MailMergeRuleNameValueDialogResult?> AskMergeRuleNameValueAsync(
        Window owner,
        string title,
        string valueLabel)
    {
        var session = new MailMergeRuleNameValueDialogSession();
        var dialog = CreateDialog(title, 360, 210);
        var nameBox = CreateTextBox(string.Empty, "Bookmark name");
        var valueBox = CreateTextBox(string.Empty, valueLabel);
        MailMergeRuleNameValueDialogResult? result = null;
        var content = CreateForm(
            (MailMergeDialogMetadata.BookmarkNameLabel, (Control)nameBox),
            (valueLabel, (Control)valueBox));
        AddActions(dialog, content, () =>
        {
            result = session.Accept(nameBox.Text, valueBox.Text);
        });

        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window CreateDialog(string title, double width, double height) =>
        new()
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

    private static TextBox CreateTextBox(string text, string placeholder)
    {
        var box = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static ComboBox CreateOperatorCombo(
        IReadOnlyList<MailMergeConditionOperatorChoice> choices)
    {
        var combo = new ComboBox
        {
            ItemsSource = choices
                .Select(choice => choice.Label)
                .ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static ComboBox CreateChoiceCombo(IEnumerable<string> labels, int selectedIndex)
    {
        var items = labels.ToArray();
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = items.Length == 0 ? -1 : Math.Clamp(selectedIndex, 0, items.Length - 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static Grid CreateForm(params (string Label, Control Control)[] rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
            RowDefinitions = new RowDefinitions(string.Join(",", rows.Select(_ => "Auto"))),
            Margin = new Thickness(16, 16, 16, 0),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var label = new TextBlock
            {
                Text = rows[i].Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
            };
            var control = rows[i].Control;
            control.Margin = new Thickness(0, 0, 0, 8);

            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            Grid.SetRow(control, i);
            Grid.SetColumn(control, 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }

        return grid;
    }

    private static void AddActions(
        Window dialog,
        Control content,
        Action onOk,
        Action<Button>? configureOk = null)
    {
        var ok = new Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        configureOk?.Invoke(ok);
        ok.Click += (_, _) =>
        {
            onOk();
            dialog.Close();
        };
        var cancel = new Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => dialog.Close();

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 10, 16, 14));
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(content, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(content);
        grid.Children.Add(buttons);
        dialog.Content = grid;
    }
}
