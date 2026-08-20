using System.Globalization;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ContentControlInteractionPlannerTests
{
    [Fact]
    public void PromptText_UsesDefaultPromptWhenSelectionIsEmpty()
    {
        ContentControlInteractionPlanner.PromptText(null).Should().Be("Click to enter text");
        ContentControlInteractionPlanner.PromptText(string.Empty).Should().Be("Click to enter text");
        ContentControlInteractionPlanner.PromptText("Selected").Should().Be("Selected");
    }

    [Fact]
    public void ListItemsOrDefault_ReturnsSharedDefaultItems()
    {
        var items = ContentControlInteractionPlanner.ListItemsOrDefault(null);
        var emptyItems = ContentControlInteractionPlanner.ListItemsOrDefault([]);
        var customItems = new[] { new ContentControlListItem("Custom", "C") };

        items.Select(item => item.DisplayText).Should().Equal(
            "Choose an item",
            "Item 1",
            "Item 2",
            "Item 3");
        items.Select(item => item.Value).Should().Equal(
            "Choose an item",
            "Item 1",
            "Item 2",
            "Item 3");
        emptyItems.Should().BeSameAs(ContentControlInteractionPlanner.DefaultListItems);
        ContentControlInteractionPlanner.ListItemsOrDefault(customItems).Should().BeSameAs(customItems);
    }

    [Fact]
    public void DateFormatting_UsesSharedDefaultOrRequestedFormat()
    {
        var date = new DateTime(2026, 8, 11);

        ContentControlInteractionPlanner.DateFormatOrDefault(null)
            .Should().Be(ContentControl.DefaultDateFormat);
        ContentControlInteractionPlanner.DateFormatOrDefault(string.Empty)
            .Should().Be(ContentControl.DefaultDateFormat);
        ContentControlInteractionPlanner.FormatDate("yyyy-MM-dd", date, CultureInfo.InvariantCulture)
            .Should().Be("2026-08-11");
    }

    [Fact]
    public void Tooltip_MatchesExistingWpfCopyAndAliasBehavior()
    {
        ContentControlInteractionPlanner.Tooltip(
                new ContentControl(ContentControlKind.CheckBox))
            .Should().Be("Checkbox content control (click to toggle)");

        ContentControlInteractionPlanner.Tooltip(
                new ContentControl(ContentControlKind.ComboBox, Alias: "Status"))
            .Should().Be("Combo box: Status");
    }

    [Fact]
    public void ToggleCheckBox_ReturnsUpdatedRunWithGlyphAndState()
    {
        var run = Run.CheckBoxControl(@checked: false, tag: "Approval");
        run.HyperlinkTooltip = "kept";

        var updated = ContentControlInteractionPlanner.ToggleCheckBox(run);

        updated.Should().NotBeNull();
        updated!.Text.Should().Be(ContentControl.CheckedGlyph);
        updated.Control!.Checked.Should().BeTrue();
        updated.Control.Tag.Should().Be("Approval");
        updated.HyperlinkTooltip.Should().Be("kept");
    }

    [Fact]
    public void ToggleCheckBox_WithCustomGlyphMetadata_WritesTheDocumentsOwnGlyphNotTheAppDefault()
    {
        // Document author customized both states away from Word's default 2612/2610 glyphs.
        var metadata = new ContentControlCheckBoxMetadata(
            CheckedState: new ContentControlCheckBoxStateMetadata("2714", "Segoe UI Symbol"),
            UncheckedState: new ContentControlCheckBoxStateMetadata("2716", "Segoe UI Symbol"));
        var run = Run.CheckBoxControl(@checked: false, tag: "Approval", checkBoxMetadata: metadata);

        var toggledOn = ContentControlInteractionPlanner.ToggleCheckBox(run);

        toggledOn.Should().NotBeNull();
        toggledOn!.Text.Should().Be("✔");
        toggledOn.Control!.Checked.Should().BeTrue();
        toggledOn.Control.CheckBoxMetadata.Should().Be(metadata);

        var toggledBackOff = ContentControlInteractionPlanner.ToggleCheckBox(toggledOn);

        toggledBackOff.Should().NotBeNull();
        toggledBackOff!.Text.Should().Be("✖");
        toggledBackOff.Control!.Checked.Should().BeFalse();
    }

    [Fact]
    public void ToggleCheckBox_WithoutCheckBoxMetadata_StillUsesTheAppDefaultGlyphs()
    {
        // Sibling / no-regression case: a checkbox with no custom w14 state metadata (the common
        // case) must keep writing the app's own default glyphs, unchanged from before this fix.
        var run = Run.CheckBoxControl(@checked: false, tag: "Approval");

        var updated = ContentControlInteractionPlanner.ToggleCheckBox(run);

        updated.Should().NotBeNull();
        updated!.Text.Should().Be(ContentControl.CheckedGlyph);
        updated.Control!.Checked.Should().BeTrue();
    }

    [Fact]
    public void SelectItem_UpdatesDropDownAndComboText()
    {
        var items = new[]
        {
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G")
        };
        var dropDown = Run.DropDownListControl(items);
        var combo = Run.ComboBoxControl(items);

        ContentControlInteractionPlanner.SelectItem(dropDown, 1)!.Text.Should().Be("Green");
        ContentControlInteractionPlanner.SelectItem(combo, 1)!.Text.Should().Be("Green");
        ContentControlInteractionPlanner.SelectItem(dropDown, -1).Should().BeNull();
    }

    [Fact]
    public void RelativeDateChoices_UseControlFormatAndCulture()
    {
        var run = Run.DatePickerControl("old", dateFormat: "yyyy-MM-dd");
        var culture = CultureInfo.InvariantCulture;
        var today = new DateTime(2026, 7, 4);

        var choices = ContentControlInteractionPlanner.RelativeDateChoices(run.Control!, today, culture);
        var updated = ContentControlInteractionPlanner.SelectRelativeDate(run, choiceIndex: 2, today, culture);

        choices.Select(choice => choice.Label).Should().Equal("Today", "Yesterday", "Tomorrow");
        choices.Select(choice => choice.DisplayText).Should().Equal("2026-07-04", "2026-07-03", "2026-07-05");
        updated.Should().NotBeNull();
        updated!.Text.Should().Be("2026-07-05");
        updated.Control!.DateFormat.Should().Be("yyyy-MM-dd");
    }


    /// <summary>
    /// The relative choices only ever reach today, yesterday and tomorrow — a calendar has to be able to
    /// commit any date at all, in the control's own format.
    /// </summary>
    [Fact]
    public void SelectDate_WritesAnyDateInTheControlsFormat()
    {
        var run = Run.DatePickerControl("old", dateFormat: "yyyy-MM-dd");

        var updated = ContentControlInteractionPlanner.SelectDate(
            run,
            new DateTime(1999, 12, 31),
            CultureInfo.InvariantCulture);

        updated.Should().NotBeNull();
        updated!.Text.Should().Be("1999-12-31");
        updated.Control!.Kind.Should().Be(ContentControlKind.DatePicker);
        ContentControlInteractionPlanner.SelectDate(new Run("plain"), new DateTime(1999, 12, 31))
            .Should().BeNull("only a date picker takes a date");
    }

    /// <summary>A calendar opens on the date the field already shows, not on today.</summary>
    [Fact]
    public void CurrentDate_ReadsTheFieldsOwnDateBackAndDeclinesAnythingElse()
    {
        var culture = CultureInfo.InvariantCulture;
        var run = Run.DatePickerControl("2026-07-04", dateFormat: "yyyy-MM-dd");

        ContentControlInteractionPlanner.CurrentDate(run.Control, run.Text, culture)
            .Should().Be(new DateTime(2026, 7, 4));
        // A field whose text an import wrote in some other shape still parses.
        ContentControlInteractionPlanner.CurrentDate(run.Control, "2026-08-20T00:00:00", culture)
            .Should().Be(new DateTime(2026, 8, 20));
        ContentControlInteractionPlanner.CurrentDate(run.Control, "Click to enter a date", culture)
            .Should().BeNull("a placeholder is not a date — the caller opens on today");
        ContentControlInteractionPlanner.CurrentDate(Run.PlainTextControl("x").Control, "2026-07-04", culture)
            .Should().BeNull();
    }
    [Fact]
    public void CanEditExistingContentControl_FollowsSharedRestrictEditingPolicy()
    {
        var controlRun = Run.CheckBoxControl(@checked: false);
        var plainRun = new Run("plain");

        ContentControlInteractionPlanner.CanEditExistingContentControl(
                controlRun,
                Policy(ProtectionMode.None, isMarkedAsFinal: false))
            .Should().BeTrue();
        ContentControlInteractionPlanner.CanEditExistingContentControl(
                controlRun,
                Policy(ProtectionMode.FillingForms, isMarkedAsFinal: false))
            .Should().BeTrue();
        ContentControlInteractionPlanner.CanEditExistingContentControl(
                controlRun,
                Policy(ProtectionMode.ReadOnly, isMarkedAsFinal: false))
            .Should().BeFalse();
        ContentControlInteractionPlanner.CanEditExistingContentControl(
                controlRun,
                Policy(ProtectionMode.None, isMarkedAsFinal: true))
            .Should().BeFalse();
        ContentControlInteractionPlanner.CanEditExistingContentControl(
                plainRun,
                Policy(ProtectionMode.FillingForms, isMarkedAsFinal: false))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(ContentControlLockMode.NotSpecified, true)]
    [InlineData(ContentControlLockMode.Unlocked, true)]
    [InlineData(ContentControlLockMode.ControlLocked, true)]
    [InlineData(ContentControlLockMode.ContentLocked, false)]
    [InlineData(ContentControlLockMode.ControlAndContentLocked, false)]
    public void CanEditExistingContentControl_HonorsContentLock(
        ContentControlLockMode lockMode,
        bool expected)
    {
        var run = Run.CheckBoxControl(@checked: false);
        run.Control = run.Control! with { LockMode = lockMode };

        ContentControlInteractionPlanner.CanEditExistingContentControl(
                run,
                Policy(ProtectionMode.None, isMarkedAsFinal: false))
            .Should().Be(expected);
    }

    [Fact]
    public void WithText_replaces_only_the_text_and_keeps_every_other_run_mark()
    {
        var run = Run.PlainTextControl("Bob", tag: "Applicant", alias: "Name");
        run.HyperlinkTooltip = "preserved";
        run.CommentId = 7;
        run.Formatting = run.Formatting with { Bold = true };

        var updated = ContentControlInteractionPlanner.WithText(run, "Bobby");

        updated.Should().NotBeNull();
        updated!.Text.Should().Be("Bobby");
        updated.Control!.Kind.Should().Be(ContentControlKind.PlainText);
        updated.Control!.Tag.Should().Be("Applicant");
        updated.Control!.Alias.Should().Be("Name");
        updated.HyperlinkTooltip.Should().Be("preserved");
        updated.CommentId.Should().Be(7);
        updated.Formatting.Bold.Should().BeTrue();

        ContentControlInteractionPlanner.WithText(new Run("plain"), "x").Should().BeNull();
    }

    /// <summary>
    /// K3: a placeholder-showing control (w:showingPlcHdr) stops showing placeholder text the moment any
    /// of the four operations that call the shared CloneWith gives it real content -- typed text, a
    /// checkbox toggle, a picked list item, or a picked relative date -- matching Word, which drops the
    /// flag on any edit even if the result is empty. Before the fix, CloneWith carried the flag through
    /// unchanged, so a filled-in control still reported itself as showing placeholder text.
    /// </summary>
    [Fact]
    public void CloneWith_ClearsShowingPlaceholder_OnEveryContentSettingOperation()
    {
        static ContentControl Placeholder(ContentControlKind kind, bool @checked = false) => new(
            kind,
            Checked: @checked,
            WordMetadata: new ContentControlWordMetadata(ShowingPlaceholder: true));

        var typed = new Run("Click to enter text") { Control = Placeholder(ContentControlKind.PlainText) };
        var afterTyping = ContentControlInteractionPlanner.WithText(typed, "Bobby");
        afterTyping!.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "typing real text into a placeholder-showing field must clear w:showingPlcHdr");

        var checkbox = new Run(ContentControl.UncheckedGlyph)
        {
            Control = Placeholder(ContentControlKind.CheckBox)
        };
        var afterToggle = ContentControlInteractionPlanner.ToggleCheckBox(checkbox);
        afterToggle!.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "toggling a placeholder-showing checkbox must clear w:showingPlcHdr");

        var list = new Run("Choose an item")
        {
            Control = Placeholder(ContentControlKind.DropDownList) with
            {
                ListItems = [new ContentControlListItem("Choose an item"), new ContentControlListItem("Item 1")]
            }
        };
        var afterSelect = ContentControlInteractionPlanner.SelectItem(list, 1);
        afterSelect!.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "picking a list item on a placeholder-showing drop-down must clear w:showingPlcHdr");

        var date = new Run("Click to enter a date") { Control = Placeholder(ContentControlKind.DatePicker) };
        var afterDate = ContentControlInteractionPlanner.SelectRelativeDate(date, choiceIndex: 0);
        afterDate!.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "picking a relative date on a placeholder-showing date picker must clear w:showingPlcHdr");
    }

    /// <summary>
    /// Sibling no-regression coverage: CloneWith must not disturb a control that was never showing a
    /// placeholder (stays false, not flipped true) and must not manufacture WordMetadata out of thin air
    /// for a control that never had any (stays null, so DocxWriter does not start emitting an empty
    /// w:sdtPr addition it never used to).
    /// </summary>
    [Fact]
    public void CloneWith_LeavesNonPlaceholderControlsUntouched()
    {
        var noMetadata = new Run("Bob") { Control = new ContentControl(ContentControlKind.PlainText) };
        var updatedNoMetadata = ContentControlInteractionPlanner.WithText(noMetadata, "Bobby");
        updatedNoMetadata!.Control!.WordMetadata.Should().BeNull(
            "a control with no Word metadata must not gain any just from being edited");

        var alreadyReal = new Run("Bob")
        {
            Control = new ContentControl(
                ContentControlKind.PlainText,
                WordMetadata: new ContentControlWordMetadata(ShowingPlaceholder: false, Temporary: true))
        };
        var updatedAlreadyReal = ContentControlInteractionPlanner.WithText(alreadyReal, "Bobby");
        updatedAlreadyReal!.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse();
        updatedAlreadyReal.Control!.WordMetadata!.Temporary.Should().BeTrue(
            "unrelated Word metadata fields must survive untouched");
    }

    [Theory]
    [InlineData(ContentControlKind.PlainText, true)]
    [InlineData(ContentControlKind.RichText, true)]
    [InlineData(ContentControlKind.ComboBox, true)]
    [InlineData(ContentControlKind.CheckBox, false)]
    [InlineData(ContentControlKind.DatePicker, false)]
    [InlineData(ContentControlKind.DropDownList, false)]
    public void IsTextEntryControl_only_accepts_controls_that_take_typed_text(
        ContentControlKind kind,
        bool expected) =>
        ContentControlInteractionPlanner.IsTextEntryControl(new ContentControl(kind))
            .Should().Be(expected);

    [Fact]
    public void CanEditContentControlText_combines_the_kind_with_the_protection_policy()
    {
        var text = Run.PlainTextControl("Bob");
        var checkBox = Run.CheckBoxControl(@checked: false);

        ContentControlInteractionPlanner.CanEditContentControlText(
                text,
                Policy(ProtectionMode.FillingForms, isMarkedAsFinal: false))
            .Should().BeTrue("filling in forms exists to let the user type into fields");
        ContentControlInteractionPlanner.CanEditContentControlText(
                checkBox,
                Policy(ProtectionMode.FillingForms, isMarkedAsFinal: false))
            .Should().BeFalse("a check box owns its glyph");
        ContentControlInteractionPlanner.CanEditContentControlText(
                text,
                Policy(ProtectionMode.ReadOnly, isMarkedAsFinal: false))
            .Should().BeFalse();
        ContentControlInteractionPlanner.CanEditContentControlText(
                new Run("plain"),
                Policy(ProtectionMode.None, isMarkedAsFinal: false))
            .Should().BeFalse();
    }

    private static RestrictEditingEnforcementPolicy Policy(ProtectionMode mode, bool isMarkedAsFinal) =>
        RestrictEditingEnforcementPolicy.From(new ProtectionSettings(mode), isMarkedAsFinal);
}
