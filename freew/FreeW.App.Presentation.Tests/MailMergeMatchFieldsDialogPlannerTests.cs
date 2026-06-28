using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeMatchFieldsDialogPlannerTests
{
    [Fact]
    public void RolePlans_UseWordRoleOrderAndLabels()
    {
        var plans = MailMergeMatchFieldsDialogPlanner.GetRolePlans([], new FieldMapping());

        plans.Select(plan => plan.Role).Should().Equal(
            FieldRole.Title,
            FieldRole.FirstName,
            FieldRole.MiddleName,
            FieldRole.LastName,
            FieldRole.Suffix,
            FieldRole.Company,
            FieldRole.Address1,
            FieldRole.Address2,
            FieldRole.City,
            FieldRole.State,
            FieldRole.PostalCode,
            FieldRole.Country);
        plans.Select(plan => plan.Label).Should().Equal(
            "Title (Mr., Mrs., \u2026)",
            "First Name",
            "Middle Name",
            "Last Name",
            "Suffix (Jr., Sr., \u2026)",
            "Company",
            "Address 1",
            "Address 2",
            "City",
            "State",
            "Postal Code",
            "Country or Region");
    }

    [Fact]
    public void RolePlans_SelectMappedHeaderCaseInsensitively()
    {
        var mapping = new FieldMapping
        {
            [FieldRole.FirstName] = "first name",
            [FieldRole.PostalCode] = "zip"
        };

        var plans = MailMergeMatchFieldsDialogPlanner.GetRolePlans(
            ["First Name", "ZIP"],
            mapping);

        plans.Single(plan => plan.Role == FieldRole.FirstName).SelectedChoice.Should().Be("First Name");
        plans.Single(plan => plan.Role == FieldRole.PostalCode).SelectedChoice.Should().Be("ZIP");
    }

    [Fact]
    public void RolePlans_DefaultToNotMatchedWhenNoCurrentHeaderMatchExists()
    {
        var mapping = new FieldMapping
        {
            [FieldRole.Company] = "Organization"
        };

        var plans = MailMergeMatchFieldsDialogPlanner.GetRolePlans(
            ["Company"],
            mapping);

        plans.Single(plan => plan.Role == FieldRole.Company).SelectedChoice
            .Should().Be(MailMergeMatchFieldsDialogPlanner.NotMatchedChoice);
        plans.Single(plan => plan.Role == FieldRole.LastName).SelectedChoice
            .Should().Be(MailMergeMatchFieldsDialogPlanner.NotMatchedChoice);
    }

    [Fact]
    public void ColumnChoices_StartWithNotMatchedSentinel()
    {
        var choices = MailMergeMatchFieldsDialogPlanner.GetColumnChoices(["First", "Last"]);

        choices.Should().Equal(
            MailMergeMatchFieldsDialogPlanner.NotMatchedChoice,
            "First",
            "Last");
    }

    [Fact]
    public void CreateResult_CreatesFieldMappingFromSelectedValues()
    {
        var result = MailMergeMatchFieldsDialogPlanner.CreateResult(
            new Dictionary<FieldRole, string?>
            {
                [FieldRole.FirstName] = "Given",
                [FieldRole.LastName] = "Family",
                [FieldRole.City] = "Town"
            });

        result[FieldRole.FirstName].Should().Be("Given");
        result[FieldRole.LastName].Should().Be("Family");
        result[FieldRole.City].Should().Be("Town");
    }

    [Fact]
    public void CreateResult_NormalizesNullEmptyAndSentinelToUnmapped()
    {
        var result = MailMergeMatchFieldsDialogPlanner.CreateResult(
            new Dictionary<FieldRole, string?>
            {
                [FieldRole.Title] = null,
                [FieldRole.FirstName] = string.Empty,
                [FieldRole.LastName] = MailMergeMatchFieldsDialogPlanner.NotMatchedChoice,
                [FieldRole.Company] = "(NOT MATCHED)",
                [FieldRole.Address1] = "Address"
            });

        result[FieldRole.Title].Should().BeNull();
        result[FieldRole.FirstName].Should().BeNull();
        result[FieldRole.LastName].Should().BeNull();
        result[FieldRole.Company].Should().BeNull();
        result[FieldRole.Address1].Should().Be("Address");
    }
}
