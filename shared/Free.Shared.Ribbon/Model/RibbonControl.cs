namespace Free.Shared.Ribbon;

/// <summary>
/// Base for every ribbon control. Structure only — behavior lives in the command
/// registry, keyed by <see cref="CommandId"/>.
/// </summary>
public abstract record RibbonControl(
    RibbonCommandId CommandId,
    string Label)
{
    public string? KeyTip { get; init; }
    public RibbonCommandIcon? Icon { get; init; }
    public RibbonCommandLayoutKind PreferredLayout { get; init; } = RibbonCommandLayoutKind.Medium;
    public string? TooltipTitle { get; init; }
    public string? TooltipDescription { get; init; }
}

public sealed record RibbonButton(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label);

public sealed record RibbonToggleButton(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label);

public sealed record RibbonCheckBox(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label);

public sealed record RibbonLabel(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label);

public enum RibbonComboBoxPresentationKind
{
    Standard,
    Gallery,
}

public enum RibbonComboBoxGalleryPreviewKind
{
    None,
    General,
    Number,
    Currency,
    Accounting,
    ShortDate,
    LongDate,
    Time,
    Percentage,
    Fraction,
    Scientific,
    Text,
    More,
}

/// <summary>
/// A combo-box choice whose stable value is command protocol and whose label is presentation.
/// Gallery metadata is optional so standard combos keep their compact label-only presentation.
/// </summary>
public sealed record RibbonComboBoxChoice(
    string Value,
    string Label,
    string? Description = null,
    RibbonComboBoxGalleryPreviewKind PreviewKind = RibbonComboBoxGalleryPreviewKind.None);

public sealed record RibbonComboBox(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label)
{
    /// <summary>Closed selector and opened-popup presentation contract for the renderer.</summary>
    public RibbonComboBoxPresentationKind PresentationKind { get; init; } = RibbonComboBoxPresentationKind.Standard;

    /// <summary>
    /// Typed choices for semantic/enumerated controls. Renderers display <see cref="RibbonComboBoxChoice.Label"/>
    /// and dispatch or match state with <see cref="RibbonComboBoxChoice.Value"/>.
    /// </summary>
    public IReadOnlyList<RibbonComboBoxChoice> Choices { get; init; } = Array.Empty<RibbonComboBoxChoice>();

    /// <summary>
    /// Legacy values that are both display text and command protocol. Retained for editable and
    /// unmigrated controls.
    /// </summary>
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();

    /// <summary>Explicit width in DIPs (e.g. a narrow font-size box). Null = renderer default.</summary>
    public double? Width { get; init; }
}

public sealed record RibbonSplitButton(RibbonCommandId CommandId, string Label, RibbonMenu Menu)
    : RibbonControl(CommandId, Label);

public enum RibbonDropdownPresentationKind
{
    Standard,
    CellStyleGallery,
}

public sealed record RibbonDropdown(RibbonCommandId CommandId, string Label, RibbonMenu Menu)
    : RibbonControl(CommandId, Label)
{
    public RibbonDropdownPresentationKind PresentationKind { get; init; } =
        RibbonDropdownPresentationKind.Standard;
}

public sealed record RibbonGallery(RibbonCommandId CommandId, string Label)
    : RibbonControl(CommandId, Label);

/// <summary>A thin vertical divider between controls within a group row.</summary>
public sealed record RibbonSeparator()
    : RibbonControl(new RibbonCommandId(""), "");

/// <summary>Starts a new horizontal row within a group (Office-style multi-row groups).</summary>
public sealed record RibbonRowBreak()
    : RibbonControl(new RibbonCommandId(""), "");
