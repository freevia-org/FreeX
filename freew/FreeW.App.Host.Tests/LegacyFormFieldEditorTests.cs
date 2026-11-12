using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word 97-2003 form fields in the WPF editor: shading, fill-in through the model (max length, the
/// blank placeholder, check box toggle, drop-down pick), undo, "Filling in forms" Tab navigation with
/// the field's format applied on exit, and Legacy Tools insert / reset.
/// </summary>
public sealed class LegacyFormFieldEditorTests
{
    private static readonly MethodInfo OnPreviewKeyDownMethod =
        typeof(DocumentView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static (DocumentView View, ComplexField Text, ComplexField Box, ComplexField List) Load(
        LegacyFormField? textField = null)
    {
        var text = new ComplexField(" FORMTEXT ", FormField: textField ?? LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { MaxLength = 5 });
        var box = new ComplexField(" FORMCHECKBOX ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.CheckBox, "Check1"));
        var list = new ComplexField(" FORMDROPDOWN ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown, "Dropdown1") with { DropDownEntries = ["Red", "Green"] });

        var first = new Paragraph();
        first.Runs.Add(new Run("Name: "));
        first.Runs.Add(new Run(LegacyFormField.BlankTextResult) { ComplexField = text });
        var second = new Paragraph();
        second.Runs.Add(new Run("Agree "));
        second.Runs.Add(new Run(ContentControl.UncheckedGlyph) { ComplexField = box });
        second.Runs.Add(new Run(" colour "));
        second.Runs.Add(new Run("Red") { ComplexField = list });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(first);
        document.Blocks.Add(second);

        var view = new DocumentView();
        view.LoadModel(document);
        return (view, text, box, list);
    }

    private static ComplexField Current(DocumentView view, string keyword) =>
        LegacyFormField.Enumerate(view.Model).Single(item => item.Field.Keyword == keyword).Field;

    private static Run ModelRun(DocumentView view, string keyword) =>
        LegacyFormField.Enumerate(view.Model).Single(item => item.Field.Keyword == keyword).Run;

    private static void PlaceCaretIn(DocumentView view, ComplexField field, int offset)
    {
        var run = view.FindLegacyFormFieldRun(field) ?? throw new InvalidOperationException("field not rendered");
        var pointer = offset >= run.Text.Length ? run.ContentEnd : run.ContentStart.GetPositionAtOffset(offset)!;
        view.Selection.Select(pointer, pointer);
        view.CaretPosition = pointer;
    }

    private static void SelectWhole(DocumentView view, ComplexField field)
    {
        var run = view.FindLegacyFormFieldRun(field)!;
        view.Selection.Select(run.ContentStart, run.ContentEnd);
    }

    [StaFact]
    public void FormFields_RenderShadedOnScreen_NotIntoTheModel_AndNotWhenPrinting()
    {
        var (view, text, _, _) = Load();
        var run = view.FindLegacyFormFieldRun(text)!;

        run.Background.Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(DocumentView.LegacyFormFieldShade);

        view.CommitToModel();
        ModelRun(view, "FORMTEXT").Formatting?.HighlightColorHex.Should().BeNull(
            "the grey shading is view chrome and must never be saved as a highlight");

        using (DocumentView.SuspendLegacyFormFieldShading(view.Document))
            run.Background.Should().BeNull("print / PDF clone the page with the screen-only shading suspended");
        run.Background.Should().NotBeNull();
    }

    [StaFact]
    public void Typing_FillsTheFieldThroughTheModel_RespectingMaxLength_AndUndoes()
    {
        var (view, text, _, _) = Load();
        PlaceCaretIn(view, text, 2);

        view.TryHandleLegacyFormFieldTextInput("A").Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("A", "typing into the blank placeholder replaces it whole");

        foreach (var character in "bcdefg")
            view.TryHandleLegacyFormFieldTextInput(character.ToString()).Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("Abcde", "the field's maximum length is 5");

        view.TryHandleLegacyFormFieldDeletion(backward: true).Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("Abcd");

        view.Commands.Undo().Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("Abcde");
    }

    [StaFact]
    public void Typing_OutsideAField_IsLeftToTheNormalPaths()
    {
        var (view, _, _, _) = Load();
        view.CaretPosition = view.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);

        view.TryHandleLegacyFormFieldTextInput("x").Should().BeFalse();
    }

    [StaFact]
    public void CheckBoxAndDropDown_ToggleAndPick_UnderFillingForms()
    {
        var (view, _, box, list) = Load();
        view.SetProtection(ProtectionMode.FillingForms);

        SelectWhole(view, box);
        view.TryHandleLegacyFormFieldTextInput(" ").Should().BeTrue();
        ModelRun(view, "FORMCHECKBOX").ComplexField!.FormField!.IsChecked.Should().BeTrue();
        ModelRun(view, "FORMCHECKBOX").Text.Should().Be(ContentControl.CheckedGlyph);

        view.SelectLegacyDropDownEntry(Current(view, "FORMDROPDOWN"), 1).Should().BeTrue();
        ModelRun(view, "FORMDROPDOWN").Text.Should().Be("Green");
        ModelRun(view, "FORMDROPDOWN").ComplexField!.FormField!.DropDownResult.Should().Be(1);

        view.Commands.Undo().Should().BeTrue("form-field edits stay undoable under Filling in forms");
        ModelRun(view, "FORMDROPDOWN").Text.Should().Be("Red");
    }

    [StaFact]
    public void ReadOnlyProtection_RefusesEveryFill()
    {
        var (view, text, box, _) = Load();
        view.SetProtection(ProtectionMode.ReadOnly);

        view.ToggleLegacyCheckBox(Current(view, "FORMCHECKBOX")).Should().BeFalse();
        SelectWhole(view, Current(view, "FORMTEXT"));
        view.TryHandleLegacyFormFieldTextInput("x").Should().BeTrue("the keystroke is swallowed, not applied");
        ModelRun(view, "FORMTEXT").Text.Should().Be(LegacyFormField.BlankTextResult);
        _ = (text, box);
    }

    [StaFact]
    public void DisabledField_IsSkippedAndLockedOnlyWhileProtected()
    {
        var (view, _, _, _) = Load(LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { Enabled = false });
        SelectWhole(view, Current(view, "FORMTEXT"));
        view.TryHandleLegacyFormFieldTextInput("x").Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("x", "an unprotected form field is ordinary editable content");

        view.SetProtection(ProtectionMode.FillingForms);
        SelectWhole(view, Current(view, "FORMTEXT"));
        view.TryHandleLegacyFormFieldTextInput("y").Should().BeTrue();
        ModelRun(view, "FORMTEXT").Text.Should().Be("x", "Fill-in disabled applies while the form is protected");
    }

    [StaFact]
    public void Tab_UnderFillingForms_VisitsFormFields_AndAppliesTheFormatOnExit()
    {
        var (view, _, _, _) = Load(
            LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { TextFormat = "UPPERCASE" });
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -10000, Top = -10000,
            Width = 400, Height = 300, Content = view,
        };
        try
        {
            window.Show();
            view.SetProtection(ProtectionMode.FillingForms);
            PlaceCaretIn(view, Current(view, "FORMTEXT"), 0);
            foreach (var character in "abc")
                view.TryHandleLegacyFormFieldTextInput(character.ToString());

            PressTab(view).Should().BeTrue();

            ModelRun(view, "FORMTEXT").Text.Should().Be("ABC", "the UPPERCASE format applies when leaving the field");
            view.Selection.Text.Should().Be(ContentControl.UncheckedGlyph, "Tab moves to the next form field, the check box");

            PressTab(view).Should().BeTrue();
            view.Selection.Text.Should().Be("Red");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void LegacyTools_InsertAndReset()
    {
        var (view, _, _, _) = Load();
        view.CaretPosition = view.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);

        view.InsertLegacyFormField(LegacyFormFieldKind.Text);
        view.CommitToModel();
        LegacyFormField.Enumerate(view.Model).Select(item => item.FormField.Name)
            .Should().Contain("Text2", "the next free Word name is used");

        view.SelectLegacyDropDownEntry(Current(view, "FORMDROPDOWN"), 1);
        view.ToggleLegacyCheckBox(Current(view, "FORMCHECKBOX"));
        view.ResetLegacyFormFields();
        ModelRun(view, "FORMDROPDOWN").Text.Should().Be("Red");
        ModelRun(view, "FORMCHECKBOX").ComplexField!.FormField!.IsChecked.Should().BeFalse();
    }

    private static bool PressTab(DocumentView view)
    {
        var source = PresentationSource.FromVisual(view)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        OnPreviewKeyDownMethod.Invoke(view, [args]);
        return args.Handled;
    }
}
