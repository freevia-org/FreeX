using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum HyperlinkDialogValidationError
{
    None,
    MissingAddress,
    MissingNewDocumentName,
    MissingDocumentLocation,
    MissingEmailAddress,
    InvalidEmailAddress
}

public sealed record HyperlinkDialogPlan(
    HyperlinkTargetKind LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark);

public static class HyperlinkDialogPlanner
{
    public static HyperlinkDialogPlan Plan(
        string target,
        string? displayText,
        HyperlinkTargetKind linkType = HyperlinkTargetKind.ExistingFileOrWebPage,
        string? screenTip = "",
        string? bookmark = "")
    {
        var trimmedTarget = target.Trim();
        var normalizedTarget = NormalizeTargetForLinkType(trimmedTarget, linkType);
        var normalizedDisplay = string.IsNullOrWhiteSpace(displayText)
            ? CreateDefaultDisplayText(trimmedTarget, linkType)
            : displayText.Trim();
        return new HyperlinkDialogPlan(
            linkType,
            normalizedTarget,
            normalizedDisplay,
            (screenTip ?? "").Trim(),
            (bookmark ?? "").Trim());
    }

    public static bool TryPlan(
        string? target,
        string? displayText,
        HyperlinkTargetKind linkType,
        string? screenTip,
        string? bookmark,
        out HyperlinkDialogPlan plan,
        out HyperlinkDialogValidationError error)
    {
        plan = Plan(target ?? "", displayText, linkType, screenTip, bookmark);
        if (string.IsNullOrWhiteSpace(plan.Target))
        {
            error = linkType switch
            {
                HyperlinkTargetKind.PlaceInThisDocument => HyperlinkDialogValidationError.MissingDocumentLocation,
                HyperlinkTargetKind.EmailAddress => HyperlinkDialogValidationError.MissingEmailAddress,
                HyperlinkTargetKind.CreateNewDocument => HyperlinkDialogValidationError.MissingNewDocumentName,
                _ => HyperlinkDialogValidationError.MissingAddress
            };
            return false;
        }

        if (linkType == HyperlinkTargetKind.EmailAddress && !IsValidEmailAddressTarget(plan.Target))
        {
            error = HyperlinkDialogValidationError.InvalidEmailAddress;
            return false;
        }

        error = HyperlinkDialogValidationError.None;
        return true;
    }

    public static bool IsValidEmailAddressTarget(string target)
    {
        var address = target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? target["mailto:".Length..]
            : target;
        return address.IndexOf('@') > 0 &&
            address.IndexOf('@') == address.LastIndexOf('@') &&
            address.LastIndexOf('.') > address.IndexOf('@') + 1 &&
            address.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }

    private static string NormalizeTargetForLinkType(string target, HyperlinkTargetKind linkType)
    {
        if (linkType != HyperlinkTargetKind.EmailAddress ||
            target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(target))
            return target;

        return "mailto:" + target;
    }

    private static string CreateDefaultDisplayText(string target, HyperlinkTargetKind linkType)
    {
        if (linkType != HyperlinkTargetKind.EmailAddress ||
            !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return target;

        var address = target["mailto:".Length..];
        var queryStart = address.IndexOf('?', StringComparison.Ordinal);
        return queryStart < 0 ? address : address[..queryStart];
    }
}
