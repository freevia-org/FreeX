using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageAccountPanePlannerTests
{
    [Fact]
    public void Build_ProducesAccountPaneRowsActionsAndNotices()
    {
        var plan = FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            UserName: " quinn ",
            DeviceName: " FREEX-PC ",
            VersionText: "Version 1.2.3",
            OptionsAvailable: true,
            CurrentWorkbookPath: @"C:\Workbooks\Budget.xlsx",
            CurrentWorkbookName: "Ignored.xlsx",
            TrademarkNotice: "Trademark",
            LicenseNotice: "License",
            PrivacyNotice: "Privacy"));

        plan.TitleKey.Should().Be("Backstage_Account_Title");
        plan.LocalInfoHeadingKey.Should().Be("Backstage_Account_LocalInfoHeading");
        plan.NoticesHeadingKey.Should().Be("Backstage_Account_NoticesSectionHeader");
        plan.Details.Select(detail => detail.Id).Should().Equal(
            FreeXBackstageAccountDetailId.FreeXUserName,
            FreeXBackstageAccountDetailId.LocalOsAccount,
            FreeXBackstageAccountDetailId.Device,
            FreeXBackstageAccountDetailId.AppVersion,
            FreeXBackstageAccountDetailId.OptionsFile,
            FreeXBackstageAccountDetailId.CurrentWorkbook,
            FreeXBackstageAccountDetailId.Sharing,
            FreeXBackstageAccountDetailId.Export);
        TextFor(plan, FreeXBackstageAccountDetailId.FreeXUserName).Should().Be("quinn");
        TextFor(plan, FreeXBackstageAccountDetailId.LocalOsAccount).Should().Be("quinn");
        TextFor(plan, FreeXBackstageAccountDetailId.Device).Should().Be("FREEX-PC");
        TextFor(plan, FreeXBackstageAccountDetailId.AppVersion).Should().Be("Version 1.2.3");
        TextFor(plan, FreeXBackstageAccountDetailId.CurrentWorkbook).Should().Be("Budget.xlsx");
        KeyFor(plan, FreeXBackstageAccountDetailId.OptionsFile).Should().Be("Backstage_Account_OptionsFileLocalProfile");
        KeyFor(plan, FreeXBackstageAccountDetailId.Sharing).Should().Be("Backstage_Account_SharingSaveAsRequired");
        KeyFor(plan, FreeXBackstageAccountDetailId.Export).Should().Be("Backstage_Account_ExportReadyLocal");

        plan.Actions.Select(action => action.Id).Should().Equal(
            FreeXBackstageAccountActionId.Options,
            FreeXBackstageAccountActionId.LegalNotices);
        plan.Notices.Select(notice => (notice.Id, notice.Text, notice.AutomationId)).Should().Equal(
            (FreeXBackstageAccountNoticeId.Trademark, "Trademark", "BackstageAccountTrademark"),
            (FreeXBackstageAccountNoticeId.License, "License", "BackstageAccountLicense"),
            (FreeXBackstageAccountNoticeId.Privacy, "Privacy", "BackstageAccountPrivacy"));
    }

    [Fact]
    public void Build_UsesFallbackKeysForBlankAccountAndWorkbookValues()
    {
        var plan = FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            UserName: " ",
            DeviceName: null,
            VersionText: "Version 1.2.3",
            OptionsAvailable: false,
            CurrentWorkbookPath: null,
            CurrentWorkbookName: " ",
            TrademarkNotice: "Trademark",
            LicenseNotice: "License",
            PrivacyNotice: "Privacy"));

        KeyFor(plan, FreeXBackstageAccountDetailId.FreeXUserName).Should().Be("Backstage_Account_UserLocalOnly");
        KeyFor(plan, FreeXBackstageAccountDetailId.LocalOsAccount).Should().Be("Backstage_Account_UserLocalOnly");
        TextFor(plan, FreeXBackstageAccountDetailId.Device).Should().BeEmpty();
        KeyFor(plan, FreeXBackstageAccountDetailId.CurrentWorkbook).Should().Be("Backstage_Account_CurrentWorkbookUnsaved");
        plan.Actions.Select(action => action.Id).Should().Equal(FreeXBackstageAccountActionId.LegalNotices);
    }

    private static string? TextFor(
        FreeXBackstageAccountPanePlan plan,
        FreeXBackstageAccountDetailId id) =>
        Detail(plan, id).Value.Text;

    private static string? KeyFor(
        FreeXBackstageAccountPanePlan plan,
        FreeXBackstageAccountDetailId id) =>
        Detail(plan, id).Value.TextKey;

    private static FreeXBackstageAccountDetailPlan Detail(
        FreeXBackstageAccountPanePlan plan,
        FreeXBackstageAccountDetailId id) =>
        plan.Details.Single(detail => detail.Id == id);
}
