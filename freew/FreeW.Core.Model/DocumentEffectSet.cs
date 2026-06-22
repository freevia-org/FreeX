namespace FreeW.Core.Model;

/// <summary>
/// Word-style Design &gt; Document Formatting effect sets. An effect set records the DrawingML
/// <c>a:fmtScheme</c> family used by themed shapes, SmartArt, charts, and WordArt. Text-only documents do
/// not visually change immediately, but the selected scheme is preserved in <see cref="TextDocument.Theme"/>
/// and round-trips through <c>word/theme/theme1.xml</c>.
/// </summary>
public sealed record DocumentEffectSet(
    string Name,
    int LineWidthEmu,
    bool OuterShadow,
    bool SoftEdges)
{
    public static readonly IReadOnlyList<DocumentEffectSet> Catalog =
    [
        new("Office", 6350, OuterShadow: false, SoftEdges: false),
        new("Subtle", 9525, OuterShadow: true, SoftEdges: false),
        new("Moderate", 12700, OuterShadow: true, SoftEdges: true),
        new("Intense", 19050, OuterShadow: true, SoftEdges: true),
    ];

    public static DocumentEffectSet Default => Catalog[0];

    public static DocumentEffectSet? FindByName(string name) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static DocumentEffectSet FromTheme(DocumentTheme theme) =>
        FindByName(theme.EffectSetName) ?? Default;

    public static void Apply(TextDocument doc, DocumentEffectSet effectSet)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(effectSet);

        doc.Theme = doc.Theme with { EffectSetName = effectSet.Name };
    }
}
