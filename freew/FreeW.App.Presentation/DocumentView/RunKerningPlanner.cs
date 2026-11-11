using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Decides whether pair kerning applies to a run, matching Word: kerning is OFF unless the run's
/// effective properties carry <c>w:kern</c> ("Kerning for fonts ... points and above") with a positive
/// threshold and the run's font size reaches it. Text engines that kern by default (WPF, HarfBuzz) must
/// therefore switch kerning off for every other run, or lines measure narrower than Word's and wrap at
/// different words.
/// </summary>
public static class RunKerningPlanner
{
    /// <param name="formatting">The run's effective (style/default-resolved) formatting.</param>
    /// <param name="defaultFontSizePt">Font size used when the formatting carries none.</param>
    public static bool IsKerningEnabled(RunFormatting formatting, double defaultFontSizePt)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        if (formatting.KerningMinSizePt is not { } thresholdPt || thresholdPt <= 0)
            return false;
        var sizePt = formatting.FontSizePt ?? defaultFontSizePt;
        return sizePt >= thresholdPt;
    }
}
