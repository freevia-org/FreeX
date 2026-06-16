namespace FreeW.Core.Model;

public enum StyleType { Paragraph, Character }

/// <summary>
/// A named style (Word's styles.xml). Carries optional run and paragraph formatting that a
/// paragraph/run resolves through, optionally chaining via <see cref="BasedOnStyleId"/>.
/// </summary>
public sealed class DocumentStyle
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public StyleType Type { get; init; } = StyleType.Paragraph;
    public string? BasedOnStyleId { get; init; }
    public RunFormatting Run { get; set; } = RunFormatting.Default;
    public ParagraphFormatting Paragraph { get; set; } = ParagraphFormatting.Default;
}
