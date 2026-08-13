using Free.Shared.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.ContextMenus;

public sealed record FreeWEditorContextMenuState(
    bool CanUndo,
    bool CanRedo,
    bool HasSelection,
    bool CanPaste,
    bool CanEdit);

public sealed record FreeWSpellingContextMenuState(
    ProofingDiagnostic Diagnostic,
    bool CanEdit,
    bool CanIgnore,
    bool CanAddToDictionary);

public enum FreeWContextMenuCoverage
{
    Paired,
    ExternalOnly,
}

public sealed record FreeWContextMenuInventoryEntry(
    string Id,
    string SemanticSurface,
    string WpfAuthority,
    string AvaloniaCounterpart,
    FreeWContextMenuCoverage Coverage,
    bool IsExplicitWpfContextMenu);

/// <summary>
/// Portable menu plans for every FreeW menu presented through a WPF ContextMenu, plus the portable
/// editing-command core of WPF RichTextBox's framework-owned context menu.
/// </summary>
public static class FreeWContextMenuPlanner
{
    public const string EditorUndo = "freew.context.editor.undo";
    public const string EditorRedo = "freew.context.editor.redo";
    public const string EditorCut = "freew.context.editor.cut";
    public const string EditorCopy = "freew.context.editor.copy";
    public const string EditorPaste = "freew.context.editor.paste";
    public const string EditorDelete = "freew.context.editor.delete";
    public const string EditorSelectAll = "freew.context.editor.select-all";
    public const string EditorSpellingIgnoreAll = "freew.context.editor.spelling.ignore-all";
    public const string EditorSpellingAddToDictionary = "freew.context.editor.spelling.add-to-dictionary";
    public const string EditorSpellingReplacementPrefix = "freew.context.editor.spelling.replace.";

    public const string OutlineMoveUp = "freew.context.outline.move-up";
    public const string OutlineMoveDown = "freew.context.outline.move-down";
    public const string OutlinePromote = "freew.context.outline.promote";
    public const string OutlineDemote = "freew.context.outline.demote";
    public const string OutlineCollapse = "freew.context.outline.collapse";
    public const string OutlineExpand = "freew.context.outline.expand";

    public const string ContentChoicePrefix = "freew.context.content-choice.";
    public const string ContentDatePrefix = "freew.context.content-date.";
    public const string FindSpecialPrefix = "freew.context.find-special.";
    public const string ParagraphSpacingPrefix = "freew.context.paragraph-spacing.";
    public const string EffectsPrefix = "freew.context.effects.";
    public const string TableStylesPrefix = "freew.context.table-styles.";

    public static readonly IReadOnlyList<(string Label, string Insert)> FindSpecialCharacters =
    [
        ("Paragraph Mark  (^p / \\n)", "\n"),
        ("Tab Character  (^t / \\t)", "\t"),
        ("Any Character  (?)", "?"),
        ("Any Digit  ([0-9])", "[0-9]"),
        ("Any Letter  ([A-Za-z])", "[A-Za-z]"),
        ("Beginning of Word  (<)", "<"),
        ("End of Word  (>)", ">"),
        ("Em Dash  (\u2014)", "\u2014"),
        ("En Dash  (\u2013)", "\u2013"),
    ];

