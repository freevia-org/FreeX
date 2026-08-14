using FreeX.App.Presentation;
using Free.Shared.Localization;
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

public enum HyperlinkDialogTextProfile
{
    Wpf,
    Avalonia
}

public enum HyperlinkDialogFocusTarget
{
    Target
}

public sealed record HyperlinkDialogPlan(
    HyperlinkTargetKind LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark);

public sealed record HyperlinkDialogPrefill(
    HyperlinkTargetKind LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark)
{
    public static HyperlinkDialogPrefill FromCell(Sheet? sheet, CellAddress address)
    {
        const string DefaultTarget = "https://";

        if (sheet is null)
        {
            return new HyperlinkDialogPrefill(
                HyperlinkTargetKind.ExistingFileOrWebPage,
                DefaultTarget,
                "",
                "",
                "");
        }

        var target = sheet.Hyperlinks.TryGetValue(address, out var existingTarget) &&
            !string.IsNullOrWhiteSpace(existingTarget)
            ? existingTarget.Trim()
            : DefaultTarget;
        var metadata = sheet.HyperlinkMetadata.TryGetValue(address, out var existingMetadata)
            ? existingMetadata
            : new HyperlinkMetadata();

        return new HyperlinkDialogPrefill(
            metadata.LinkType,
            target,
            FormatDisplayText(sheet.GetCell(address)?.Value),
            metadata.ScreenTip,
            metadata.Bookmark);
    }

    private static string FormatDisplayText(ScalarValue? value) =>
        SpreadsheetDisplayFormatter.FormatCellValue(value);
}

/// <summary>
/// Normalizes and validates the <em>target text</em> a user types into the spreadsheet Insert
/// Hyperlink dialog, across the four Excel link types in <see cref="HyperlinkTargetKind"/>, and
/// projects the outcome into a <see cref="HyperlinkDialogPlan"/> plus a
/// <see cref="HyperlinkDialogValidationError"/> taxonomy that hosts render through
/// <see cref="ValidationPresentationDescriptor{T}"/>.
/// <para>
/// Cross-app note (assessed 2026-08-15): <c>FreeP.App.Compositor.HyperlinkDialogPlanner</c> shares
/// only this type's <em>name</em>. This planner solves target-text validation: it decides missing
/// address vs. missing document location vs. missing new-document name from the link type, applies
/// an <c>@</c>/dot/whitespace email-address rule, adds and strips the <c>mailto:</c> prefix, and
/// derives default display text — and it owns a five-member error enum plus a Wpf/Avalonia text
/// profile so each shell can phrase the same failure natively. The FreeP planner solves a different
/// problem: it publishes a dialog <em>surface schema</em> (field ids, control kinds, labels,
/// accessible names, automation ids) over FreeP's <c>PresentationDialogSurfacePlan</c>
/// infrastructure, drives a mutable <c>HyperlinkDialogSession</c> view-state machine, and resolves
/// PowerPoint slide targets by id/index. Its taxonomy is two target kinds (Url, Slide) against this
/// one's four, it has no email-address rule, no <c>mailto:</c> normalization, no display-text
/// derivation, no error enum and no text profile, and it emits literal strings rather than
/// <see cref="LocalizedTextDescriptor"/> resources. The one primitive both apps genuinely share —
/// URL scheme allowlisting — is <em>already</em> extracted as
/// <see cref="Free.Shared.AppServices.ExternalUriLauncher.TryCreateAllowedUri"/>, which FreeP calls
/// directly and which FreeX routes every launch through. Ignoring braces and short lines, the two
/// files share exactly one identical line — the <c>public static class</c> declaration. There is no
/// further stable neutral contract to extract; do not merge them.
/// </para>
/// </summary>
public static class HyperlinkDialogPlanner
{
    public const double Width = 560;
    public const double Height = 300;
    public const double MinWidth = 520;
    public const double MinHeight = 300;
    public const double DialogMargin = 16;
    public const double LinkTypeColumnWidth = 170;
    public const double LinkTypeColumnGap = 12;
    public const double LabelColumnWidth = 110;
    public const double FieldHeight = 24;
    public const double FieldBottomMargin = 8;
    public const double ButtonGap = 8;
    public const double SecondaryButtonWidth = 96;
    public const double ActionButtonWidth = 72;
    public const double LinkTypeListHeight = 96;

    public static ValidationPresentationDescriptor<HyperlinkDialogFocusTarget> DescribeValidationError(
        HyperlinkDialogValidationError error,
        HyperlinkDialogTextProfile profile)
    {
        var message = profile == HyperlinkDialogTextProfile.Wpf
            ? error switch
            {
                HyperlinkDialogValidationError.MissingDocumentLocation => LocalizedTextDescriptor.Resource("Hyperlink_EnterValidCellReferenceOrDefinedName"),
                HyperlinkDialogValidationError.MissingEmailAddress => LocalizedTextDescriptor.Resource("Hyperlink_EnterEmailAddress"),
                HyperlinkDialogValidationError.MissingNewDocumentName => LocalizedTextDescriptor.Resource("Hyperlink_EnterNewDocumentName"),
                HyperlinkDialogValidationError.InvalidEmailAddress => LocalizedTextDescriptor.Resource("Hyperlink_EnterValidEmailAddress"),
                _ => LocalizedTextDescriptor.Resource("Hyperlink_EnterAddress")
            }
            : error switch
            {
                HyperlinkDialogValidationError.MissingDocumentLocation => LocalizedTextDescriptor.Literal("Enter a cell reference or defined name."),
                HyperlinkDialogValidationError.MissingEmailAddress => LocalizedTextDescriptor.Literal("Enter an email address."),
                HyperlinkDialogValidationError.MissingNewDocumentName => LocalizedTextDescriptor.Literal("Enter a new document name."),
                HyperlinkDialogValidationError.InvalidEmailAddress => LocalizedTextDescriptor.Literal("Enter a valid email address."),
                _ => LocalizedTextDescriptor.Literal("Enter an address.")
            };
        return new ValidationPresentationDescriptor<HyperlinkDialogFocusTarget>(
            message,
            HyperlinkDialogFocusTarget.Target);
    }

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
