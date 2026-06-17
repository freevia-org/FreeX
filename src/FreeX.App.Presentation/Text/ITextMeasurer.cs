namespace FreeX.App.Presentation.Text;

/// <summary>
/// Minimal text-measurement abstraction for the portable layout engine. The layout math needs to
/// know how much space a label occupies (axis tick labels, legend entries, data labels, the title)
/// without depending on any platform font stack. Each desktop host supplies a concrete measurer
/// backed by its own renderer; tests supply a deterministic fake.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>
    /// Measures a single (possibly multi-line) run of text rendered with the given font attributes.
    /// Implementations should treat <paramref name="text"/> newlines as line breaks and return the
    /// bounding box of the whole block. A null/empty string returns <see cref="TextSize.Empty"/>.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontFamily">Font family name (e.g. "Calibri"). May be null/empty for a default.</param>
    /// <param name="fontSize">Em size in device-independent units.</param>
    /// <param name="bold">Whether the run is bold.</param>
    /// <param name="italic">Whether the run is italic.</param>
    TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic);
}
