using System.Globalization;
using Free.Shared.Opc;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentPropertiesDialogSessionTests
{
    [Fact]
    public void Session_owns_ordered_field_catalog_initial_values_and_read_only_projection()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var created = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.FromHours(3));
        var properties = new DocumentProperties
        {
            Title = "Report",
            Comments = "Draft",
            LastModifiedBy = "Word Owner",
            Created = created,
        };

        var session = new DocumentPropertiesDialogSession(properties, culture);

        session.Surface.Title.Should().Be("Document Properties");
        session.Surface.Fields.Select(field => field.Field).Should().Equal(
            DocumentPropertiesDialogField.Title,
            DocumentPropertiesDialogField.Author,
            DocumentPropertiesDialogField.Subject,
            DocumentPropertiesDialogField.Category,
            DocumentPropertiesDialogField.Keywords,
            DocumentPropertiesDialogField.Comments,
            DocumentPropertiesDialogField.ContentStatus,
            DocumentPropertiesDialogField.Language,
            DocumentPropertiesDialogField.Version,
            DocumentPropertiesDialogField.LastModifiedBy,
            DocumentPropertiesDialogField.Created,
            DocumentPropertiesDialogField.Modified);

        Field(DocumentPropertiesDialogField.Title).Should().Be(
            new DocumentPropertiesDialogFieldSpec(
                DocumentPropertiesDialogField.Title,
                "Title:",
                "DocumentPropertiesTitle",
                "Report",
                IsEditable: true));
        Field(DocumentPropertiesDialogField.Comments).IsMultiline.Should().BeTrue();
        Field(DocumentPropertiesDialogField.LastModifiedBy).Value.Should().Be("Word Owner");
        Field(DocumentPropertiesDialogField.Created).Value.Should().Be(
            created.ToLocalTime().ToString("g", culture));
        Field(DocumentPropertiesDialogField.Modified).Value.Should().Be("-");

        DocumentPropertiesDialogFieldSpec Field(DocumentPropertiesDialogField field) =>
            session.Surface.Fields.Single(spec => spec.Field == field);
    }

    [Fact]
    public void Session_normalizes_values_and_owns_command_and_dirty_commit_decisions()
    {
        var properties = new DocumentProperties { Title = "Before" };
        var session = new DocumentPropertiesDialogSession(properties, CultureInfo.InvariantCulture);
        var input = new DocumentPropertiesDialogInput(
            "  After  ",
            " Ada ",
            "Parity",
            " freew metadata ",
            "   ",
            " Reports ",
            " Final ",
            " en-GB ",
            " 4.2 ");

        var plan = session.PlanCommit(accepted: true, input);

        plan.ShouldExecuteCommand.Should().BeTrue();
        plan.ShouldMarkDirty.Should().BeTrue();
        plan.Values.Should().Be(new DocumentPropertiesDialogValues(
            "After",
            "Ada",
            "Parity",
            "freew metadata",
            null,
            "Reports",
            "Final",
            "en-GB",
            "4.2"));
        properties.Title.Should().Be("Before");
    }

    [Fact]
    public void Input_capture_projects_editable_fields_in_contract_order()
    {
        var values = Enum.GetValues<DocumentPropertiesDialogField>()
            .ToDictionary(field => field, field => $"value-{field}");

        var input = DocumentPropertiesDialogInput.Capture(field => values[field]);

        input.Should().Be(new DocumentPropertiesDialogInput(
            "value-Title",
            "value-Author",
            "value-Subject",
            "value-Keywords",
            "value-Comments",
            "value-Category",
            "value-ContentStatus",
            "value-Language",
            "value-Version"));
    }

    [Fact]
    public void Session_cancellation_does_not_dispatch_or_dirty_the_document()
    {
        var session = new DocumentPropertiesDialogSession(
            new DocumentProperties(),
            CultureInfo.InvariantCulture);

        var plan = session.PlanCommit(accepted: false, input: null);

        plan.ShouldExecuteCommand.Should().BeFalse();
        plan.ShouldMarkDirty.Should().BeFalse();
        plan.Values.Should().BeNull();
    }
}