    public static readonly IReadOnlyList<FreeWContextMenuInventoryEntry> Inventory =
    [
        new("editor", "Rich text editor commands", "WPF RichTextBox framework menu", "Avalonia DocumentView ContextMenu", FreeWContextMenuCoverage.Paired, false),
        new("content-choice", "Drop-down/combo content-control choices", "Editing/DocumentView.cs", "Editing/DocumentView.cs", FreeWContextMenuCoverage.Paired, true),
        new("content-date", "Date content-control relative dates", "Editing/DocumentView.cs", "Editing/DocumentView.cs", FreeWContextMenuCoverage.Paired, true),
        new("outline", "Navigation outline restructuring", "MainWindow.cs", "NavigationPane.cs", FreeWContextMenuCoverage.Paired, true),
        new("find-special", "Find/Replace special-character insertion", "FindReplaceDialog.cs", "FindReplaceDialog.cs", FreeWContextMenuCoverage.Paired, true),
        new("paragraph-spacing", "Design paragraph-spacing catalog", "Ribbon/ThemeGallery.cs", "FreeWAvaloniaRibbonDefinition.cs", FreeWContextMenuCoverage.Paired, true),
        new("effects", "Design effects catalog", "Ribbon/ThemeGallery.cs", "FreeWAvaloniaRibbonDefinition.cs", FreeWContextMenuCoverage.Paired, true),
        new("table-styles", "Table Styles catalog", "Ribbon/TableStylesGallery.cs", "FreeWAvaloniaRibbonDefinition.cs", FreeWContextMenuCoverage.Paired, true),
        new("editor-spelling", "Portable spelling suggestions and dictionary actions for planner diagnostics", "WPF RichTextBox/Windows spell checker", "ProofingDiagnosticPlanner correction catalog", FreeWContextMenuCoverage.Paired, false),
        new("editor-spelling-native", "Native OS spelling coverage beyond planner diagnostics", "WPF RichTextBox/Windows spell checker", "No portable OS dictionary provider", FreeWContextMenuCoverage.ExternalOnly, false),
    ];

    public static RibbonMenu BuildEditor(
        FreeWEditorContextMenuState state,
        FreeWSpellingContextMenuState? spelling = null)
    {
        var items = new List<RibbonMenuItem>();
        if (spelling is not null && spelling.Diagnostic.Kind == ProofingDiagnosticKind.Spelling)
            items.AddRange(BuildSpelling(spelling).Items);

        items.AddRange(
        [
            Command("Undo", EditorUndo, state.CanUndo),
            Command("Redo", EditorRedo, state.CanRedo),
            RibbonMenuItem.Separator(),
            Command("Cut", EditorCut, state.HasSelection && state.CanEdit),
            Command("Copy", EditorCopy, state.HasSelection),
            Command("Paste", EditorPaste, state.CanPaste && state.CanEdit),
            Command("Delete", EditorDelete, state.HasSelection && state.CanEdit),
            RibbonMenuItem.Separator(),
            Command("Select All", EditorSelectAll, true),
        ]);
        return new RibbonMenu(items);
    }

    public static RibbonMenu BuildSpelling(FreeWSpellingContextMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Diagnostic.Kind != ProofingDiagnosticKind.Spelling)
            return RibbonMenu.Empty;

        var items = new List<RibbonMenuItem>();
        var suggestions = ProofingCorrectionCatalog.SuggestionsFor(state.Diagnostic.Word);
        for (var index = 0; index < suggestions.Count; index++)
        {
            items.Add(Command(
                suggestions[index],
                EditorSpellingReplacementPrefix + index,
                state.CanEdit));
        }

        if (items.Count > 0)
            items.Add(RibbonMenuItem.Separator());

