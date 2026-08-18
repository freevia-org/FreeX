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
