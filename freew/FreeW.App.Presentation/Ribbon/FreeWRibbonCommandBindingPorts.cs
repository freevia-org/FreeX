using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public enum FreeWRibbonCommandGroup
{
    Clipboard,
    Font,
    ParagraphAndStyles,
    Insert,
    Table,
    Picture,
    Drawing,
    ChartAndSmartArt,
    LayoutAndDesign,
    References,
    Mailings,
    Review,
    View,
    Help,
    Other,
}

public sealed record FreeWRibbonCommandBuildResult(
    RibbonCommandRegistry Registry,
    IReadOnlyDictionary<FreeWRibbonCommandGroup, IReadOnlyList<RibbonCommandId>> CommandGroups,
    IReadOnlyList<RibbonCommandId> AdapterCommandIds)
{
    public IReadOnlyList<RibbonCommandId> CanonicalCommandIds =>
        CommandGroups.Values.SelectMany(static commands => commands).ToArray();
}

/// <summary>
/// Renderer-neutral boundary between canonical FreeW actions and native command implementations.
/// Renderers bind native editor/dialog commands or simple callback ports; presentation owns the
/// final command-id registration pass and grouped inventory.
/// </summary>
public sealed class FreeWRibbonCommandBindingPorts : IRibbonCommandRegistry
{
    private readonly Dictionary<FreeWRibbonCommandAction, IRibbonCommand> _canonical = new();
    private readonly Dictionary<RibbonCommandId, IRibbonCommand> _adapterCommands = new();

    public IRibbonCommand Bind(FreeWRibbonCommandAction action, IRibbonCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _canonical[action] = command;
        return command;
    }

    public IRibbonCommand BindAction(
        FreeWRibbonCommandAction action,
        Action execute,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Bind(action, new CallbackPortCommand(
            _ => execute(),
            isEnabled is null ? null : () => new RibbonCommandState(IsEnabled: isEnabled()),
            prepareExecution));
    }

    public IRibbonCommand BindValue(
        FreeWRibbonCommandAction action,
        Action<string?> execute,
        Func<bool>? isEnabled = null,
        Func<string?>? getValue = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Bind(action, new CallbackPortCommand(
            context => execute(context.SelectedValue),
            isEnabled is null && getValue is null
                ? null
                : () => new RibbonCommandState(
                    IsEnabled: isEnabled?.Invoke() ?? true,
                    Value: getValue?.Invoke()),
            prepareExecution));
    }

    public IRibbonStatefulCommand BindToggle(
        FreeWRibbonCommandAction action,
        Action toggle,
        Func<bool> isChecked,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        ArgumentNullException.ThrowIfNull(isChecked);
        var command = new CallbackPortCommand(
            _ => toggle(),
            () => new RibbonCommandState(
                IsEnabled: isEnabled?.Invoke() ?? true,
                IsChecked: isChecked()),
            prepareExecution);
        Bind(action, command);
        return command;
    }

    /// <summary>
    /// Registers renderer-only aliases, palette entries, and dynamically generated gallery commands.
    /// Canonical <see cref="FreeWRibbonCommandAction"/> routes must use <see cref="Bind"/>.
    /// </summary>
    public void Register(RibbonCommandId id, IRibbonCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _adapterCommands[id] = command;
    }

    public bool TryGet(RibbonCommandId id, out IRibbonCommand? command)
    {
        if (_adapterCommands.TryGetValue(id, out command))
            return true;

        var route = FreeWRibbonCommandWorkflow.Routes.FirstOrDefault(candidate => candidate.CommandId == id);
        return route is not null && _canonical.TryGetValue(route.Action, out command);
    }

    public FreeWRibbonCommandBuildResult Build() => FreeWRibbonExecutionProfile.Build(this);

    internal IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> CanonicalBindings => _canonical;

    internal IReadOnlyDictionary<RibbonCommandId, IRibbonCommand> AdapterBindings => _adapterCommands;

    private sealed class CallbackPortCommand(
        Action<RibbonCommandContext> execute,
        Func<RibbonCommandState>? getState,
        Action? prepareExecution) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (getState?.Invoke().IsEnabled == false)
                return;

            prepareExecution?.Invoke();
            execute(context);
        }

        public RibbonCommandState GetState() => getState?.Invoke() ?? RibbonCommandState.Default;
    }
}

