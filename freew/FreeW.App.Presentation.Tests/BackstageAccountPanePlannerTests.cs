using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageAccountPanePlannerTests
{
    [Fact]
    public void Build_UsesLocalProductUserAndStorageFields()
    {
        var plan = BackstageAccountPanePlanner.Build(
            productName: "FreeW",
            version: "1.2.3",
            userName: "Ada",
            machineName: "WORD-BOX",
            dataFolder: @"C:\Users\Ada\AppData\Local\FreeW");

        plan.Description.Should().Contain("local product and user information");
        plan.OptionsText.Should().Be("FreeW Options...");
        plan.Groups.Select(group => group.Heading).Should().Equal("Product Information", "User Information");
        plan.Groups[0].Fields.Should().Equal(
            new BackstageFieldRow("Product", "FreeW"),
            new BackstageFieldRow("Version", "1.2.3"),
            new BackstageFieldRow("Device", "WORD-BOX"));
        plan.Groups[1].Fields.Should().Equal(
            new BackstageFieldRow("Windows user", "Ada"),
            new BackstageFieldRow("Data folder", @"C:\Users\Ada\AppData\Local\FreeW"),
            new BackstageFieldRow("Connected services", "Local desktop app"));
    }

    [Fact]
    public void Build_UsesFallbackForBlankValues()
    {
        var plan = BackstageAccountPanePlanner.Build(
            productName: "",
            version: " ",
            userName: "\t",
            machineName: "",
            dataFolder: "");

        plan.Groups.SelectMany(group => group.Fields)
            .Where(field => field.Label != "Connected services")
            .Select(field => field.Value)
            .Should().AllBeEquivalentTo("Not available");
    }
}
