using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewContentControlInteractionTests
{
    [Fact]
    public void InsertedContentControls_UseSharedPlannerDefaults()
    {
        InsertedRun(view => view.InsertPlainTextControl()).Text
            .Should().Be(ContentControlInteractionPlanner.DefaultPromptText);

        InsertedRun(view => view.InsertRichTextControl()).Text
            .Should().Be(ContentControlInteractionPlanner.DefaultPromptText);

        var checkBox = InsertedRun(view => view.InsertCheckBoxControl());
        checkBox.Text.Should().Be(ContentControl.UncheckedGlyph);
        checkBox.Control!.Checked.Should().BeFalse();

        var date = InsertedRun(view => view.InsertDatePickerControl());
        date.Text.Should().Be(ContentControlInteractionPlanner.FormatDate(
            ContentControl.DefaultDateFormat,
            DateTime.Today));
        date.Control!.DateFormat.Should().Be(ContentControl.DefaultDateFormat);

        var dropDown = InsertedRun(view => view.InsertDropDownListControl());
        dropDown.Text.Should().Be("Choose an item");
        dropDown.Control!.Items.Select(item => item.DisplayText)
            .Should().Equal(ContentControlInteractionPlanner.DefaultListItems.Select(item => item.DisplayText));

        var combo = InsertedRun(view => view.InsertComboBoxControl());
        combo.Text.Should().Be("Choose an item");
        combo.Control!.Items.Select(item => item.DisplayText)
            .Should().Equal(ContentControlInteractionPlanner.DefaultListItems.Select(item => item.DisplayText));
    }

    [Fact]
    public void PublicInteractionMethods_MutateContentControlRuns()
    {
        var paragraph = new Paragraph();
        var checkBox = Run.CheckBoxControl(@checked: false);
        checkBox.HyperlinkTooltip = "preserved";
        paragraph.Runs.Add(checkBox);
        paragraph.Runs.Add(Run.DropDownListControl(
        [
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G")
        ]));
        paragraph.Runs.Add(Run.DatePickerControl("old", dateFormat: "yyyy-MM-dd"));
        paragraph.Runs.Add(new Run("plain"));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.ToggleContentControl(0, 0).Should().BeTrue();
        paragraph.Runs[0].Text.Should().Be(ContentControl.CheckedGlyph);
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();
        paragraph.Runs[0].HyperlinkTooltip.Should().Be("preserved");

        view.SelectContentControlItem(0, 1, 1).Should().BeTrue();
        paragraph.Runs[1].Text.Should().Be("Green");
        paragraph.Runs[1].Control!.Kind.Should().Be(ContentControlKind.DropDownList);

        view.SelectContentControlRelativeDate(0, 2, choiceIndex: 2).Should().BeTrue();
        paragraph.Runs[2].Text.Should().Be(DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"));
        paragraph.Runs[2].Control!.DateFormat.Should().Be("yyyy-MM-dd");

        view.ToggleContentControl(0, 3).Should().BeFalse();
        view.SelectContentControlItem(0, 1, 99).Should().BeFalse();
        view.SelectContentControlRelativeDate(0, 2, -1).Should().BeFalse();
    }


    /// <summary>
    /// A date field used to reach only today, yesterday and tomorrow; the click and keyboard gestures now
    /// open a calendar, which commits through this seam (a flyout cannot be clicked in a headless run).
    /// The edit is one undoable command and honours the same locks as every other field edit.
    /// </summary>
    [Fact]
    public void SelectContentControlDate_CommitsAnyDateAsOneUndoableFieldEdit()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.DatePickerControl("2026-07-04", dateFormat: "yyyy-MM-dd"));
        paragraph.Runs.Add(Run.DatePickerControl("2026-07-04", dateFormat: "yyyy-MM-dd"));
        paragraph.Runs[1].Control = paragraph.Runs[1].Control! with
        {
            LockMode = ContentControlLockMode.ContentLocked,
        };

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.SelectContentControlDate(0, 0, new DateTime(1999, 12, 31)).Should().BeTrue();
        paragraph.Runs[0].Text.Should().Be("1999-12-31", "a calendar reaches dates no relative choice does");
        paragraph.Runs[0].Control!.Kind.Should().Be(ContentControlKind.DatePicker);

        view.Undo();
        view.Document.Paragraphs.Single().Runs[0].Text.Should().Be("2026-07-04");

        view.SelectContentControlDate(0, 1, new DateTime(1999, 12, 31))
            .Should().BeFalse("a content-locked field takes no picked date either");
    }
    [Fact]
    public void PublicInteractionMethods_AllowExistingControlsUnderFillingFormsButBlockStricterProtection()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "Agree"));
        paragraph.Runs.Add(new Run("body"));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.SetProtection(ProtectionMode.FillingForms);
        view.InsertCheckBoxControl();

        paragraph.Runs.Should().HaveCount(2, "Filling Forms may fill existing fields but must not insert new body controls");
        view.ToggleContentControl(0, 0).Should().BeTrue();
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();

        view.CanUndo.Should().BeTrue();
        view.Undo();
        paragraph.Runs[0].Control!.Checked.Should().BeFalse();
        view.CanRedo.Should().BeTrue();
        view.Redo();
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();

        view.SetProtection(ProtectionMode.ReadOnly);
        view.ToggleContentControl(0, 0).Should().BeFalse();
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();

        view.SetProtection(ProtectionMode.None);
        view.SetMarkedAsFinal(true);
        view.ToggleContentControl(0, 0).Should().BeFalse();
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();
    }

    [Theory]
    [InlineData(ContentControlLockMode.ControlLocked, true)]
    [InlineData(ContentControlLockMode.ContentLocked, false)]
    [InlineData(ContentControlLockMode.ControlAndContentLocked, false)]
    public void PublicInteractionMethods_HonorContentControlLock(
        ContentControlLockMode lockMode,
        bool expected)
    {
        var run = Run.CheckBoxControl(@checked: false);
        run.Control = run.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.ToggleContentControl(0, 0).Should().Be(expected);
        paragraph.Runs[0].Control!.Checked.Should().Be(expected);
    }

    private static Run InsertedRun(Action<DocumentView> insert)
    {
        var view = new DocumentView();
        view.LoadDocument(TextDocument.CreateEmpty());

        insert(view);

        return view.Document.Blocks
            .OfType<Paragraph>()
            .Single()
            .Runs
            .Single(run => run.Control is not null);
    }
}
