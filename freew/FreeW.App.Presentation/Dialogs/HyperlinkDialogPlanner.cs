using FreeW.App.Presentation.Links;

namespace FreeW.App.Presentation.Dialogs;

public enum HyperlinkDialogMode
{
    Insert,
    Edit,
}

public sealed record HyperlinkDialogPresentation(
    HyperlinkDialogMode Mode,
    string Title,
    string DisplayLabel,
    string AddressLabel,
    string DisplayPlaceholder,
    string AddressPlaceholder,
    string InitialDisplayText,
    string InitialAddress);

public readonly record struct HyperlinkDialogAcceptance(
    bool IsAccepted,
    string DisplayText,
    string Address,
    HyperlinkTarget Target);

/// <summary>
/// Renderer-neutral presentation and acceptance policy for Insert/Edit Hyperlink.
/// Native dialogs only project these fields; target normalization remains shared.
/// </summary>
public static class HyperlinkDialogPlanner
{
    public static HyperlinkDialogPresentation Build(
        HyperlinkDialogMode mode,
        string? initialDisplayText = null,
        string? initialAddress = null)
    {
        var text = InsertDialogTextResources.Hyperlink;
        return new HyperlinkDialogPresentation(
            mode,
            mode == HyperlinkDialogMode.Edit ? text.EditTitle : text.Title,
            text.DisplayLabel,
            text.AddressLabel,
            text.DisplayPlaceholder,
            text.AddressPlaceholder,
            initialDisplayText ?? string.Empty,
            initialAddress ?? string.Empty);
    }

    public static HyperlinkDialogAcceptance PlanAcceptance(string? displayText, string? address)
    {
        if (!HyperlinkTarget.TryParse(address, out var target))
            return new HyperlinkDialogAcceptance(false, string.Empty, string.Empty, default);

        return new HyperlinkDialogAcceptance(
            true,
            displayText?.Trim() ?? string.Empty,
            target.Address,
            target);
    }
}
