using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeX.ParityCompare.Core;

/// <summary>
/// The manifest each shell's <c>--parity-capture &lt;dir&gt;</c> mode writes to
/// <c>&lt;dir&gt;/manifest.json</c>. Shape per the capture contract:
/// <c>{ "platform","shell","surfaces":[{"id","kind","png","captured","note","width","height",
/// "evidenceProvenance"}] }</c>.
/// </summary>
public sealed class CaptureManifest
{
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";

    [JsonPropertyName("shell")]
    public string Shell { get; set; } = "";

    [JsonPropertyName("surfaces")]
    public List<CapturedSurface> Surfaces { get; set; } = new();

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parse a manifest from JSON text. Throws on malformed JSON.</summary>
    public static CaptureManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<CaptureManifest>(json, ReadOptions)
            ?? throw new FormatException("manifest.json deserialized to null");
        manifest.Surfaces ??= new List<CapturedSurface>();
        return manifest;
    }

    /// <summary>Load a manifest from a <c>manifest.json</c> file path.</summary>
    public static CaptureManifest Load(string path) => Parse(File.ReadAllText(path));
}

/// <summary>One captured surface entry in a <see cref="CaptureManifest"/>.</summary>
public sealed class CapturedSurface
{
    /// <summary>Stable cross-shell id, e.g. <c>tab.Home</c>, <c>grid.demo</c>, <c>dialog.FormatCells</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Surface family token — derived from <see cref="Id"/> prefix when absent.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>PNG file name (relative to the manifest dir) or path.</summary>
    [JsonPropertyName("png")]
    public string? Png { get; set; }

    /// <summary>Whether the shell actually produced an image for this surface.</summary>
    [JsonPropertyName("captured")]
    public bool Captured { get; set; }

    /// <summary>Optional human note (e.g. why a surface was skipped).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Optional rendered pixel width. Fixed-size contracts use this to fail closed on clipping.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>Optional rendered pixel height. Fixed-size contracts use this to fail closed on clipping.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>Capture origin used by fail-closed contracts to reject reconstructed evidence.</summary>
    [JsonPropertyName("evidenceProvenance")]
    public string? EvidenceProvenance { get; set; }

    /// <summary>Native root screenshot from which a physical popup frame was cropped.</summary>
    [JsonPropertyName("sourcePng")]
    public string? SourcePng { get; set; }

    /// <summary>Machine-readable X11 popup-window and crop geometry evidence.</summary>
    [JsonPropertyName("geometryEvidence")]
    public string? GeometryEvidence { get; set; }

    [JsonPropertyName("sourceX")]
    public int? SourceX { get; set; }

    [JsonPropertyName("sourceY")]
    public int? SourceY { get; set; }

    [JsonPropertyName("sourceWidth")]
    public int? SourceWidth { get; set; }

    [JsonPropertyName("sourceHeight")]
    public int? SourceHeight { get; set; }
}
