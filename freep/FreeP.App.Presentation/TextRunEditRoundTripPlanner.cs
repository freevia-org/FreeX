using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Preserves DrawingML run state that a native editor cannot represent when the edited run is
/// still text-identical to its matched source run.
/// </summary>
public static class TextRunEditRoundTripPlanner
{
    public static void PreserveSourceOnlyMetadata(Run target, Run? matchedSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (matchedSource is null
            || !string.Equals(target.Text, matchedSource.Text, StringComparison.Ordinal))
        {
            return;
        }

        var source = matchedSource;
        target.Language = source.Language;
        target.AlternateLanguage = source.AlternateLanguage;
        target.Kumimoji = source.Kumimoji;
        target.SmartTagClean = source.SmartTagClean;
        target.NormalizeHeight = source.NormalizeHeight;
        target.CharacterSpacingHundredthsPt = source.CharacterSpacingHundredthsPt;
        target.KerningThresholdHundredthsPt = source.KerningThresholdHundredthsPt;
        target.UnderlineStyleToken = target.Underline ? source.UnderlineStyleToken : null;
        target.StrikeStyleToken = target.Strikethrough ? source.StrikeStyleToken : null;
        target.Dirty = source.Dirty;
        target.NoProof = source.NoProof;
        target.Error = source.Error;
        target.RightToLeft = source.RightToLeft;
        target.Caps = source.Caps;
        target.Field = source.Field;
        target.TextFill = source.TextFill;
        target.TextOutline = source.TextOutline;
        target.TextShadow = source.TextShadow;
        target.TextReflection = source.TextReflection;
        target.TextGlow = source.TextGlow;
        target.TextSoftEdge = source.TextSoftEdge;
        target.Math = source.Math;
    }
}