public static class FreeWRibbonCommandBindingExtensions
{
    public static IRibbonCommand Bind(
        this IRibbonCommandRegistry registry,
        FreeWRibbonCommandAction action,
        IRibbonCommand command)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry is FreeWRibbonCommandBindingPorts bindings
            ? bindings.Bind(action, command)
            : BindIntoRegistry(registry, action, command);
    }

    public static IRibbonCommand BindAction(
        this IRibbonCommandRegistry registry,
        FreeWRibbonCommandAction action,
        Action execute,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry is FreeWRibbonCommandBindingPorts bindings)
            return bindings.BindAction(action, execute, isEnabled, prepareExecution);

        return FreeWRibbonCommandWorkflow.RegisterAction(
            registry,
            action,
            execute,
            isEnabled,
            prepareExecution);
    }

    public static IRibbonCommand BindValue(
        this IRibbonCommandRegistry registry,
        FreeWRibbonCommandAction action,
        Action<string?> execute,
        Func<bool>? isEnabled = null,
        Func<string?>? getValue = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry is FreeWRibbonCommandBindingPorts bindings)
        {
            return bindings.BindValue(
                action,
                execute,
                isEnabled,
                getValue,
                prepareExecution);
        }

        return FreeWRibbonCommandWorkflow.RegisterValue(
            registry,
            action,
            execute,
            isEnabled,
            getValue,
            prepareExecution);
    }

    public static IRibbonStatefulCommand BindToggle(
        this IRibbonCommandRegistry registry,
        FreeWRibbonCommandAction action,
        Action toggle,
        Func<bool> isChecked,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry is FreeWRibbonCommandBindingPorts bindings)
        {
            return bindings.BindToggle(
                action,
                toggle,
                isChecked,
                isEnabled,
                prepareExecution);
        }

        return FreeWRibbonCommandWorkflow.RegisterToggle(
            registry,
            action,
            toggle,
            isChecked,
            isEnabled,
            prepareExecution);
    }

    private static IRibbonCommand BindIntoRegistry(
        IRibbonCommandRegistry registry,
        FreeWRibbonCommandAction action,
        IRibbonCommand command)
    {
        FreeWRibbonCommandWorkflow.Register(registry, action, command);
        return command;
    }
}

internal static class FreeWRibbonCommandGroupCatalog
{
    private static readonly HashSet<FreeWRibbonCommandAction> ClipboardActions =
    [
        FreeWRibbonCommandAction.Copy,
        FreeWRibbonCommandAction.Cut,
        FreeWRibbonCommandAction.Find,
        FreeWRibbonCommandAction.FormatPainter,
        FreeWRibbonCommandAction.Paste,
        FreeWRibbonCommandAction.PasteMerge,
        FreeWRibbonCommandAction.PastePlain,
        FreeWRibbonCommandAction.PasteSpecial,
        FreeWRibbonCommandAction.Redo,
        FreeWRibbonCommandAction.Replace,
        FreeWRibbonCommandAction.Select,
        FreeWRibbonCommandAction.Undo,
    ];

    private static readonly HashSet<FreeWRibbonCommandAction> ViewActions =
    [
        FreeWRibbonCommandAction.ReviewingPane,
        FreeWRibbonCommandAction.ShowComments,
        FreeWRibbonCommandAction.ShowNotes,
    ];

    private static readonly HashSet<FreeWRibbonCommandAction> HelpActions =
    [
        FreeWRibbonCommandAction.About,
        FreeWRibbonCommandAction.CheckUpdates,
        FreeWRibbonCommandAction.CopyDiagnostics,
        FreeWRibbonCommandAction.Feedback,
        FreeWRibbonCommandAction.HelpOnline,
        FreeWRibbonCommandAction.LegalNotices,
    ];

