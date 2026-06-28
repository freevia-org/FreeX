using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>
/// One allowed-action toggle in the Protect Sheet option list, pairing the Core
/// <see cref="SheetProtectionPermission"/> it maps to with its out-of-the-box default
/// checked state plus the renderer-localized label key. The list of these
/// (see <see cref="SheetProtectionOptions.All"/>) mirrors
/// the ordered checklist the desktop hosts present, so any renderer can bind to it directly
/// without re-deriving the order or defaults.
/// </summary>
/// <param name="Permission">The Core permission this toggle controls.</param>
/// <param name="DefaultEnabled">
/// Whether the toggle starts checked when the dialog opens for an unprotected sheet.
/// </param>
/// <param name="LabelKey">The resource key renderers use for the checklist label.</param>
public sealed record SheetProtectionOption(
    SheetProtectionPermission Permission,
    bool DefaultEnabled,
    string LabelKey);
