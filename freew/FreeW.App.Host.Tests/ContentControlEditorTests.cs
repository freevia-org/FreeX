using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Editor coverage for the date-picker, drop-down-list, combo-box and rich-text content controls in
/// <see cref="DocumentView"/>: each insert command must drop a content-control run that renders into the
/// FlowDocument and survives a <see cref="DocumentView.CommitToModel"/> round-trip with its kind-specific
/// properties (date format, list items) intact. Runs on an STA thread (<c>[StaFact]</c>, via Xunit.StaFact)
/// because the RichTextBox/FlowDocument need STA. Mirrors <see cref="CheckBoxContentControlTests"/>.
/// </summary>
public sealed class ContentControlEditorTests
{
    private static DocumentView NewView()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        return view;
    }

    private static DocumentView NewViewWithParagraph(string text)
    {
        var model = new TextDocument();
        model.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadModel(model);
        return view;
    }

    private static Run CommittedControlRun(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.Control is not null);
    }

    [StaFact]
    public void InsertRichTextControl_RoundTrips()
    {
        var view = NewView();
        view.InsertRichTextControl(tag: "Bio", alias: "Biography");

        var run = CommittedControlRun(view);
        run.Control!.Kind.Should().Be(ContentControlKind.RichText);
        run.Control.Tag.Should().Be("Bio");
        run.Control.Alias.Should().Be("Biography");
        run.Text.Should().NotBeEmpty();
    }

    [StaFact]
    public void InsertPlainTextControl_InsertsAtMiddleCaret()
    {
        var view = NewViewWithParagraph("Hello world");
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = PositionAfterText(paragraph, "Hello ");

        view.InsertPlainTextControl(tag: "Mid");
        view.CommitToModel();

        var runs = view.Model.Blocks
            .OfType<Paragraph>()
            .Single()
            .Runs;
        runs.Select(r => r.Text).Should().Equal("Hello ", "Click to enter text", "world");
        runs[1].Control.Should().NotBeNull();
        runs[1].Control!.Tag.Should().Be("Mid");
    }

    [StaFact]
    public void InsertDatePickerControl_RoundTrips_WithFormat()
    {
        var view = NewView();
        view.InsertDatePickerControl(tag: "Signed", alias: "Signed on", dateFormat: "yyyy-MM-dd");

        var run = CommittedControlRun(view);
        run.Control!.Kind.Should().Be(ContentControlKind.DatePicker);
        run.Control.DateFormat.Should().Be("yyyy-MM-dd");
        run.Control.Tag.Should().Be("Signed");
        // Today's date rendered in the requested format (length of "yyyy-MM-dd").
        run.Text.Should().HaveLength(10);
    }

    [StaFact]
    public void InsertDropDownListControl_RoundTrips_Items()
    {
        var view = NewView();
        var items = new[]
        {
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G")
        };
        view.InsertDropDownListControl(items, tag: "Color");

        var run = CommittedControlRun(view);
        run.Control!.Kind.Should().Be(ContentControlKind.DropDownList);
        run.Control.Items.Select(i => i.DisplayText).Should().ContainInOrder("Red", "Green");
        run.Control.Items.Select(i => i.Value).Should().ContainInOrder("R", "G");
        run.Text.Should().Be("Red", "the first item is the initial selection");
    }

    [StaFact]
    public void InsertComboBoxControl_RoundTrips_Items()
    {
        var view = NewView();
        var items = new[] { new ContentControlListItem("A", "a"), new ContentControlListItem("B", "b") };
        view.InsertComboBoxControl(items, tag: "Pick");

        var run = CommittedControlRun(view);
        run.Control!.Kind.Should().Be(ContentControlKind.ComboBox);
        run.Control.Items.Should().HaveCount(2);
        run.Control.Tag.Should().Be("Pick");
    }

    [StaFact]
    public void InsertControlsWithoutItems_UseDefaultSample()
    {
        var view = NewView();
        view.InsertDropDownListControl();

        var run = CommittedControlRun(view);
        run.Control!.Items.Should().Equal(ContentControlInteractionPlanner.DefaultListItems);
        run.Text.Should().Be(ContentControlInteractionPlanner.DefaultListItems[0].DisplayText);
    }

    [StaFact]
    public void InsertControlsWithoutValues_UseEverySharedPlannerPolicy()
    {
        var plainView = NewView();
        plainView.InsertPlainTextControl();
        CommittedControlRun(plainView).Text.Should().Be(ContentControlInteractionPlanner.DefaultPromptText);

        var richView = NewView();
        richView.InsertRichTextControl();
        CommittedControlRun(richView).Text.Should().Be(ContentControlInteractionPlanner.DefaultPromptText);

        var dateView = NewView();
        dateView.InsertDatePickerControl();
        var date = CommittedControlRun(dateView);
        date.Control!.DateFormat.Should().Be(ContentControlInteractionPlanner.DateFormatOrDefault(null));
        date.Text.Should().Be(ContentControlInteractionPlanner.FormatDate((string?)null, DateTime.Today));

        var comboView = NewView();
        comboView.InsertComboBoxControl([]);
        CommittedControlRun(comboView).Control!.Items.Should().Equal(
            ContentControlInteractionPlanner.DefaultListItems);
    }

    [StaTheory]
    [InlineData(ContentControlLockMode.ControlLocked, true)]
    [InlineData(ContentControlLockMode.ContentLocked, false)]
    [InlineData(ContentControlLockMode.ControlAndContentLocked, false)]
    public void ExistingControlInteraction_HonorsContentControlLock(
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
        view.LoadModel(document);

        view.ToggleContentControl(0, 0).Should().Be(expected);
        view.Model.Paragraphs.Single().Runs.Single().Control!.Checked.Should().Be(expected);
    }

    private static System.Windows.Documents.TextPointer PositionAfterText(
        System.Windows.Documents.Paragraph paragraph,
        string text)
    {
        var remaining = text.Length;
        var pointer = paragraph.ContentStart;
        while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(System.Windows.Documents.LogicalDirection.Forward) ==
                System.Windows.Documents.TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(System.Windows.Documents.LogicalDirection.Forward);
                if (remaining <= runText.Length)
                    return pointer.GetPositionAtOffset(remaining)!;
                remaining -= runText.Length;
            }

            pointer = pointer.GetNextContextPosition(System.Windows.Documents.LogicalDirection.Forward);
        }

        throw new InvalidOperationException($"Text '{text}' was not found in the paragraph.");
    }

    /// <summary>
    /// Cross-host parity guard for the Avalonia work: editing the body text AROUND a field must keep the
    /// field, and clearing the field's own text must keep the (empty) control. The WPF host reaches the
    /// model through a native-editor commit rather than a cell round-trip, so it needs its own proof.
    /// </summary>
    [StaFact]
    public void EditingBodyTextAroundAControl_KeepsTheControl()
    {
        var control = Run.PlainTextControl("Bob", tag: "Applicant");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(" (staff)"));
        var model = new TextDocument();
        model.Blocks.Clear();
        model.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(model);

        var rendered = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = PositionAfterText(rendered, "Name");
        view.InsertText("!");

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().Single();
        committed.PlainText.Should().Be("Name!: Bob (staff)");
        var fields = committed.Runs.Where(run => run.Control is not null).ToList();
        fields.Should().ContainSingle("the field survives an edit to the text around it");
        fields[0].Text.Should().Be("Bob");
        fields[0].Control!.Tag.Should().Be("Applicant");
    }
}
