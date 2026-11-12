using System.Globalization;
using Free.Shared.Ribbon;
using FluentAssertions;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

public sealed class LegacyFormFieldInteractionPlannerTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static LegacyFormField Text(int? maxLength = null) =>
        LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { MaxLength = maxLength };

    private static RestrictEditingEnforcementPolicy Policy(ProtectionMode mode) =>
        RestrictEditingEnforcementPolicy.From(new ProtectionSettings(mode), isMarkedAsFinal: false);

    [Fact]
    public void Typing_ReplacesTheBlankPlaceholderWhole()
    {
        var edit = LegacyFormFieldInteractionPlanner.ApplyTypedText(Text(), LegacyFormField.BlankTextResult, 2, 0, "A");

        edit.Should().Be(new LegacyFormFieldTextEdit("A", 1));
    }

    [Fact]
    public void Typing_RespectsMaximumLength()
    {
        var field = Text(maxLength: 4);

        LegacyFormFieldInteractionPlanner.ApplyTypedText(field, "abc", 3, 0, "xyz")
            .Should().Be(new LegacyFormFieldTextEdit("abcx", 4));
        LegacyFormFieldInteractionPlanner.ApplyTypedText(field, "abcd", 4, 0, "e").Should().BeNull();
        LegacyFormFieldInteractionPlanner.ApplyTypedText(field, "abcd", 1, 2, "XY")
            .Should().Be(new LegacyFormFieldTextEdit("aXYd", 3));
    }

    [Fact]
    public void Deleting_TheLastCharacterRestoresThePlaceholder()
    {
        LegacyFormFieldInteractionPlanner.ApplyDeletion(Text(), "a", 1, 0, backward: true)
            .Should().Be(new LegacyFormFieldTextEdit(LegacyFormField.BlankTextResult, 0));
        LegacyFormFieldInteractionPlanner.ApplyDeletion(Text(), "abc", 0, 0, backward: true).Should().BeNull();
        LegacyFormFieldInteractionPlanner.ApplyDeletion(Text(), "abc", 0, 0, backward: false)
            .Should().Be(new LegacyFormFieldTextEdit("bc", 0));
        LegacyFormFieldInteractionPlanner.ApplyDeletion(Text(), LegacyFormField.BlankTextResult, 3, 0, backward: true)
            .Should().BeNull();
    }

    [Fact]
    public void Exit_AppliesNumberDateAndCaseFormats()
    {
        var number = Text() with { TextType = LegacyFormTextType.Number, TextFormat = "#,##0.00" };
        LegacyFormFieldInteractionPlanner.FormatOnExit(number, "1234.5", EnUs)
            .Should().Be(new LegacyFormFieldExitResult(true, "1,234.50"));
        LegacyFormFieldInteractionPlanner.FormatOnExit(number, "abc", EnUs).IsValid.Should().BeFalse();

        var date = Text() with { TextType = LegacyFormTextType.Date, TextFormat = "yyyy-MM-dd" };
        LegacyFormFieldInteractionPlanner.FormatOnExit(date, "3/7/2026", EnUs)
            .Should().Be(new LegacyFormFieldExitResult(true, "2026-03-07"));

        var upper = Text() with { TextFormat = LegacyFormFieldInteractionPlanner.UppercaseFormat };
        LegacyFormFieldInteractionPlanner.FormatOnExit(upper, "hello", EnUs).Text.Should().Be("HELLO");
        LegacyFormFieldInteractionPlanner.ApplyCaseFormat("hello big world", "TITLE CASE", EnUs)
            .Should().Be("Hello Big World");
        LegacyFormFieldInteractionPlanner.ApplyCaseFormat("hello", "FIRST CAPITAL", EnUs).Should().Be("Hello");

        LegacyFormFieldInteractionPlanner.FormatOnExit(number, LegacyFormField.BlankTextResult, EnUs).IsValid
            .Should().BeTrue("a blank field is never invalid");
    }

    [Fact]
    public void CheckBoxAndDropDown_ToggleAndPick()
    {
        var box = LegacyFormField.CreateDefault(LegacyFormFieldKind.CheckBox, "Check1");
        var toggled = LegacyFormFieldInteractionPlanner.ToggleCheckBox(box)!.Value;
        toggled.Field.IsChecked.Should().BeTrue();
        toggled.Text.Should().Be(ContentControl.CheckedGlyph);

        var list = LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown) with { DropDownEntries = ["A", "B"] };
        var picked = LegacyFormFieldInteractionPlanner.SelectDropDownEntry(list, 1)!.Value;
        picked.Text.Should().Be("B");
        picked.Field.DropDownResult.Should().Be(1);
        LegacyFormFieldInteractionPlanner.SelectDropDownEntry(list, 5).Should().BeNull();
        LegacyFormFieldInteractionPlanner.ToggleCheckBox(list).Should().BeNull();
    }

    [Fact]
    public void Filling_IsAllowedUnprotectedAndUnderFormsProtectionOnly()
    {
        var field = Text();
        LegacyFormFieldInteractionPlanner.CanFill(field, Policy(ProtectionMode.None)).Should().BeTrue();
        LegacyFormFieldInteractionPlanner.CanFill(field, Policy(ProtectionMode.FillingForms)).Should().BeTrue();
        LegacyFormFieldInteractionPlanner.CanFill(field, Policy(ProtectionMode.ReadOnly)).Should().BeFalse();
        LegacyFormFieldInteractionPlanner.CanFill(field with { Enabled = false }, Policy(ProtectionMode.FillingForms))
            .Should().BeFalse();
        LegacyFormFieldInteractionPlanner.CanFill(field with { Enabled = false }, Policy(ProtectionMode.None))
            .Should().BeTrue("Fill-in enabled only applies while the form is protected");
        LegacyFormFieldInteractionPlanner.IsTabStop(field, Policy(ProtectionMode.FillingForms)).Should().BeTrue();
        LegacyFormFieldInteractionPlanner.CanFill(
                field with { TextType = LegacyFormTextType.Calculated },
                Policy(ProtectionMode.FillingForms))
            .Should().BeFalse("Word does not let the user type into a calculated field");
    }

    [Fact]
    public void Insertion_UsesTheNextFreeWordName()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x") { ComplexField = new ComplexField(" FORMTEXT ", FormField: Text()) });
        document.Blocks.Add(paragraph);

        var (field, text) = LegacyFormFieldInteractionPlanner.CreateInsertion(LegacyFormFieldKind.Text, document);

        field.FormField!.Name.Should().Be("Text2");
        field.Keyword.Should().Be("FORMTEXT");
        text.Should().Be(LegacyFormField.BlankTextResult);
        LegacyFormFieldInteractionPlanner.CreateInsertion(LegacyFormFieldKind.DropDown, document).Field.FormField!.Name
            .Should().Be("Dropdown1");
    }

    [Fact]
    public void TextAfterOptions_FollowsANewDefaultOnlyWhileUnfilled()
    {
        var before = Text() with { DefaultText = "old" };
        var after = before with { DefaultText = "new" };

        LegacyFormFieldInteractionPlanner.TextAfterOptions(before, after, "old").Should().Be("new");
        LegacyFormFieldInteractionPlanner.TextAfterOptions(before, after, "typed").Should().Be("typed");
        LegacyFormFieldInteractionPlanner.TextAfterOptions(before, after with { MaxLength = 3 }, "typed")
            .Should().Be("typ");
    }

    [Fact]
    public void OptionsModel_ValidatesAndBuildsTheField()
    {
        var model = new LegacyFormFieldOptionsModel(Text())
        {
            MaxLengthText = "0",
        };
        model.TryBuild(out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();

        model.MaxLengthText = "5";
        model.DefaultText = "abcdefgh";
        model.TextType = LegacyFormTextType.Number;
        model.StatusText = "Enter an amount";
        model.TryBuild(out var built, out error).Should().BeTrue();
        error.Should().BeNull();
        built.MaxLength.Should().Be(5);
        built.DefaultText.Should().Be("abcde");
        built.TextType.Should().Be(LegacyFormTextType.Number);
        built.StatusText.Should().Be(new LegacyFormFieldHelpText(false, "Enter an amount"));

        model.Name = "a name with spaces";
        model.TryBuild(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void OptionsModel_EditsDropDownItemsAndCheckBoxSize()
    {
        var list = new LegacyFormFieldOptionsModel(
            LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown) with { DropDownEntries = ["One", "Two"], DropDownResult = 1 });
        list.TryAddItem("Three").Should().BeTrue();
        list.TryAddItem(new string('x', 51)).Should().BeFalse();
        list.MoveItem(1, -1).Should().BeTrue();
        list.TryBuild(out var built, out _).Should().BeTrue();
        built.DropDownEntries.Should().Equal("Two", "One", "Three");
        built.DisplayText(null).Should().Be("Two", "the chosen entry follows its move");

        var box = new LegacyFormFieldOptionsModel(LegacyFormField.CreateDefault(LegacyFormFieldKind.CheckBox))
        {
            CheckBoxAutoSize = false,
            CheckBoxSizeText = "12.5",
            DefaultChecked = true,
        };
        box.TryBuild(out var sized, out _).Should().BeTrue();
        sized.CheckBoxSizeHalfPoints.Should().Be(25);
        sized.IsChecked.Should().BeTrue();
        box.CheckBoxSizeText = "5000";
        box.TryBuild(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void RibbonWorkflow_RegistersEveryMenuCommandAndRoutesThem()
    {
        var registry = new RibbonCommandRegistry();
        var calls = new List<string>();
        LegacyFormFieldRibbonWorkflow.Register(registry, new LegacyFormFieldRibbonPorts(
            PrepareExecution: () => calls.Add("focus"),
            InsertFormField: kind => calls.Add("insert:" + kind),
            ShowFormFieldOptions: () => calls.Add("options"),
            ToggleFormFieldShading: () => calls.Add("shading"),
            ResetFormFields: () => calls.Add("reset")));

        var menuIds = LegacyFormFieldRibbonWorkflow.BuildMenu().Items
            .Where(item => item.CommandId is not null)
            .Select(item => item.CommandId!.Value.Value)
            .ToList();
        menuIds.Should().BeEquivalentTo(LegacyFormFieldRibbonWorkflow.CommandIds.Skip(1));

        foreach (var id in LegacyFormFieldRibbonWorkflow.CommandIds)
            registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue(id);

        registry.TryGet(new RibbonCommandId(LegacyFormFieldRibbonWorkflow.DropDownFieldCommandId), out var dropDown);
        dropDown!.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));
        calls.Should().Equal("focus", "insert:DropDown");
    }
}
