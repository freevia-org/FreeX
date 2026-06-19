using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LocalAccountInfoPlannerTests
{
    [Fact]
    public void Build_PopulatesProductDeviceAndNotices()
    {
        var plan = LocalAccountInfoPlanner.Build(
            typeof(LocalAccountInfoPlannerTests).Assembly,
            deviceName: "FREEX-PC",
            userName: "quinn",
            optionsAvailable: true);

        plan.ProductName.Should().Be(AppHelpInfo.ProductName);
        plan.VersionText.Should().StartWith("Version ");
        plan.DeviceName.Should().Be("FREEX-PC");
        plan.UserName.Should().Be("quinn");
        plan.OptionsAvailable.Should().BeTrue();
        plan.TrademarkNotice.Should().Be(AppHelpInfo.TrademarkNotice);
        plan.LicenseNotice.Should().Be(AppHelpInfo.ProjectLicenseNotice);
        plan.PrivacyNotice.Should().Be(AppHelpInfo.PrivacyNotice);
        plan.HelpUrl.Should().Be(AppHelpInfo.HelpUrl);
    }

    [Fact]
    public void Build_BlankIdentity_NormalizesToEmpty()
    {
        var plan = LocalAccountInfoPlanner.Build(
            typeof(LocalAccountInfoPlannerTests).Assembly,
            deviceName: "   ",
            userName: null);

        plan.DeviceName.Should().BeEmpty();
        plan.UserName.Should().BeEmpty();
    }
}
