using Free.Shared.Opc;

namespace FreeP.Core.Model;

public enum PresentationRecordingMediaArtifactKind
{
    NarrationAudio,
    CameraVideo,
    NarrationCaption,
    CameraCaption
}

public sealed record PresentationRecordingMediaArtifact(
    PresentationRecordingMediaArtifactKind Kind,
    int SlideIndex,
    string SuggestedFileName,
    string ContentType,
    string PackagePath,
    long ContentLengthBytes,
    string ContentSha256,
    int DurationMs,
    string CapturedByHost,
    string StatusText,
    byte[]? PayloadBytes = null)
{
    public bool HasPayload =>
        PayloadBytes is { Length: > 0 } &&
        !string.IsNullOrWhiteSpace(PackagePath);
}

/// <summary>
/// The root presentation model. Holds the slide list, masters, layouts, theme, and document
/// properties. Designed to support a real .pptx reader/writer: one master, N layouts, M slides,
/// one theme. Editing tools work with mutable lists; the IO layer reads/writes immutably.
/// </summary>
public sealed class Presentation
{
    // ── Slide size ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Slide width in EMU. Default is 16:9 widescreen (12192000 EMU = 10 inches at 914400 EMU/inch).
    /// </summary>
    public long SlideSizeCxEmu { get; set; } = 12192000;

    /// <summary>
    /// Slide height in EMU. Default is 16:9 widescreen (6858000 EMU ≈ 7.5 inches).
    /// </summary>
    public long SlideSizeCyEmu { get; set; } = 6858000;

    public const long DefaultNotesPageSizeCxEmu = 6858000;
    public const long DefaultNotesPageSizeCyEmu = 9144000;

    /// <summary>
    /// Notes-page canvas width in EMU, from presentation.xml p:notesSz.
    /// </summary>
    public long NotesPageSizeCxEmu { get; set; } = DefaultNotesPageSizeCxEmu;

    /// <summary>
    /// Notes-page canvas height in EMU, from presentation.xml p:notesSz.
    /// </summary>
    public long NotesPageSizeCyEmu { get; set; } = DefaultNotesPageSizeCyEmu;

    /// <summary>
    /// Native notes-master placeholder shapes imported from ppt/notesMasters.  The notes-page
    /// planner uses their authored geometry before falling back to its deterministic defaults.
    /// </summary>
    public List<SlideShape> NotesMasterPlaceholders { get; } = new();

    /// <summary>
    /// Notes-master text styles from p:notesMaster/p:notesStyle.  Null means the package had no
    /// readable notes master styles and consumers should use their existing defaults.
    /// </summary>
    public MasterTextStyles? NotesMasterTextStyles { get; set; }

    /// <summary>
    /// Original notes-master XML and relationships, retained so a read/write round trip does not
    /// replace an authored notes master with the writer's minimal fallback part.
    /// </summary>
    public byte[]? NotesMasterXml { get; set; }
    public byte[]? NotesMasterRelsXml { get; set; }

    // ── Content ───────────────────────────────────────────────────────────────────

    /// <summary>Slides, in presentation order.</summary>
    public List<Slide> Slides { get; } = new();

    /// <summary>Slide layouts referenced by the slides (keyed by SlideLayout.Id).</summary>
    public List<SlideLayout> Layouts { get; } = new();

    /// <summary>Slide masters (one per master in the package, keyed by SlideMaster.Id).</summary>
    public List<SlideMaster> Masters { get; } = new();

    /// <summary>The presentation theme (color + font schemes).</summary>
    public PresentationTheme Theme { get; set; } = PresentationTheme.CreateDefault();

    /// <summary>
    /// The native Office package family. This is kept separate from the editable
    /// presentation model so open/save can preserve macro-enabled, template, and
    /// slide-show package identity without executing or interpreting VBA.
    /// </summary>
    public PresentationPackageKind PackageKind { get; set; } = PresentationPackageKind.Presentation;

