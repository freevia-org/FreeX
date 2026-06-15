namespace FreeX.Ribbon;

public enum RibbonDiagnosticSeverity { Info, Warning, Error }

public sealed record RibbonDiagnostic(
    string Code,
    RibbonDiagnosticSeverity Severity,
    string Message);

public sealed class RibbonDiagnostics
{
    public IReadOnlyList<RibbonDiagnostic> Items { get; }

    public RibbonDiagnostics(IReadOnlyList<RibbonDiagnostic> items) => Items = items;

    public bool HasErrors => Items.Any(i => i.Severity == RibbonDiagnosticSeverity.Error);

    public static readonly RibbonDiagnostics Empty = new(Array.Empty<RibbonDiagnostic>());
}