        items.Add(Command("Ignore All", EditorSpellingIgnoreAll, state.CanIgnore));
        items.Add(Command("Add to Dictionary", EditorSpellingAddToDictionary, state.CanAddToDictionary));
        items.Add(RibbonMenuItem.Separator());
        return new RibbonMenu(items);
    }

    public static RibbonMenu BuildContentControl(Run run, DateTime? today = null, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Control is not { } control)
            return RibbonMenu.Empty;

        if (control.Kind is ContentControlKind.DropDownList or ContentControlKind.ComboBox)
        {
            return IndexedMenu(control.Items.Select(item => item.DisplayText).ToArray(), ContentChoicePrefix,
                checkedIndex: IndexOf(control.Items.Select(item => item.DisplayText), run.Text), isEnabled: isEnabled);
        }

        if (control.Kind == ContentControlKind.DatePicker)
        {
            var choices = ContentControlInteractionPlanner.RelativeDateChoices(control, today);
            return IndexedMenu(choices.Select(choice => $"{choice.Label} ({choice.DisplayText})").ToArray(), ContentDatePrefix,
                checkedIndex: IndexOf(choices.Select(choice => choice.DisplayText), run.Text), isEnabled: isEnabled);
        }

        return RibbonMenu.Empty;
    }

    public static Run? ApplyContentControlCommand(
        Run run,
        RibbonCommandId commandId,
        DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (TryParseIndex(commandId, ContentChoicePrefix, out var choiceIndex))
            return ContentControlInteractionPlanner.SelectItem(run, choiceIndex);
        if (TryParseIndex(commandId, ContentDatePrefix, out var dateIndex))
            return ContentControlInteractionPlanner.SelectRelativeDate(run, dateIndex, today);
        return null;
    }

    public static RibbonMenu BuildOutline(IReadOnlyList<Block> blocks, int blockIndex, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var paragraph = blockIndex >= 0 && blockIndex < blocks.Count ? blocks[blockIndex] as Paragraph : null;
        var isHeading = paragraph is not null && DocumentOutline.TryGetLevel(paragraph.StyleId, out _);
        var canMoveUp = isHeading && !ReferenceEquals(OutlineTools.MoveSubtree(blocks, blockIndex, true), blocks);
        var canMoveDown = isHeading && !ReferenceEquals(OutlineTools.MoveSubtree(blocks, blockIndex, false), blocks);
        var canPromote = isHeading && !string.Equals(OutlineTools.Promote(paragraph!.StyleId), paragraph.StyleId, StringComparison.Ordinal);
        var canDemote = isHeading && !string.Equals(OutlineTools.Demote(paragraph!.StyleId), paragraph.StyleId, StringComparison.Ordinal);

        return new RibbonMenu(
        [
            Command("Move Up", OutlineMoveUp, canMoveUp),
            Command("Move Down", OutlineMoveDown, canMoveDown),
            RibbonMenuItem.Separator(),
            Command("Promote", OutlinePromote, canPromote),
            Command("Demote", OutlineDemote, canDemote),
            RibbonMenuItem.Separator(),
            Command("Collapse", OutlineCollapse, isHeading && !isCollapsed),
            Command("Expand", OutlineExpand, isHeading && isCollapsed),
        ]);
    }

    public static RibbonMenu BuildFindSpecial() => IndexedMenu(
        FindSpecialCharacters.Select(item => item.Label).ToArray(), FindSpecialPrefix);

    public static RibbonMenu BuildParagraphSpacing(string? currentName = null) => IndexedMenu(
        DocumentParagraphSpacingSet.Catalog.Select(item => item.Name).ToArray(),
        ParagraphSpacingPrefix,
        IndexOf(DocumentParagraphSpacingSet.Catalog.Select(item => item.Name), currentName));

    public static RibbonMenu BuildEffects(string? currentName = null) => IndexedMenu(
        DocumentEffectSet.Catalog.Select(item => item.Name).ToArray(),
        EffectsPrefix,
        IndexOf(DocumentEffectSet.Catalog.Select(item => item.Name), currentName));

    public static RibbonMenu BuildTableStyles(string? currentStyleId = null) => IndexedMenu(
        DocumentTableStyle.Catalog.Select(item => item.Name).ToArray(),
        TableStylesPrefix,
        IndexOf(DocumentTableStyle.Catalog.Select(item => item.WordStyleId), currentStyleId));

    public static bool TryParseIndex(RibbonCommandId commandId, string prefix, out int index)
    {
        index = -1;
        var value = commandId.Value;
        return value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(prefix.Length), out index)
            && index >= 0;
    }

    private static RibbonMenu IndexedMenu(IReadOnlyList<string> headers, string commandPrefix, int checkedIndex = -1, bool isEnabled = true) =>
        new(headers.Select((header, index) =>
            new RibbonMenuItem(header, new RibbonCommandId(commandPrefix + index))
            {
                IsChecked = checkedIndex >= 0 ? index == checkedIndex : null,
                IsEnabled = isEnabled,
            }).ToArray());

    private static RibbonMenuItem Command(string header, string id, bool enabled) =>
        new(header, new RibbonCommandId(id)) { IsEnabled = enabled };

    private static int IndexOf(IEnumerable<string> values, string? selected)
    {
        if (selected is null)
            return -1;
        var index = 0;
        foreach (var value in values)
        {
            if (string.Equals(value, selected, StringComparison.Ordinal))
                return index;
            index++;
        }
        return -1;
    }
}
