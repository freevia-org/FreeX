using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// r454: a presentation read from a .pptx package, plus any non-fatal damage found while reading it.
/// </summary>
/// <remarks>
/// Deliberately the same shape as FreeX's <c>XlsxLoadResult</c>: the file always opens, and the
/// warnings say which parts could not be read and were replaced with empty ones. Reporting that is
/// the difference between a repair the user can act on and silent data loss they will overwrite --
/// PowerPoint recovers a damaged deck too, and tells you it did.
/// </remarks>
/// <param name="Presentation">The presentation, with any unreadable parts opened blank.</param>
/// <param name="Warnings">
/// One message per part that could not be read. Empty for an undamaged file, which is the case
/// callers should expect: a warning that fires on healthy files trains users to dismiss the one
/// that matters.
/// </param>
public sealed record PptxReadResult(
    Presentation Presentation,
    IReadOnlyList<string> Warnings);
