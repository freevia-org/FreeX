using System.Text.Json;
using System.Text.Json.Serialization;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Legacy <c>.fxp</c> ("Free Presentation") reader/writer. The real on-disk format is .pptx; this
/// frozen JSON format exists only so existing tests and host code can exercise Open/Save without
/// the full OPC stack. The serialization is canonical (stable property order via the DTO shape,
/// indented, no BOM), so writing a model and re-writing the model parsed back from it produces
/// byte-identical output — the round-trip invariant the host relies on.
///
/// Wire format contract (Version 1 — frozen):
///   { Version, Properties:{...}, Slides:[{ Id, Title, Shapes:[{Kind, Text}] }] }
///
/// The Title field holds the slide's title text. Shapes lists only non-placeholder content shapes.
/// Placeholders (title, body) are NOT in the Shapes array — they are an internal model concept
/// above what the FXP format knows about.
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

    /// <summary>Reads a presentation from a <c>.fxp</c> file. Throws on malformed input.</summary>
    public static Presentation Read(string path)
    {
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<PresentationDto>(stream, SerializerOptions)
            ?? throw new InvalidDataException("The presentation file is empty or not a valid .fxp document.");

        try
        {
            return dto.ToModel();
        }
        catch (NullReferenceException ex)
        {
            // r453: well-formed JSON that is not a presentation ("{}", or any object missing the
            // members ToModel dereferences) deserialises into a DTO of nulls and then fails deep
            // inside it. The shell shows Exception.Message verbatim
            // (PresentationNativeCommandOutcomePlanner), so the user read "Object reference not set
            // to an instance of an object" for a file this reader already owns an accurate sentence
            // about. Same fix FreeX made for the same reason in r382, down to keeping the original as
            // InnerException so nothing is swallowed.
            throw new InvalidDataException(
                "The presentation file is empty or not a valid .fxp document.", ex);
        }
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
    // The wire format is a frozen contract. The model mapping adapts here.

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
        string? Comments,
        string? LastModifiedBy,
        DateTimeOffset? Created,
        DateTimeOffset? Modified,
        string? Category,
        string? ContentStatus,
        string? Language,
        string? Version)
    {
        public static PropertiesDto FromModel(DocumentProperties p) =>
            new(
                p.Title,
                p.Author,
                p.Subject,
                p.Keywords,
                p.Comments,
                p.LastModifiedBy,
                p.Created,
                p.Modified,
                p.Category,
                p.ContentStatus,
                p.Language,
                p.Version);

        public void ApplyTo(DocumentProperties p)
        {
            p.Title = Title;
            p.Author = Author;
            p.Subject = Subject;
            p.Keywords = Keywords;
            p.Comments = Comments;
            p.LastModifiedBy = LastModifiedBy;
            p.Created = Created;
            p.Modified = Modified;
            p.Category = Category;
            p.ContentStatus = ContentStatus;
            p.Language = Language;
            p.Version = Version;
        }
    }

    private sealed record SlideDto(string Id, string Title, IReadOnlyList<ShapeDto> Shapes)
    {
        public static SlideDto FromModel(Slide s) => new(
            s.Id,
            s.Title,
            // Only serialize non-placeholder content shapes. The title/body placeholders are
            // reconstructed from the Title field and the slide's layout on load.
            s.Shapes
                .Where(shape => shape.Placeholder is null)
                .Select(ShapeDto.FromModel)
                .ToList());

        public Slide ToModel()
        {
            var slide = new Slide { Id = Id, Title = Title };
            foreach (var shape in Shapes)
                slide.Shapes.Add(shape.ToModel());
            return slide;
        }
    }

    /// <summary>
    /// Shape DTO — the wire format stores Kind as a free-form string (legacy contract).
    /// On read, the string is preserved in <see cref="SlideShape.LegacyFxpKind"/> for
    /// byte-stable re-write without having to re-derive the string from the enum.
    /// </summary>
    private sealed record ShapeDto(string Kind, string Text)
    {
        public static ShapeDto FromModel(SlideShape s)
        {
            // Prefer the preserved legacy string for byte stability; derive otherwise.
            var kindString = s.LegacyFxpKind ?? DeriveKindString(s);
            return new(kindString, s.Text);
        }

        private static string DeriveKindString(SlideShape s) => s.Kind switch
        {
            Free.Shared.Drawing.SlideShapeKind.Picture => "picture",
            Free.Shared.Drawing.SlideShapeKind.Group => "group",
            Free.Shared.Drawing.SlideShapeKind.Table => "table",
            Free.Shared.Drawing.SlideShapeKind.Connector => "connector",
            _ => s.AutoShapeKind.ToString().ToLowerInvariant()
        };

        public SlideShape ToModel()
        {
            var shape = new SlideShape
            {
                Kind = ParseSlideShapeKind(Kind),
                AutoShapeKind = ParseAutoShapeKind(Kind),
                LegacyFxpKind = Kind, // Preserve for byte-stable round-trips
            };
            shape.Text = Text;
            return shape;
        }

        private static Free.Shared.Drawing.SlideShapeKind ParseSlideShapeKind(string kind) =>
            kind.ToLowerInvariant() switch
            {
                "picture" => Free.Shared.Drawing.SlideShapeKind.Picture,
                "group" => Free.Shared.Drawing.SlideShapeKind.Group,
                "table" => Free.Shared.Drawing.SlideShapeKind.Table,
                "connector" => Free.Shared.Drawing.SlideShapeKind.Connector,
                _ => Free.Shared.Drawing.SlideShapeKind.AutoShape
            };

        private static Free.Shared.Drawing.DrawingShapeKind ParseAutoShapeKind(string kind) =>
            kind.ToLowerInvariant() switch
            {
                "rectangle" => Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                "ellipse" => Free.Shared.Drawing.DrawingShapeKind.Ellipse,
                "line" => Free.Shared.Drawing.DrawingShapeKind.Line,
                "roundedrectangle" => Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle,
                "triangle" => Free.Shared.Drawing.DrawingShapeKind.Triangle,
                "diamond" => Free.Shared.Drawing.DrawingShapeKind.Diamond,
                _ => Free.Shared.Drawing.DrawingShapeKind.Rectangle
            };
    }
}
