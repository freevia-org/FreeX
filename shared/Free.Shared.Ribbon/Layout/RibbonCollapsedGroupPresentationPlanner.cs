namespace Free.Shared.Ribbon;

/// <summary>
/// Renderer-neutral decisions for a ribbon group collapsed into one overflow button.
/// Native renderers still own controls, flyouts, focus, and command dispatch.
/// </summary>
public static class RibbonCollapsedGroupPresentationPlanner
{
    public static RibbonCollapsedGroupPresentation CreatePresentation(
        RibbonGroup group,
        ISet<string>? usedKeyTips = null,
        bool includeOverflowSeparators = false)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new RibbonCollapsedGroupPresentation(
            group.Id,
            group.Header,
            DeriveGroupKeyTip(group.Header, usedKeyTips),
            GetRepresentativeIcon(group),
            GetOverflowControls(group, includeOverflowSeparators));
    }

    public static RibbonCollapsedGroupRepresentativeIcon GetRepresentativeIcon(RibbonGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var source = group.Controls.FirstOrDefault(IsRepresentativeIconSource);
        return source?.Icon is { } icon
            ? new RibbonCollapsedGroupRepresentativeIcon(icon, source.CommandId.Value)
            : new RibbonCollapsedGroupRepresentativeIcon(
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                CommandName: null);
    }

    public static IReadOnlyList<RibbonControl> GetOverflowControls(
        RibbonGroup group,
        bool includeSeparators = false)
    {
        ArgumentNullException.ThrowIfNull(group);

        var controls = new List<RibbonControl>(group.Controls.Count);
        foreach (var control in group.Controls)
        {
            switch (control)
            {
                case RibbonRowBreak:
                    continue;
                case RibbonSeparator when includeSeparators:
                    controls.Add(control);
                    continue;
                case RibbonSeparator:
                    continue;
            }

            if (!string.IsNullOrEmpty(control.Label))
                controls.Add(control);
        }

        return controls;
    }

    public static string DeriveGroupKeyTip(string groupName, ISet<string>? usedKeyTips = null)
    {
        var letters = groupName.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        var candidates = new List<string>();
        if (letters.Length >= 2)
        {
            candidates.Add(new string([letters[0], letters[1]]));
            for (var i = 2; i < letters.Length; i++)
                candidates.Add(new string([letters[0], letters[i]]));
        }
        else if (letters.Length == 1)
        {
            candidates.Add(new string([letters[0]]));
        }

        candidates.Add("G");
        for (var index = 1; index <= 9; index++)
            candidates.Add($"G{index}");

        foreach (var candidate in candidates)
        {
            if (usedKeyTips is not null && !usedKeyTips.Add(candidate))
                continue;

            return candidate;
        }

        return "G";
    }

    private static bool IsRepresentativeIconSource(RibbonControl control) =>
        control is not RibbonRowBreak and not RibbonSeparator && control.Icon is not null;
}

public sealed record RibbonCollapsedGroupPresentation(
    string GroupId,
    string Header,
    string KeyTip,
    RibbonCollapsedGroupRepresentativeIcon RepresentativeIcon,
    IReadOnlyList<RibbonControl> OverflowControls);

public readonly record struct RibbonCollapsedGroupRepresentativeIcon(
    RibbonCommandIcon Icon,
    string? CommandName);
