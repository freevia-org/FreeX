namespace FreeW.Core.Model;

/// <summary>
/// The single shared rule for advancing a numbered-list counter across a <see cref="ParagraphFormatting.ListStartOverride"/>
/// restart boundary. A paragraph with a set override restarts the count at that value (never below 1); otherwise the
/// count simply continues from the running total. Both the live document renderer's marker sequencing
/// (<c>DocumentListMarkerSequencePlanner</c> in FreeW.App.Presentation) and any lower-layer numbering consumer (e.g.
/// <see cref="CrossReferences"/>'s "Insert as Paragraph Number" cross-reference resolution) must advance a numbered-list
/// counter through this one rule, so a restart is honoured identically everywhere the number is shown.
/// </summary>
public static class ListRestartCounter
{
    /// <summary>
    /// Returns the next value of a numbered-list counter given its current value and the paragraph's
    /// <see cref="ParagraphFormatting.ListStartOverride"/> (null when the paragraph does not restart the list).
    /// </summary>
    public static int NextCount(int current, int? listStartOverride) =>
        listStartOverride is { } overrideStart ? Math.Max(1, overrideStart) : current + 1;
}
