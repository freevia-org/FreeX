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

    /// <summary>
    /// Header/footer visibility flags from the notes master's own <c>p:hf</c> element
    /// (ppt/notesMasters/notesMaster1.xml). PowerPoint's "Notes and Handouts" tab in the
    /// Header and Footer dialog is presentation-wide — there is no per-notes-slide override in
    /// the file format (<c>p:hf</c> is not a valid child of <c>p:notes</c>) — so this is modeled
    /// once here rather than on <see cref="Slide"/>. Null means the package had no explicit
    /// notes-master <c>p:hf</c>; consumers should fall back to placeholder presence, matching
    /// the null-flags fallback already used for slide-level <see cref="HfFlags"/>.
    /// </summary>
    public HfFlags? NotesHfVisibility { get; set; }

    /// <summary>
    /// Native handout-master placeholder shapes imported from ppt/handoutMasters. The handout
    /// PDF/print exporter uses their authored geometry and header/footer/date/slide-number text
    /// before falling back to its deterministic corner-of-the-page defaults.
    /// </summary>
    public List<SlideShape> HandoutMasterPlaceholders { get; } = new();

    /// <summary>
    /// Original handout-master XML and relationships, retained so a read/write round trip keeps
    /// an authored handout master (custom header/footer text and placement) instead of dropping
    /// the part. Null when the package had none — the writer never synthesizes a handout master,
    /// matching the "only preserve, never invent" behavior of the reader.
    /// </summary>
    public byte[]? HandoutMasterXml { get; set; }
    public byte[]? HandoutMasterRelsXml { get; set; }

    /// <summary>
    /// Header/footer visibility flags from the handout master's own <c>p:hf</c> element
    /// (ppt/handoutMasters/handoutMaster1.xml). Null means the package had no explicit handout
    /// master <c>p:hf</c>; consumers should fall back to <see cref="NotesHfVisibility"/> (the
    /// same "Notes and Handouts" dialog tab drives both surfaces in PowerPoint).
    /// </summary>
    public HfFlags? HandoutHfVisibility { get; set; }

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

    /// <summary>
    /// Whether slideshow playback includes non-placeholder shapes authored on the slide master.
    /// PresentationML defaults this policy to enabled when the showPr attribute is omitted.
    /// </summary>
    public bool ShowMasterShapes { get; set; } = true;

    /// <summary>
    /// Whether PowerPoint shows date, footer, and slide-number placeholders on title slides.
    /// PresentationML omits <c>showSpecialPlsOnTitleSld</c> when this policy is disabled.
    /// </summary>
    public bool ShowSpecialPlaceholdersOnTitleSlide { get; set; }

    /// <summary>Whether slideshow playback honors authored per-slide transition timings.</summary>
    public bool UseSlideTimings { get; set; } = true;

    /// <summary>Whether slideshow playback runs authored shape animations.</summary>
    public bool ShowWithAnimation { get; set; } = true;

    /// <summary>Whether slideshow playback plays authored narration/audio tracks.</summary>
    public bool ShowWithNarration { get; set; } = true;

    /// <summary>Whether slideshow playback loops back to its first slide after the last slide.</summary>
    public bool LoopUntilStopped { get; set; }

    /// <summary>
    /// The authored default presenter pen color from PresentationML <c>p:showPr/p:penClr</c>.
    /// Null means the package did not specify a color and presenter tools use their host defaults.
    /// </summary>
    public ThemeAwareColor? PresenterPenColor { get; set; }

    /// <summary>How PowerPoint presents the slide show window.</summary>
    public PresentationShowType ShowType { get; set; } = PresentationShowType.PresentedBySpeaker;

    /// <summary>Whether a browsed-by-individual show requests a scroll bar.</summary>
    public bool ShowBrowseScrollbar { get; set; } = true;

    /// <summary>
    /// Optional kiosk restart interval from the authored p:kiosk restart value.
    /// PresentationML stores this duration in milliseconds.
    /// </summary>
    public uint? KioskRestartAfterMilliseconds { get; set; }

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
