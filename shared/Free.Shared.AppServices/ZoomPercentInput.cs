namespace Free.Shared.AppServices;

/// <summary>
/// The shared taxonomy for why a Zoom dialog's custom-percent box could not be turned into a zoom
/// level. Both FreeX's and FreeW's Zoom dialogs classify their custom-percent input with these
/// values and then project them onto their own (localized) message surface, so the *decision* is
/// shared even though the wording is not.
/// </summary>
public enum ZoomPercentInputError
{
    /// <summary>The input parsed cleanly; no error.</summary>
    None,

    /// <summary>The box was empty or whitespace only.</summary>
    Missing,

    /// <summary>The text was not a number in either the current or the invariant culture.</summary>
    NotNumeric,

    /// <summary>The number parsed but fell outside the policy's supported range (reject mode only).</summary>
    OutOfRange,

    /// <summary>The number parsed and was in range, but was not a whole percent.</summary>
    NotWholePercent,
}

/// <summary>
/// How <see cref="ZoomPercentPolicy.TryResolveWholePercent"/> should treat a percent that parses but
/// falls outside the policy's supported range. FreeX's Zoom dialog rejects out-of-range input with a
/// dedicated message ("Zoom must be between 10% and 400%"); FreeW's silently clamps into 50..200%.
/// </summary>
public enum ZoomPercentRangeMode
{
    /// <summary>Report <see cref="ZoomPercentInputError.OutOfRange"/> instead of accepting the value.</summary>
    Reject,

    /// <summary>Clamp the value into the supported range and accept it.</summary>
    Clamp,
}
