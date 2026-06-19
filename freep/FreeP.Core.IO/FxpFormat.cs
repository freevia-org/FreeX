using System.Text.Json;
using System.Text.Json.Serialization;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Stub <c>.fxp</c> ("Free Presentation") reader/writer. FreeP's real on-disk format will be an OPC/.pptx
/// package; this scaffold uses a small, deterministic JSON document so the host can prove a full
/// Open → model → Save round-trip without pulling in the presentation domain. The serialization is
/// canonical (stable property order via the DTO shape, indented, no BOM), so writing a model and re-writing
/// the model parsed back from it produces byte-identical output — the round-trip invariant the host relies on.
/// </summary>
public static class FxpFormat
{
    /// <summary>The file extension FreeP reads and writes (its own format — never steals .pptx).</summary>
    public const string Extension = ".fxp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Deterministic, ASCII-safe output keeps round-trips byte-stable across machines/cultures.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads a presentation from a <c>.fxp</c> file. Throws on malformed input (the host shows an error).</summary>
    public static Presentation Read(string path)
    {
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<PresentationDto>(stream, SerializerOptions)
            ?? throw new InvalidDataException("The presentation file is empty or not a valid .fxp document.");
        return dto.ToModel();
    }

    /// <summary>Writes a presentation to a <c>.fxp</c> file (canonical JSON; UTF-8, no BOM).</summary>
    public static void Write(Presentation presentation, string path)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var json = JsonSerializer.Serialize(PresentationDto.FromModel(presentation), SerializerOptions);
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Serializes a presentation to its canonical JSON string (used by tests for byte-stability checks).</summary>
    public static string Serialize(Presentation presentation) =>
        JsonSerializer.Serialize(PresentationDto.FromModel(presentation), SerializerOptions);

    // ── On-disk DTOs ─────────────────────────────────────────────────────────────
    // Separate from the model so the wire format is an explicit, stable contract (property order, nullability)
    // rather than whatever the mutable model happens to look like. Keeps the round-trip canonical.

    private sealed record PresentationDto(
        int Version,
        PropertiesDto Properties,
        IReadOnlyList<SlideDto> Slides)
    {
        public const int CurrentVersion = 1;

        public static PresentationDto FromModel(Presentation p) => new(
            CurrentVersion,
            PropertiesDto.FromModel(p.Properties),
            p.Slides.Select(SlideDto.FromModel).ToList());

        public Presentation ToModel()
        {
            var presentation = new Presentation();
            Properties.ApplyTo(presentation.Properties);
            foreach (var slide in Slides)
                presentation.Slides.Add(slide.ToModel());
            return presentation;
        }
    }

    private sealed record PropertiesDto(
        string? Title,
        string? Author,
        string? Subject,
        string? Keywords,
        string? Comments)
    {
        public static PropertiesDto FromModel(PresentationProperties p) =>
            new(p.Title, p.Author, p.Subject, p.Keywords, p.Comments);

        public void ApplyTo(PresentationProperties p)
        {
            p.Title = Title;
            p.Author = Author;
            p.Subject = Subject;
            p.Keywords = Keywords;
            p.Comments = Comments;
        }
    }

    private sealed record SlideDto(string Id, string Title, IReadOnlyList<ShapeDto> Shapes)
    {
        public static SlideDto FromModel(Slide s) =>
            new(s.Id, s.Title, s.Shapes.Select(ShapeDto.FromModel).ToList());

        public Slide ToModel()
        {
            var slide = new Slide { Id = Id, Title = Title };
            foreach (var shape in Shapes)
                slide.Shapes.Add(shape.ToModel());
            return slide;
        }
    }

    private sealed record ShapeDto(string Kind, string Text)
    {
        public static ShapeDto FromModel(SlideShape s) => new(s.Kind, s.Text);

        public SlideShape ToModel() => new() { Kind = Kind, Text = Text };
    }
}
