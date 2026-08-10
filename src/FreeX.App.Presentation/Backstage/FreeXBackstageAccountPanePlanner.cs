namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstageTextValue(string? Text = null, string? TextKey = null)
{
    public static FreeXBackstageTextValue Literal(string text) => new(text);

    public static FreeXBackstageTextValue Key(string key) => new(TextKey: key);

    public static string ResolveKey(string? key, Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        return key is null ? string.Empty : Key(key).Resolve(getText);
    }

    public static string? ResolveOptionalKey(
        string? key,
        Func<string, string> getText,
        Func<string, string>? transform = null)
    {
        ArgumentNullException.ThrowIfNull(getText);
        if (key is null)
            return null;

        var resolved = Key(key).Resolve(getText);
        return transform is null ? resolved : transform(resolved);
    }

    public string Resolve(Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        return TextKey is { Length: > 0 } key ? getText(key) : Text ?? string.Empty;
    }
}

public sealed record FreeXBackstageAccountPaneRequest(
    string? UserName,
    string? DeviceName,
    string VersionText,
    bool OptionsAvailable,
    string? CurrentWorkbookPath,
    string? CurrentWorkbookName,
    string TrademarkNotice,
    string LicenseNotice,
    string PrivacyNotice,
    string? LocalOsAccount = null,
    string? OptionsFile = null,
    string? SharingStatus = null,
    string? ExportStatus = null);

public sealed record FreeXBackstageAccountDetailPlan(
    FreeXBackstageAccountDetailId Id,
    string LabelKey,
    FreeXBackstageTextValue Value,
    string ValueAutomationId);

public sealed record FreeXBackstageAccountNoticePlan(
    FreeXBackstageAccountNoticeId Id,
    string AutomationId,
    string Text);

public sealed record FreeXBackstageAccountPanePlan(
    string TitleKey,
    string LocalInfoHeadingKey,
    IReadOnlyList<FreeXBackstageAccountDetailPlan> Details,
    IReadOnlyList<FreeXBackstageAccountActionDefinition> Actions,
    string NoticesHeadingKey,
    IReadOnlyList<FreeXBackstageAccountNoticePlan> Notices);

/// <summary>
/// Builds the renderer-neutral content model for FreeX's Backstage Account pane. Shells localize
/// keys, render controls, and bind actions; this planner owns row ordering and fallback values.
/// </summary>
public static class FreeXBackstageAccountPanePlanner
{
    public static FreeXBackstageAccountPanePlan Build(FreeXBackstageAccountPaneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = new List<FreeXBackstageAccountDetailPlan>();
        foreach (var detail in FreeXBackstagePaneCatalog.BuildAccountDetails())
        {
            details.Add(new FreeXBackstageAccountDetailPlan(
                detail.Id,
                detail.LabelKey,
                ResolveDetailValue(detail.Id, request),
                detail.ValueAutomationId));
        }

        var notices = new List<FreeXBackstageAccountNoticePlan>();
        foreach (var notice in FreeXBackstagePaneCatalog.BuildAccountNotices())
        {
            notices.Add(new FreeXBackstageAccountNoticePlan(
                notice.Id,
                notice.AutomationId,
                ResolveNoticeValue(notice.Id, request)));
        }

        return new FreeXBackstageAccountPanePlan(
            "Backstage_Account_Title",
            "Backstage_Account_LocalInfoHeading",
            details,
            FreeXBackstagePaneCatalog.BuildAccountActions(request.OptionsAvailable),
            "Backstage_Account_NoticesSectionHeader",
            notices);
    }

    private static FreeXBackstageTextValue ResolveDetailValue(
        FreeXBackstageAccountDetailId id,
        FreeXBackstageAccountPaneRequest request) =>
        id switch
        {
            FreeXBackstageAccountDetailId.FreeXUserName => ResolveUserName(request.UserName),
            FreeXBackstageAccountDetailId.LocalOsAccount => ResolveOptionalValue(
                request.LocalOsAccount,
                () => ResolveUserName(request.UserName)),
            FreeXBackstageAccountDetailId.Device => ResolveOptionalLiteral(request.DeviceName),
            FreeXBackstageAccountDetailId.AppVersion => FreeXBackstageTextValue.Literal(request.VersionText),
            FreeXBackstageAccountDetailId.OptionsFile => ResolveOptionalValue(
                request.OptionsFile,
                () => FreeXBackstageTextValue.Key("Backstage_Account_OptionsFileLocalProfile")),
            FreeXBackstageAccountDetailId.CurrentWorkbook => ResolveCurrentWorkbook(request.CurrentWorkbookPath, request.CurrentWorkbookName),
            FreeXBackstageAccountDetailId.Sharing => ResolveOptionalValue(
                request.SharingStatus,
                () => FreeXBackstageTextValue.Key("Backstage_Account_SharingSaveAsRequired")),
            FreeXBackstageAccountDetailId.Export => ResolveOptionalValue(
                request.ExportStatus,
                () => FreeXBackstageTextValue.Key("Backstage_Account_ExportReadyLocal")),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static FreeXBackstageTextValue ResolveOptionalValue(
        string? value,
        Func<FreeXBackstageTextValue> fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback()
            : FreeXBackstageTextValue.Literal(value.Trim());

    private static FreeXBackstageTextValue ResolveUserName(string? userName) =>
        string.IsNullOrWhiteSpace(userName)
            ? FreeXBackstageTextValue.Key("Backstage_Account_UserLocalOnly")
            : FreeXBackstageTextValue.Literal(userName.Trim());

    private static FreeXBackstageTextValue ResolveOptionalLiteral(string? value) =>
        FreeXBackstageTextValue.Literal(string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());

    private static FreeXBackstageTextValue ResolveCurrentWorkbook(
        string? currentWorkbookPath,
        string? currentWorkbookName)
    {
        if (!string.IsNullOrWhiteSpace(currentWorkbookPath))
            return FreeXBackstageTextValue.Literal(Path.GetFileName(currentWorkbookPath));

        if (!string.IsNullOrWhiteSpace(currentWorkbookName))
            return FreeXBackstageTextValue.Literal(currentWorkbookName.Trim());

        return FreeXBackstageTextValue.Key("Backstage_Account_CurrentWorkbookUnsaved");
    }

    private static string ResolveNoticeValue(
        FreeXBackstageAccountNoticeId id,
        FreeXBackstageAccountPaneRequest request) =>
        id switch
        {
            FreeXBackstageAccountNoticeId.Trademark => request.TrademarkNotice,
            FreeXBackstageAccountNoticeId.License => request.LicenseNotice,
            FreeXBackstageAccountNoticeId.Privacy => request.PrivacyNotice,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };
}