    /// <summary>
    /// Whether PowerPoint should show media user-interface controls during a slide show.
    /// The PresentationML default is enabled; the value is serialized only when disabled.
    /// </summary>
    public bool ShowMediaControls { get; set; } = true;

    /// <summary>Whether slideshow playback honors authored per-slide transition timings.</summary>
    public bool UseSlideTimings { get; set; } = true;

    /// <summary>Whether slideshow playback runs authored shape animations.</summary>
    public bool ShowWithAnimation { get; set; } = true;

    /// <summary>Whether slideshow playback loops back to its first slide after the last slide.</summary>
    public bool LoopUntilStopped { get; set; }

    /// <summary>How PowerPoint presents the slide show window.</summary>
    public PresentationShowType ShowType { get; set; } = PresentationShowType.PresentedBySpeaker;

    /// <summary>Whether a browsed-by-individual show requests a scroll bar.</summary>
    public bool ShowBrowseScrollbar { get; set; } = true;

    /// <summary>
    /// Optional kiosk restart interval from the authored p:kiosk restart value.
    /// PresentationML stores this duration in milliseconds.
    /// </summary>
    public uint? KioskRestartAfterMilliseconds { get; set; }

    /// <summary>Compatibility alias for the initial misnamed projection.</summary>
    [Obsolete("Use KioskRestartAfterMilliseconds; PresentationML stores restart in milliseconds.")]
    public uint? KioskRestartAfterMinutes
    {
        get => KioskRestartAfterMilliseconds;
        set => KioskRestartAfterMilliseconds = value;
    }

    /// <summary>Core document properties (title, author, subject, …).</summary>
    public DocumentProperties Properties { get; } = new();

    /// <summary>
    /// Original package entries captured by the PPTX reader so the writer can retain
    /// non-modeled parts and relationships that FreeP does not edit yet.
    /// </summary>
    public PptxPackageSnapshot? PackageSnapshot { get; set; }

    /// <summary>
    /// Authored package/document-level OMML defaults, when the PresentationML
    /// package exposes a related settings part. Normal PPTX files generally do
    /// not expose this source, so null is the correct value in that case.
    /// </summary>
    public OmmlMathProperties? DocumentMathProperties { get; set; }

    // ── Sections ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Named slide sections in presentation order. Each section groups a run of slides by
    /// their sldId integer values. Empty when the presentation has no sections.
    /// Stored in ppt/presentation.xml inside a p14:sectionLst extension.
    /// </summary>
    public List<PresentationSection> Sections { get; } = new();

    /// <summary>
    /// Named custom slide shows in presentation order. Each show stores an ordered list of
    /// <see cref="Slide.Id"/> values and is serialized as p:custShowLst in ppt/presentation.xml.
    /// </summary>
    public List<PresentationCustomShow> CustomShows { get; } = new();

    /// <summary>
    /// Package-ready presenter-recording media descriptors captured by the shared slideshow
    /// recording backend contract. The actual media bytes can be authored later; this manifest
    /// keeps deterministic metadata with the presentation instead of leaving it in host state.
    /// </summary>
    public List<PresentationRecordingMediaArtifact> RecordingMediaArtifacts { get; } = new();

    // ── Factory ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an empty presentation seeded with one blank title slide, one default master,
    /// one blank layout, and the default Office theme.
    /// </summary>
    public static Presentation CreateEmpty()
    {
        var presentation = new Presentation();

        var master = new SlideMaster { Id = "rId1" };
        presentation.Masters.Add(master);

        var layout = new SlideLayout
        {
            Id = "rId1",
            Name = "Title Slide",
            LayoutType = SlideLayoutType.Title,
            MasterId = master.Id
        };
        presentation.Layouts.Add(layout);

        var slide = new Slide { LayoutId = layout.Id };
        slide.Title = "Slide 1";
        presentation.Slides.Add(slide);

        return presentation;
    }
}
