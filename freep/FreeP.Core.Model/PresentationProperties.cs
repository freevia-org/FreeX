namespace FreeP.Core.Model;

/// <summary>
/// FreeP's core document properties, mirroring FreeW's <c>DocumentProperties</c> shape so the shared
/// Backstage Info pane can surface them uniformly across the sister apps. These map onto the same OPC
/// core-properties vocabulary a future <c>.pptx</c> exporter would use (dc:title, dc:creator, ...).
/// </summary>
public sealed class PresentationProperties
{
    /// <summary>dc:title</summary>
    public string? Title { get; set; }

    /// <summary>dc:creator (the presentation's author).</summary>
    public string? Author { get; set; }

    /// <summary>dc:subject</summary>
    public string? Subject { get; set; }

    /// <summary>cp:keywords</summary>
    public string? Keywords { get; set; }

    /// <summary>dc:description (free-form comments).</summary>
    public string? Comments { get; set; }
}
