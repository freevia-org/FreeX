using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record RunOpenTypeFeaturePlan(
    int? StylisticSet,
    NumberForm? NumberForm,
    NumberSpacing? NumberSpacing,
    IReadOnlyList<string> AvaloniaFeatureSettings)
{
    public bool HasFeatures => StylisticSet is not null || NumberForm is not null || NumberSpacing is not null;
}

/// <summary>
/// Resolves the single-glyph OpenType features that both FreeW compositors can shape coherently.
/// Multi-character ligatures remain outside this plan until Avalonia shapes complete run fragments.
/// </summary>
public static class RunOpenTypeFeaturePlanner
{
    public const int MinStylisticSet = 1;
    public const int MaxStylisticSet = 20;
    private static readonly RunOpenTypeFeaturePlan EmptyPlan = new(
        null,
        null,
        null,
        Array.Empty<string>());

    public static RunOpenTypeFeaturePlan Build(RunFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        var stylisticSet = formatting.StylisticSet is >= MinStylisticSet and <= MaxStylisticSet
            ? formatting.StylisticSet
            : null;
        NumberForm? numberForm = formatting.NumberForm is FreeW.Core.Model.NumberForm.Lining or FreeW.Core.Model.NumberForm.OldStyle
            ? formatting.NumberForm
            : null;
        NumberSpacing? numberSpacing = formatting.NumberSpacing is FreeW.Core.Model.NumberSpacing.Proportional or FreeW.Core.Model.NumberSpacing.Tabular
            ? formatting.NumberSpacing
            : null;
        if (stylisticSet is null && numberForm is null && numberSpacing is null)
            return EmptyPlan;

        var settings = new List<string>(5);

        if (stylisticSet is { } set)
            settings.Add($"ss{set:00}=1");

        switch (numberForm)
        {
            case FreeW.Core.Model.NumberForm.Lining:
                settings.Add("lnum=1");
                settings.Add("onum=0");
                break;
            case FreeW.Core.Model.NumberForm.OldStyle:
                settings.Add("onum=1");
                settings.Add("lnum=0");
                break;
        }

        switch (numberSpacing)
        {
            case FreeW.Core.Model.NumberSpacing.Proportional:
                settings.Add("pnum=1");
                settings.Add("tnum=0");
                break;
            case FreeW.Core.Model.NumberSpacing.Tabular:
                settings.Add("tnum=1");
                settings.Add("pnum=0");
                break;
        }

        return new RunOpenTypeFeaturePlan(stylisticSet, numberForm, numberSpacing, settings);
    }
}