    public static FreeWRibbonCommandGroup Resolve(FreeWRibbonCommandAction action)
    {
        if (ClipboardActions.Contains(action))
            return FreeWRibbonCommandGroup.Clipboard;
        if (ViewActions.Contains(action))
            return FreeWRibbonCommandGroup.View;
        if (HelpActions.Contains(action))
            return FreeWRibbonCommandGroup.Help;

        var name = action.ToString();
        if (name.StartsWith("Image", StringComparison.Ordinal))
            return FreeWRibbonCommandGroup.Picture;
        if (name.StartsWith("Shape", StringComparison.Ordinal) ||
            name.StartsWith("Object", StringComparison.Ordinal) ||
            name.StartsWith("Wordart", StringComparison.Ordinal))
            return FreeWRibbonCommandGroup.Drawing;
        if (name.StartsWith("Chart", StringComparison.Ordinal) ||
            name.StartsWith("Smartart", StringComparison.Ordinal))
            return FreeWRibbonCommandGroup.ChartAndSmartArt;
        if (name.StartsWith("Table", StringComparison.Ordinal) ||
            name.StartsWith("Cell", StringComparison.Ordinal) ||
            action is FreeWRibbonCommandAction.DrawTable or
                FreeWRibbonCommandAction.Eraser or
                FreeWRibbonCommandAction.SplitTable or
                FreeWRibbonCommandAction.TextToTable)
            return FreeWRibbonCommandGroup.Table;
        if (name.StartsWith("Merge", StringComparison.Ordinal) ||
            name.StartsWith("StartMailMerge", StringComparison.Ordinal))
            return FreeWRibbonCommandGroup.Mailings;
        if (IsReferenceAction(name))
            return FreeWRibbonCommandGroup.References;
        if (IsReviewAction(name))
            return FreeWRibbonCommandGroup.Review;
        if (IsFontAction(action))
            return FreeWRibbonCommandGroup.Font;
        if (IsParagraphOrStyleAction(name))
            return FreeWRibbonCommandGroup.ParagraphAndStyles;
        if (IsLayoutOrDesignAction(name))
            return FreeWRibbonCommandGroup.LayoutAndDesign;
        if (IsInsertAction(name))
            return FreeWRibbonCommandGroup.Insert;
        return FreeWRibbonCommandGroup.Other;
    }

    private static bool IsReferenceAction(string name) =>
        name.StartsWith("Bibliography", StringComparison.Ordinal) ||
        name.StartsWith("Bookmark", StringComparison.Ordinal) ||
        name.StartsWith("Caption", StringComparison.Ordinal) ||
        name.StartsWith("Citation", StringComparison.Ordinal) ||
        name.StartsWith("CrossReference", StringComparison.Ordinal) ||
        name.StartsWith("Endnote", StringComparison.Ordinal) ||
        name.StartsWith("Footnote", StringComparison.Ordinal) ||
        name.StartsWith("Index", StringComparison.Ordinal) ||
        name.StartsWith("InsertCaption", StringComparison.Ordinal) ||
        name.StartsWith("MarkCitation", StringComparison.Ordinal) ||
        name.StartsWith("MarkIndex", StringComparison.Ordinal) ||
        name.StartsWith("NextEndnote", StringComparison.Ordinal) ||
        name.StartsWith("NextFootnote", StringComparison.Ordinal) ||
        name.StartsWith("TableOfAuthorities", StringComparison.Ordinal) ||
        name.StartsWith("Toc", StringComparison.Ordinal) ||
        name.StartsWith("Tof", StringComparison.Ordinal);

    private static bool IsReviewAction(string name) =>
        name.Contains("Comment", StringComparison.Ordinal) ||
        name.Contains("Change", StringComparison.Ordinal) ||
        name.StartsWith("Accept", StringComparison.Ordinal) ||
        name.StartsWith("Reject", StringComparison.Ordinal) ||
        name.StartsWith("Compare", StringComparison.Ordinal) ||
        name.StartsWith("Combine", StringComparison.Ordinal) ||
        name.StartsWith("Inspect", StringComparison.Ordinal) ||
        name.StartsWith("CheckAccessibility", StringComparison.Ordinal) ||
        name.StartsWith("MarkAsFinal", StringComparison.Ordinal) ||
        name.StartsWith("RestrictEditing", StringComparison.Ordinal) ||
        name.StartsWith("Spellcheck", StringComparison.Ordinal) ||
        name.StartsWith("AddToDictionary", StringComparison.Ordinal) ||
        name.StartsWith("SetProofingLanguage", StringComparison.Ordinal) ||
        name.StartsWith("Thesaurus", StringComparison.Ordinal) ||
        name.StartsWith("ReadAloud", StringComparison.Ordinal);

    private static bool IsFontAction(FreeWRibbonCommandAction action) => action is
        FreeWRibbonCommandAction.Allcaps or
        FreeWRibbonCommandAction.Bold or
        FreeWRibbonCommandAction.ChangeCase or
        FreeWRibbonCommandAction.CharBorder or
        FreeWRibbonCommandAction.CharShading or
        FreeWRibbonCommandAction.ClearFormatting or
        FreeWRibbonCommandAction.FontColor or
        FreeWRibbonCommandAction.FontDialog or
        FreeWRibbonCommandAction.FontFamily or
        FreeWRibbonCommandAction.FontSize or
        FreeWRibbonCommandAction.GrowFont or
        FreeWRibbonCommandAction.Highlight or
        FreeWRibbonCommandAction.Italic or
        FreeWRibbonCommandAction.ShrinkFont or
        FreeWRibbonCommandAction.Smallcaps or
        FreeWRibbonCommandAction.Strikethrough or
        FreeWRibbonCommandAction.Subscript or
        FreeWRibbonCommandAction.Superscript or
        FreeWRibbonCommandAction.Underline;

