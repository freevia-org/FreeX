using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word 97-2003 ("legacy") form fields in the WPF editor: the grey field shading, fill-in by typing (with
/// the field's maximum length and blank placeholder), click / Space to toggle a check box, an in-place
/// list for a drop-down, Tab / Shift+Tab between fields with the field's format applied on exit while the
/// document is protected for "Filling in forms", the Options dialog, and Legacy Tools insert / reset.
/// Every edit is a model command (<see cref="SetLegacyFormFieldCommand"/>), so it is undoable and permitted
/// exactly when <see cref="LegacyFormFieldInteractionPlanner.CanFill"/> says so.
/// </summary>
public sealed partial class DocumentView
{
    /// <summary>Word's form-field shading: a light grey behind every form field.</summary>
    internal static readonly Color LegacyFormFieldShade = Color.FromRgb(0xD9, 0xD9, 0xD9);

    /// <summary>
    /// Developer &gt; Legacy Tools &gt; Form Field Shading. A view option (Word's is likewise screen-only and
    /// never printed); on by default as in Word.
    /// </summary>
    public static bool LegacyFormFieldShadingVisible { get; set; } = true;

    /// <summary>Paints a rendered form-field run: shading, tooltip, accessible name and its mouse gestures.</summary>
    private static void ApplyLegacyFormFieldChrome(WpfRun wpf, ComplexField field)
    {
        var formField = field.FormField!;
        if (LegacyFormFieldShadingVisible)
            wpf.Background = new SolidColorBrush(LegacyFormFieldShade);
        if (formField.Kind == LegacyFormFieldKind.CheckBox && formField.CheckBoxSizeHalfPoints is { } halfPoints && !formField.CheckBoxAutoSize)
            wpf.FontSize = halfPoints / 2.0 * PxPerPoint;

        var tooltip = LegacyFormFieldInteractionPlanner.Tooltip(formField);
        wpf.ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(wpf, tooltip);
        if (formField.HelpText is { IsAutoText: false, Value.Length: > 0 } help)
            System.Windows.Automation.AutomationProperties.SetHelpText(wpf, help.Value);

        switch (formField.Kind)
        {
            case LegacyFormFieldKind.CheckBox:
                wpf.Cursor = Cursors.Hand;
                wpf.MouseLeftButtonUp += OnLegacyCheckBoxClicked;
                break;
            case LegacyFormFieldKind.DropDown:
                wpf.Cursor = Cursors.Hand;
                wpf.MouseLeftButtonUp += OnLegacyDropDownClicked;
                break;
            default:
                wpf.MouseLeftButtonUp += OnLegacyTextFieldClicked;
                break;
        }

        wpf.MouseLeftButtonDown += OnLegacyFormFieldDoubleClicked;
    }

    /// <summary>
    /// The model formatting of a committed form-field run: the shading and an "Exactly" check box size are
    /// view chrome painted by <see cref="ApplyLegacyFormFieldChrome"/>, never real run formatting.
    /// </summary>
    private static RunFormatting WithoutLegacyFormFieldChrome(RunFormatting observed, RunFormatting modelFormatting) =>
        observed with
        {
            HighlightColorHex = modelFormatting.HighlightColorHex,
            FontSizePt = modelFormatting.FontSizePt,
        };

    /// <summary>
    /// Clears the form-field shading on every form field of <paramref name="document"/> and returns a scope
    /// that restores it. Print / Print Preview / PDF / XPS clone the page content inside this scope.
    /// </summary>
    internal static IDisposable SuspendLegacyFormFieldShading(FlowDocument document)
    {
        var suspended = new List<(WpfRun Run, Brush Background)>();
        foreach (var block in document.Blocks)
        {
            foreach (var run in DescendantRuns(block))
            {
                if (run.Tag is ComplexFieldMarker { Field.FormField: not null }
                    && run.Background is SolidColorBrush { Color: var color } brush
                    && color == LegacyFormFieldShade)
                {
                    suspended.Add((run, brush));
                    run.Background = null;
                }
            }
        }

        return new LegacyFormFieldShadingSuspension(suspended);
    }

    private sealed class LegacyFormFieldShadingSuspension(List<(WpfRun Run, Brush Background)> suspended) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (run, background) in suspended)
                run.Background = background;
            suspended.Clear();
        }
    }

    /// <summary>Whether <paramref name="run"/> is a form field Tab visits under "Filling in forms".</summary>
    private bool IsLegacyFormFieldTabStop(WpfRun run) =>
        run.Tag is ComplexFieldMarker { Field.FormField: { } formField }
        && LegacyFormFieldInteractionPlanner.IsTabStop(formField, RestrictEditingPolicy);

    /// <summary>
    /// The form-field run the selection sits in, with the selection's offsets inside it. False when the
    /// selection is not entirely inside one form field.
    /// </summary>
    private bool TryGetLegacyFormFieldSelection(out WpfRun run, out ComplexFieldMarker marker, out int start, out int length)
    {
        run = null!;
        marker = null!;
        start = length = 0;
        var selectionStart = Selection.IsEmpty ? CaretPosition : Selection.Start;
        var selectionEnd = Selection.IsEmpty ? CaretPosition : Selection.End;
        if (selectionStart is null || selectionEnd is null)
            return false;

        var candidate = FormFieldRunOf(selectionStart) ?? FormFieldRunOf(selectionEnd);
        if (candidate is null && Selection.IsEmpty && RestrictEditingPolicy.IsFormFieldEditingOnly)
        {
            // While only form fields are editable, a caret resting on a field's edge belongs to the field.
            candidate = AdjacentFormFieldRun(selectionStart, LogicalDirection.Forward)
                ?? AdjacentFormFieldRun(selectionStart, LogicalDirection.Backward);
        }

        if (candidate is not { Tag: ComplexFieldMarker found })
            return false;

        var startOffset = OffsetInRun(candidate, selectionStart);
        var endOffset = OffsetInRun(candidate, selectionEnd);
        if (startOffset < 0 || endOffset < startOffset)
            return false;

        run = candidate;
        marker = found;
        start = startOffset;
        length = endOffset - startOffset;
        return true;
    }

    private static WpfRun? FormFieldRunOf(TextPointer pointer) =>
        pointer.Parent is WpfRun { Tag: ComplexFieldMarker { Field.FormField: not null } } run ? run : null;

    /// <summary>
    /// The form-field run that starts (forward) or ends (backward) at <paramref name="pointer"/> with only
    /// inline element edges in between, never crossing a paragraph.
    /// </summary>
    private static WpfRun? AdjacentFormFieldRun(TextPointer pointer, LogicalDirection direction)
    {
        var current = pointer;
        for (var step = 0; step < 8 && current is not null; step++)
        {
            var context = current.GetPointerContext(direction);
            if (context is not (TextPointerContext.ElementStart or TextPointerContext.ElementEnd))
                return null;

            var element = current.GetAdjacentElement(direction);
            if (element is not Inline)
                return null;
            var entering = direction == LogicalDirection.Forward
                ? context == TextPointerContext.ElementStart
                : context == TextPointerContext.ElementEnd;
            if (entering && element is WpfRun { Tag: ComplexFieldMarker { Field.FormField: not null } } run)
                return run;

            current = current.GetNextContextPosition(direction);
        }

        return null;
    }

    /// <summary>
    /// <paramref name="pointer"/>'s offset in <paramref name="run"/>'s text; a pointer outside the run with no
    /// text between it and the run counts as the near edge. -1 when text separates them.
    /// </summary>
    private static int OffsetInRun(WpfRun run, TextPointer pointer)
    {
        if (pointer.CompareTo(run.ContentStart) < 0)
            return new TextRange(pointer, run.ContentStart).Text.Length == 0 ? 0 : -1;
        if (pointer.CompareTo(run.ContentEnd) > 0)
            return new TextRange(run.ContentEnd, pointer).Text.Length == 0 ? run.Text.Length : -1;
        return run.ContentStart.GetOffsetToPosition(pointer);
    }

    /// <summary>Typing inside a form field (see the class remarks). True when the keystroke was consumed.</summary>
    internal bool TryHandleLegacyFormFieldTextInput(string? text)
    {
        if (string.IsNullOrEmpty(text) || !TryGetLegacyFormFieldSelection(out var run, out var marker, out var start, out var length))
            return false;

        var formField = marker.Field.FormField!;
        if (!LegacyFormFieldInteractionPlanner.CanFill(formField, RestrictEditingPolicy))
            return true;

        switch (formField.Kind)
        {
            case LegacyFormFieldKind.CheckBox:
                if (text == " ")
                    ToggleLegacyCheckBox(marker.Field);
                return true;
            case LegacyFormFieldKind.DropDown:
                return true;
        }

        var edit = LegacyFormFieldInteractionPlanner.ApplyTypedText(formField, run.Text, start, length, text);
        if (edit is null)
            return true;
        ApplyLegacyFormFieldEdit(marker.Field, edit.Text, formField, edit.CaretOffset);
        return true;
    }

    /// <summary>
    /// Keys inside a form field: Backspace / Delete edit a text field and never delete a check box or
    /// drop-down; F4 / Alt+Down open a drop-down; under "Filling in forms" Tab / Shift+Tab move between
    /// fields, applying the field's format on the way out. True when the key was consumed.
    /// </summary>
    private bool TryHandleLegacyFormFieldKey(KeyEventArgs e, DocumentEditorInputPlan input)
    {
        var inField = TryGetLegacyFormFieldSelection(out var run, out var marker, out var start, out var length);
        if (input.Intent == DocumentEditorInputIntent.NavigateTab && RestrictEditingPolicy.IsFormFieldEditingOnly)
        {
            if (inField && !TryLeaveLegacyTextField(run, marker))
                return true;
            TabToContentControl(forward: !input.ExtendSelection);
            return true;
        }

        if (!inField)
            return false;

        var formField = marker.Field.FormField!;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (formField.Kind == LegacyFormFieldKind.DropDown
            && (key == Key.F4 || (key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)))
        {
            OpenLegacyDropDown(run, marker);
            return true;
        }

        if (Keyboard.Modifiers != ModifierKeys.None
            || input.Intent is not (DocumentEditorInputIntent.DeleteBackward or DocumentEditorInputIntent.DeleteForward))
        {
            return false;
        }

        return TryHandleLegacyFormFieldDeletion(backward: input.Intent == DocumentEditorInputIntent.DeleteBackward);
    }

    /// <summary>
    /// Backspace / Delete with the selection inside a form field: edits a text field (an emptied field
    /// shows its blank placeholder again) and never deletes a check box or drop-down. True when consumed.
    /// </summary>
    internal bool TryHandleLegacyFormFieldDeletion(bool backward)
    {
        if (!TryGetLegacyFormFieldSelection(out var run, out var marker, out var start, out var length))
            return false;

        var formField = marker.Field.FormField!;
        if (!LegacyFormFieldInteractionPlanner.CanFill(formField, RestrictEditingPolicy)
            || formField.Kind != LegacyFormFieldKind.Text)
        {
            return true;
        }

        var edit = LegacyFormFieldInteractionPlanner.ApplyDeletion(formField, run.Text, start, length, backward);
        if (edit is not null)
            ApplyLegacyFormFieldEdit(marker.Field, edit.Text, formField, edit.CaretOffset);
        return true;
    }

    /// <summary>
    /// Applies a text field's type / format as the user tabs out of it. Invalid input (a number field that
    /// holds no number, a date field that holds no date) is reported and keeps the user in the field, as
    /// Word does. Returns whether the user may leave.
    /// </summary>
    private bool TryLeaveLegacyTextField(WpfRun run, ComplexFieldMarker marker)
    {
        var formField = marker.Field.FormField!;
        if (formField.Kind != LegacyFormFieldKind.Text || !LegacyFormFieldInteractionPlanner.CanFill(formField, RestrictEditingPolicy))
            return true;

        var exit = LegacyFormFieldInteractionPlanner.FormatOnExit(formField, run.Text);
        if (!exit.IsValid)
        {
            var message = LegacyFormFieldOptionsModel.Text(formField.TextType == LegacyFormTextType.Number ? "Error_Number" : "Error_Date");
            MessageBox.Show(Window.GetWindow(this), message, LegacyFormFieldInteractionPlanner.Tooltip(formField), MessageBoxButton.OK, MessageBoxImage.Information);
            Selection.Select(run.ContentStart, run.ContentEnd);
            return false;
        }

        if (exit.Text != run.Text)
            ApplyLegacyFormFieldEdit(marker.Field, exit.Text, formField, caretOffset: exit.Text.Length);
        return true;
    }

    /// <summary>Executes one form-field edit through the command bus and puts the caret back inside the field.</summary>
    internal bool ApplyLegacyFormFieldEdit(ComplexField field, string text, LegacyFormField formField, int? caretOffset)
    {
        CommitToModel();
        var command = new SetLegacyFormFieldCommand(field, text, formField);
        _commands.Execute(command);
        if (command.AppliedField is not { } applied)
            return false;

        if (caretOffset is { } offset && FindLegacyFormFieldRun(applied) is { } rendered)
        {
            var clamped = Math.Clamp(offset, 0, rendered.Text.Length);
            CaretPosition = clamped == rendered.Text.Length
                ? rendered.ContentEnd
                : rendered.ContentStart.GetPositionAtOffset(clamped, LogicalDirection.Forward) ?? rendered.ContentStart;
            Focus();
        }

        return true;
    }

    /// <summary>The rendered run of the field whose model object is <paramref name="field"/>.</summary>
    internal WpfRun? FindLegacyFormFieldRun(ComplexField field)
    {
        foreach (var block in Document.Blocks)
        {
            foreach (var run in DescendantRuns(block))
            {
                if (run.Tag is ComplexFieldMarker marker && ReferenceEquals(marker.Field, field))
                    return run;
            }
        }

        return null;
    }

    internal bool ToggleLegacyCheckBox(ComplexField field)
    {
        if (!LegacyFormFieldInteractionPlanner.CanFill(field.FormField, RestrictEditingPolicy)
            || LegacyFormFieldInteractionPlanner.ToggleCheckBox(field.FormField!) is not { } toggled)
        {
            return false;
        }

        return ApplyLegacyFormFieldEdit(field, toggled.Text, toggled.Field, caretOffset: null);
    }

    internal bool SelectLegacyDropDownEntry(ComplexField field, int index)
    {
        if (!LegacyFormFieldInteractionPlanner.CanFill(field.FormField, RestrictEditingPolicy)
            || LegacyFormFieldInteractionPlanner.SelectDropDownEntry(field.FormField!, index) is not { } picked)
        {
            return false;
        }

        return ApplyLegacyFormFieldEdit(field, picked.Text, picked.Field, caretOffset: null);
    }

    private void OpenLegacyDropDown(WpfRun run, ComplexFieldMarker marker)
    {
        var formField = marker.Field.FormField!;
        if (!LegacyFormFieldInteractionPlanner.CanFill(formField, RestrictEditingPolicy) || formField.DropDownEntries.Count == 0)
            return;

        var menu = new ContextMenu();
        for (var index = 0; index < formField.DropDownEntries.Count; index++)
        {
            var entryIndex = index;
            var item = new MenuItem
            {
                Header = formField.DropDownEntries[index],
                IsCheckable = true,
                IsChecked = index == formField.SelectedIndex,
            };
            item.Click += (_, _) => SelectLegacyDropDownEntry(marker.Field, entryIndex);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
        var anchor = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        menu.HorizontalOffset = anchor.Left;
        menu.VerticalOffset = anchor.Bottom;
        menu.IsOpen = true;
    }

    private static void OnLegacyCheckBoxClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: ComplexFieldMarker marker } wpf || FindOwnerView(wpf) is not { } owner)
            return;
        e.Handled = true;
        owner.ToggleLegacyCheckBox(marker.Field);
    }

    private static void OnLegacyDropDownClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: ComplexFieldMarker marker } wpf || FindOwnerView(wpf) is not { } owner)
            return;
        e.Handled = true;
        owner.OpenLegacyDropDown(wpf, marker);
    }

    /// <summary>Clicking a blank text field selects its placeholder so the first keystroke replaces it.</summary>
    private static void OnLegacyTextFieldClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: ComplexFieldMarker } wpf
            || !LegacyFormField.IsBlankText(wpf.Text)
            || FindOwnerView(wpf) is not { } owner)
        {
            return;
        }

        e.Handled = true;
        owner.Selection.Select(wpf.ContentStart, wpf.ContentEnd);
    }

    /// <summary>Double-clicking a field in an unprotected document opens its Options dialog, as in Word.</summary>
    private static void OnLegacyFormFieldDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2
            || sender is not WpfRun { Tag: ComplexFieldMarker marker } wpf
            || FindOwnerView(wpf) is not { } owner
            || !owner.AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
        {
            return;
        }

        e.Handled = true;
        owner.ShowLegacyFormFieldOptions(marker.Field);
    }

    /// <summary>Developer &gt; Legacy Tools: inserts a Text / Check Box / Drop-Down form field at the caret.</summary>
    public void InsertLegacyFormField(LegacyFormFieldKind kind)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        CommitToModel();
        var (field, text) = LegacyFormFieldInteractionPlanner.CreateInsertion(kind, _model);
        InsertComplexField(field, text);
    }

    /// <summary>Developer &gt; Legacy Tools &gt; Form Field Options: the dialog for the field at the caret.</summary>
    public void ShowLegacyFormFieldOptionsAtCaret()
    {
        var run = ComplexFieldRunAtPointer(CaretPosition) ?? ComplexFieldRunAtPointer(Selection.Start);
        if (run?.Tag is ComplexFieldMarker { Field.FormField: not null } marker)
        {
            ShowLegacyFormFieldOptions(marker.Field);
            return;
        }

        MessageBox.Show(
            Window.GetWindow(this),
            LegacyFormFieldOptionsModel.Text("NoFieldAtCaret"),
            LegacyFormFieldOptionsModel.Text("Menu_Options").TrimEnd('.'),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    internal void ShowLegacyFormFieldOptions(ComplexField field)
    {
        if (field.FormField is not { } formField
            || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
        {
            return;
        }

        if (LegacyFormFieldOptionsDialog.Prompt(Window.GetWindow(this), formField) is not { } updated)
            return;

        var current = FindLegacyFormFieldRun(field)?.Text ?? formField.DefaultDisplayText;
        ApplyLegacyFormFieldEdit(field, LegacyFormFieldInteractionPlanner.TextAfterOptions(formField, updated, current), updated, caretOffset: null);
    }

    /// <summary>Developer &gt; Legacy Tools &gt; Form Field Shading.</summary>
    public void ToggleLegacyFormFieldShading()
    {
        CommitToModel();
        LegacyFormFieldShadingVisible = !LegacyFormFieldShadingVisible;
        Render();
    }

    /// <summary>Developer &gt; Legacy Tools &gt; Reset Form Fields: one undoable reset of every field.</summary>
    public void ResetLegacyFormFields()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.FormFieldEdit))
            return;

        CommitToModel();
        _commands.Execute(new ResetLegacyFormFieldsCommand());
    }
}
