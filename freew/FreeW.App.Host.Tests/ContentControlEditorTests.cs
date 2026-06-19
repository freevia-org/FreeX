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
        run.Control!.Items.Should().NotBeEmpty("a list control inserted without items gets a default sample");
    }
}
