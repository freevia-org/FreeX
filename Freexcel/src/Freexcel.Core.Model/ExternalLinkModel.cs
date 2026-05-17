namespace Freexcel.Core.Model;

/// <summary>
/// Metadata for an external workbook link package part.
/// </summary>
public sealed class ExternalLinkModel
{
    public string PackagePart { get; set; } = "";
    public string? TargetUri { get; set; }
    public string? TargetMode { get; set; }
}
