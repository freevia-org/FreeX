using FluentAssertions;
using Free.Shared.Shell;

namespace FreeX.App.Host.Logic.Tests;

public sealed class ShellStringAdapterTests
{
    [Fact]
    public void StaticShellStrings_NeutralEnglishMatchesDefaultFallback()
    {
        var strings = StaticShellStrings.NeutralEnglish;

        strings.Ok.Should().Be("_OK");
        strings.Cancel.Should().Be("_Cancel");
        strings.ErrorTitle.Should().Be("Error");
        strings.WarningTitle.Should().Be("Warning");
        strings.InformationTitle.Should().Be("Information");
        strings.ConfirmTitle.Should().Be("Confirm");
        strings.CreateAutomationName("_OK").Should().Be("OK");
    }

    [Fact]
    public void StaticShellStrings_ProductTitleKeepsAppTerminologyForMessageTitles()
    {
        var strings = StaticShellStrings.ForProductTitle("FreeP");

        strings.Ok.Should().Be("_OK");
        strings.Cancel.Should().Be("_Cancel");
        strings.ErrorTitle.Should().Be("FreeP");
        strings.WarningTitle.Should().Be("FreeP");
        strings.InformationTitle.Should().Be("FreeP");
        strings.ConfirmTitle.Should().Be("FreeP");
    }

    [Fact]
    public void ResourceShellStrings_DelegatesEveryLookupToAppCatalog()
    {
        var strings = new ResourceShellStrings(
            ok: () => "_Apply",
            cancel: () => "_Dismiss",
            errorTitle: () => "Workbook Error",
            warningTitle: () => "Workbook Warning",
            informationTitle: () => "Workbook Info",
            confirmTitle: () => "Workbook Confirm",
            createAutomationName: text => $"auto:{text.Replace("_", string.Empty, StringComparison.Ordinal)}");

        strings.Ok.Should().Be("_Apply");
        strings.Cancel.Should().Be("_Dismiss");
        strings.ErrorTitle.Should().Be("Workbook Error");
        strings.WarningTitle.Should().Be("Workbook Warning");
        strings.InformationTitle.Should().Be("Workbook Info");
        strings.ConfirmTitle.Should().Be("Workbook Confirm");
        strings.CreateAutomationName("_Apply").Should().Be("auto:Apply");
    }

    [Fact]
    public void ResourceBackstageStrings_DelegatesGetAndFormatToAppCatalog()
    {
        var strings = new ResourceBackstageStrings(
            key => $"value:{key}",
            (key, args) => $"{key}:{string.Join("|", args)}");

        strings.Get("Backstage_GreetingMorning").Should().Be("value:Backstage_GreetingMorning");
        strings.Format("Backstage_Recent_OpenRecentFileAutomationName", "Book1.xlsx")
            .Should()
            .Be("Backstage_Recent_OpenRecentFileAutomationName:Book1.xlsx");
    }
}