    private static bool IsParagraphOrStyleAction(string name) =>
        name.StartsWith("Align", StringComparison.Ordinal) ||
        name.StartsWith("Bullets", StringComparison.Ordinal) ||
        name.StartsWith("Indent", StringComparison.Ordinal) ||
        name.StartsWith("Keep", StringComparison.Ordinal) ||
        name.StartsWith("LineSpacing", StringComparison.Ordinal) ||
        name.StartsWith("Multilevel", StringComparison.Ordinal) ||
        name.StartsWith("Numbering", StringComparison.Ordinal) ||
        name.StartsWith("Para", StringComparison.Ordinal) ||
        name.StartsWith("Paragraph", StringComparison.Ordinal) ||
        name.StartsWith("Sort", StringComparison.Ordinal) ||
        name.StartsWith("Space", StringComparison.Ordinal) ||
        name.StartsWith("Style", StringComparison.Ordinal) ||
        name.StartsWith("Tabs", StringComparison.Ordinal) ||
        name.StartsWith("Widow", StringComparison.Ordinal);

    private static bool IsLayoutOrDesignAction(string name) =>
        name.StartsWith("Columns", StringComparison.Ordinal) ||
        name.StartsWith("CustomMargins", StringComparison.Ordinal) ||
        name.StartsWith("CustomParagraphSpacing", StringComparison.Ordinal) ||
        name.StartsWith("Customize", StringComparison.Ordinal) ||
        name.StartsWith("Hyphenation", StringComparison.Ordinal) ||
        name.StartsWith("LineNumbers", StringComparison.Ordinal) ||
        name.StartsWith("Margins", StringComparison.Ordinal) ||
        name.StartsWith("MorePaperSizes", StringComparison.Ordinal) ||
        name.StartsWith("Orientation", StringComparison.Ordinal) ||
        name.StartsWith("PageColor", StringComparison.Ordinal) ||
        name.StartsWith("PageSetup", StringComparison.Ordinal) ||
        name.StartsWith("PageValign", StringComparison.Ordinal) ||
        name.StartsWith("ResetStyleSet", StringComparison.Ordinal) ||
        name.StartsWith("SectionBreak", StringComparison.Ordinal) ||
        name.StartsWith("Size", StringComparison.Ordinal) ||
        name.StartsWith("Theme", StringComparison.Ordinal) ||
        name.StartsWith("Watermark", StringComparison.Ordinal);

    private static bool IsInsertAction(string name) =>
        name.StartsWith("BlankPage", StringComparison.Ordinal) ||
        name.StartsWith("CoverPage", StringComparison.Ordinal) ||
        name.StartsWith("Cc", StringComparison.Ordinal) ||
        name.StartsWith("Datetime", StringComparison.Ordinal) ||
        name.StartsWith("DropCap", StringComparison.Ordinal) ||
        name.StartsWith("Equation", StringComparison.Ordinal) ||
        name.StartsWith("Field", StringComparison.Ordinal) ||
        name.StartsWith("Header", StringComparison.Ordinal) ||
        name.StartsWith("Footer", StringComparison.Ordinal) ||
        name.StartsWith("Hf", StringComparison.Ordinal) ||
        name.StartsWith("HorizontalRule", StringComparison.Ordinal) ||
        name.StartsWith("Hyperlink", StringComparison.Ordinal) ||
        name.StartsWith("InsertFile", StringComparison.Ordinal) ||
        name.StartsWith("InsertIcon", StringComparison.Ordinal) ||
        name.StartsWith("Link", StringComparison.Ordinal) ||
        name.StartsWith("PageBreak", StringComparison.Ordinal) ||
        name.StartsWith("PageNumber", StringComparison.Ordinal) ||
        name.StartsWith("Picture", StringComparison.Ordinal) ||
        name.StartsWith("Quickpart", StringComparison.Ordinal) ||
        name.StartsWith("SaveQuickpart", StringComparison.Ordinal) ||
        name.StartsWith("ScreenClipping", StringComparison.Ordinal) ||
        name.StartsWith("Symbol", StringComparison.Ordinal);
}
